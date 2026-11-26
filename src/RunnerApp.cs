namespace Practice;

public record RunRequest(int ProblemId, string Code, bool Submit, List<TestCase>? Custom, TestCase? DebugTest = null);

public static class RunnerApp
{
    public static void Run()
    {
        var app = WebApplication.CreateBuilder().Build();
        var problems = Problems.Load();
        var gate = new SemaphoreSlim(1); // one run at a time keeps timings stable

        app.MapPost("/run", async (RunRequest r) =>
        {
            if (!problems.TryGetValue(r.ProblemId, out var problem)) return Results.NotFound();
            await gate.WaitAsync();
            try
            {
                var result = r.DebugTest is { } test
                    ? await Judge.DebugAsync(problem, r.Code, test)
                    : await Judge.RunAsync(problem, r.Code, r.Submit, r.Custom);
                return Results.Json(result, Problems.Json);
            }
            finally { gate.Release(); }
        });
        app.Run();
    }
}
