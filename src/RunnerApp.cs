namespace Practice;

public record RunRequest(int ProblemId, string Code, bool Submit);

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
            try { return Results.Json(await Judge.RunAsync(problem, r.Code, r.Submit), Problems.Json); }
            finally { gate.Release(); }
        });
        app.Run();
    }
}
