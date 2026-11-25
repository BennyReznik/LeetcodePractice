using System.Text.Json;

namespace Practice;

public record Work(string Code, string Time, string Space, string Trace, int Seconds, int Revision);
public record RunInput(Work Work, bool Submit);

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

        string StatusOf(Problem p)
        {
            var solved = Path.Combine(root, p.Key, "solved.txt");
            return File.Exists(solved) ? "solved" : File.Exists(Path.Combine(root, p.Key, "Solution.cs")) ? "started" : "new";
        }

        app.MapGet("/api/problems", () => problems.Values.OrderBy(p => p.Lesson, StringComparer.Ordinal).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.Title, p.Lesson, Status = StatusOf(p) }));

        app.MapGet("/api/problems/{id:int}", (int id) => !problems.TryGetValue(id, out var p)
            ? Results.NotFound()
            : Results.Ok(new { p.Id, p.Title, p.Lesson, p.Description, p.Starter, p.TracePrompt, Work = LoadWork(p) }));

        app.MapPut("/api/problems/{id:int}/work", (int id, Work work) =>
        {
            if (!problems.TryGetValue(id, out var p)) return Results.NotFound();
            return SaveWork(p, work) is { } saved ? Results.Ok(new { saved.Revision }) : Results.Conflict();
        });

        app.MapPost("/api/problems/{id:int}/run", async (int id, RunInput input, IHttpClientFactory http) =>
        {
            if (!problems.TryGetValue(id, out var p)) return Results.NotFound();
            if (input.Submit && new[] { input.Work.Time, input.Work.Space, input.Work.Trace }.Any(string.IsNullOrWhiteSpace))
                return Results.BadRequest(new { error = "Fill in both complexities and your trace before submitting." });
            if (SaveWork(p, input.Work) is not { } saved) return Results.Conflict();

            JudgeResult result;
            try
            {
                var response = await http.CreateClient("runner").PostAsJsonAsync("/run", new RunRequest(id, saved.Code, input.Submit), Problems.Json);
                response.EnsureSuccessStatusCode();
                result = (await response.Content.ReadFromJsonAsync<JudgeResult>(Problems.Json))!;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                return Results.Json(new { error = "The code runner isn't available. Try: docker compose restart runner", saved.Revision }, statusCode: 503);
            }

            var attempt = JsonSerializer.Serialize(new
            {
                Problem = p.Title, At = DateTime.UtcNow, input.Submit, result.Status, result.Passed, Total = result.Tests.Count,
                saved.Time, saved.Space, saved.Trace, saved.Seconds, saved.Code, result.Diagnostics, result.Tests
            }, Pretty);
            var attempts = Directory.CreateDirectory(Path.Combine(Dir(p), "attempts")).FullName;
            if (input.Submit) Write(Path.Combine(attempts, $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json"), attempt);
            Write(Path.Combine(root, "latest.json"), attempt);
            if (input.Submit && result.Status == "passed")
                Write(Path.Combine(Dir(p), "solved.txt"), DateTime.UtcNow.ToString("O"));
            return Results.Json(new { saved.Revision, Result = result }, Problems.Json);
        });

        app.Run();
    }
}
