namespace Practice;

/// <summary>
/// Checks the tests themselves. Files are named "{id}.{label}.{expected status}.cs",
/// for example "128.no-start-check.time-limit.cs".
/// </summary>
public static class Verify
{
    public static async Task<int> RunAsync(string dir)
    {
        var problems = Problems.Load();
        var withReference = new HashSet<int>();
        int failures = 0;

        foreach (var file in Directory.GetFiles(dir, "*.cs").Order())
        {
            var parts = Path.GetFileNameWithoutExtension(file).Split('.');
            int id = int.Parse(parts[0]);
            var expected = parts[^1];
            var result = await Judge.RunAsync(problems[id], File.ReadAllText(file), submit: true);
            bool ok = result.Status == expected;
            if (!ok) failures++;
            if (ok && expected == "passed") withReference.Add(id);

            var slowest = result.Tests.Count == 0 ? 0 : result.Tests.Max(t => t.Ms);
            Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {Path.GetFileName(file)}: {result.Status}, {result.Passed}/{result.Tests.Count} passed, slowest {slowest} ms");
            if (!ok)
            {
                foreach (var d in result.Diagnostics) Console.WriteLine($"       line {d.Line}: {d.Message}");
                foreach (var t in result.Tests.Where(t => t.Status != "passed")) Console.WriteLine($"       {t.Name}: {t.Status} {t.Error} expected {t.Expected} got {t.Actual}");
            }
        }
        foreach (var p in problems.Values.Where(p => !withReference.Contains(p.Id)))
        {
            failures++;
            Console.WriteLine($"FAIL {p.Title} has no passing reference solution");
        }
        Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}
