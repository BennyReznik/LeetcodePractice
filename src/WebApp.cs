using System.Text.Json;

namespace Practice;

public record Work(string Code, string Time, string Space, string Trace, int Seconds, int Revision,
    List<List<string>>? Rows = null, List<CustomTest>? Custom = null);
public record RunInput(Work Work, bool Submit, DateOnly Today);
public record StartReviewInput(int Revision, DateOnly Today);
public record DebugInput(Work Work, string Test);

public static class WebApp
{
    static readonly JsonSerializerOptions Pretty = new(Problems.Json) { WriteIndented = true };

    public static void Run()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHttpClient("runner", c =>
        {
            c.BaseAddress = new Uri(Environment.GetEnvironmentVariable("RUNNER_URL") ?? "http://runner:8080");
            c.Timeout = TimeSpan.FromSeconds(60);
        });
        var app = builder.Build();
        var problems = Problems.Load();
        var root = Environment.GetEnvironmentVariable("WORK_DIR") ?? "/work";
        var saveLock = new Lock();

        // Only this computer's browser pages may use the API, not other websites.
        app.Use(async (ctx, next) =>
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (ctx.Request.Host.Host is not ("localhost" or "127.0.0.1") ||
                (origin != "" && origin != $"http://{ctx.Request.Host}"))
            {
                ctx.Response.StatusCode = 403;
                return;
            }
            ctx.Response.Headers.CacheControl = "no-cache";
            await next();
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();

        string Dir(Problem p) => Directory.CreateDirectory(Path.Combine(root, p.Key)).FullName;

        Work LoadWork(Problem p)
        {
            var notes = Path.Combine(Dir(p), "notes.json");
            var code = Path.Combine(Dir(p), "Solution.cs");
            var work = File.Exists(notes)
                ? JsonSerializer.Deserialize<Work>(File.ReadAllText(notes), Problems.Json)!
                : new Work("", "", "", "", 0, 0);
            return work with { Code = File.Exists(code) ? File.ReadAllText(code) : p.Starter };
        }

        // Returns null if a newer copy was saved elsewhere, for example in another tab.
        Work? SaveWork(Problem p, Work work)
        {
            lock (saveLock)
            {
                if (LoadWork(p).Revision != work.Revision) return null;
                var saved = work with { Revision = work.Revision + 1 };
                Write(Path.Combine(Dir(p), "Solution.cs"), saved.Code);
                Write(Path.Combine(Dir(p), "notes.json"), JsonSerializer.Serialize(saved with { Code = "" }, Pretty));
                return saved;
            }
        }

        void Write(string path, string text)
        {
            File.WriteAllText(path + ".tmp", text);
            File.Move(path + ".tmp", path, overwrite: true);
        }

        string ReviewPath(Problem p) => Path.Combine(root, p.Key, "review.json");

        Review? LoadReview(Problem p) => File.Exists(ReviewPath(p))
            ? JsonSerializer.Deserialize<Review>(File.ReadAllText(ReviewPath(p)), Problems.Json)
            : null;

        void SaveReview(Problem p, Review review) => Write(ReviewPath(p), JsonSerializer.Serialize(review, Pretty));

        string StatusOf(Problem p, DateOnly today) => LoadReview(p) switch
        {
            { } r when r.Due <= today => "due",
            not null => "solved",
            _ => File.Exists(Path.Combine(root, p.Key, "Solution.cs")) ? "started" : "new"
        };

        app.MapGet("/api/problems", (DateOnly today) => problems.Values.OrderBy(p => p.Lesson, StringComparer.Ordinal).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.Title, p.Lesson, Status = StatusOf(p, today), LoadReview(p)?.Due }));

        app.MapGet("/api/problems/{id:int}", (int id) => !problems.TryGetValue(id, out var p)
            ? Results.NotFound()
            : Results.Ok(new { p.Id, p.Title, p.Lesson, p.Description, p.Starter, p.TracePrompt, p.TraceColumns, p.TraceRows,
                ExampleInput = string.Join("\n", p.Tests[0].Args.Select(a => a?.ToJsonString() ?? "null")),
                ExampleAnswer = p.Tests[0].Expected?.ToJsonString() ?? "null",
                Examples = p.Tests.Where(t => t.Example).Select(t => $"{t.Name}: {string.Join(", ", t.Args.Select(a => a?.ToJsonString()))}"),
                Review = LoadReview(p), Work = LoadWork(p) }));

        // Opens a blank editor. The old solution and notes are kept in "attempts".
        app.MapPost("/api/problems/{id:int}/start-review", (int id, StartReviewInput input) =>
        {
            if (!problems.TryGetValue(id, out var p)) return Results.NotFound();
            lock (saveLock)
            {
                if (LoadReview(p) is not { } review || !(review.Due <= input.Today))
                    return Results.BadRequest(new { error = "This problem is not due for review." });
                var old = LoadWork(p);
                if (old.Revision != input.Revision) return Results.Conflict();
                if (old.Code != p.Starter)
                {
                    var attempts = Directory.CreateDirectory(Path.Combine(Dir(p), "attempts")).FullName;
                    Write(Path.Combine(attempts, $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-before-review.json"), JsonSerializer.Serialize(old, Pretty));
                }
                SaveWork(p, new Work(p.Starter, "", "", "", 0, old.Revision, null, old.Custom));
                SaveReview(p, review with { Started = true });
            }
            return Results.Ok();
        });

        app.MapPut("/api/problems/{id:int}/work", (int id, Work work) =>
        {
            if (!problems.TryGetValue(id, out var p)) return Results.NotFound();
            return SaveWork(p, work) is { } saved ? Results.Ok(new { saved.Revision }) : Results.Conflict();
        });

        app.MapPost("/api/problems/{id:int}/run", async (int id, RunInput input, IHttpClientFactory http) =>
        {
            if (!problems.TryGetValue(id, out var p)) return Results.NotFound();
            if (input.Submit && new[] { input.Work.Time, input.Work.Space }.Any(string.IsNullOrWhiteSpace))
                return Results.BadRequest(new { error = "Fill in both complexities before submitting." });
            var fullRows = (input.Work.Rows ?? []).Count(r => r.Count == p.TraceColumns.Count && r.All(c => !string.IsNullOrWhiteSpace(c)));
            if (input.Submit && fullRows < p.TraceRows)
                return Results.BadRequest(new { error = $"Fill in at least {p.TraceRows} complete trace rows before submitting. Use - where a value does not apply." });
            var (custom, customError) = CustomTests.Parse(p, input.Work.Custom);
            if (customError is not null) return Results.BadRequest(new { error = customError });
            if (SaveWork(p, input.Work) is not { } saved) return Results.Conflict();

            JudgeResult result;
            try
            {
                var response = await http.CreateClient("runner").PostAsJsonAsync("/run", new RunRequest(id, saved.Code, input.Submit, custom), Problems.Json);
                response.EnsureSuccessStatusCode();
                result = (await response.Content.ReadFromJsonAsync<JudgeResult>(Problems.Json))!;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                return Results.Json(new { error = "The code runner isn't available. Try: docker compose restart runner", saved.Revision }, statusCode: 503);
            }

            var attempt = JsonSerializer.Serialize(new
            {
                Problem = p.Title, At = DateTime.UtcNow, input.Submit, result.Status, result.Passed, result.Total,
                saved.Time, saved.Space, p.TraceColumns, saved.Rows, Notes = saved.Trace, saved.Seconds, saved.Code, result.Diagnostics, result.Tests
            }, Pretty);
            var attempts = Directory.CreateDirectory(Path.Combine(Dir(p), "attempts")).FullName;
            if (input.Submit) Write(Path.Combine(attempts, $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json"), attempt);
            Write(Path.Combine(root, "latest.json"), attempt);
            if (input.Submit && result.Status == "passed")
                lock (saveLock) SaveReview(p, Reviews.Solved(LoadReview(p), input.Today));
            return Results.Json(new { saved.Revision, Result = result, Review = LoadReview(p) }, Problems.Json);
        });

        // Test ids: "example-0" for the first example, "own-0" for the first own test.
        app.MapPost("/api/problems/{id:int}/debug", async (int id, DebugInput input, IHttpClientFactory http) =>
        {
            if (!problems.TryGetValue(id, out var p)) return Results.NotFound();
            var (custom, customError) = CustomTests.Parse(p, input.Work.Custom);
            var parts = input.Test.Split('-');
            var list = parts[0] == "own" ? custom : p.Tests.Where(t => t.Example).ToList();
            if (parts.Length != 2 || !int.TryParse(parts[1], out int index) || index < 0 || index >= list.Count)
                return Results.BadRequest(new { error = customError ?? "Choose a test to debug." });
            if (SaveWork(p, input.Work) is not { } saved) return Results.Conflict();

            try
            {
                var response = await http.CreateClient("runner").PostAsJsonAsync("/run",
                    new RunRequest(id, saved.Code, false, null, list[index]), Problems.Json);
                response.EnsureSuccessStatusCode();
                var result = (await response.Content.ReadFromJsonAsync<JudgeResult>(Problems.Json))!;
                return Results.Json(new { saved.Revision, Result = result }, Problems.Json);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                return Results.Json(new { error = "The code runner isn't available. Try: docker compose restart runner", saved.Revision }, statusCode: 503);
            }
        });

        app.Run();
    }
}
