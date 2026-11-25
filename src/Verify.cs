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
            Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {Path.GetFileName(file)}: {result.Status}, {result.Passed}/{result.Total} passed, slowest {slowest} ms");
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
        foreach (var (name, ok) in await UnitChecks(problems))
        {
            if (!ok) failures++;
            Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}");
        }
        Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    static async Task<List<(string, bool)>> UnitChecks(Dictionary<int, Problem> problems)
    {
        var day = new DateOnly(2026, 1, 1);
        var first = Reviews.Solved(null, day);
        var started = first with { Started = true };
        var second = Reviews.Solved(started, day.AddDays(3));
        var late = Reviews.Solved(started, day.AddDays(10));
        var done = Reviews.Solved(second with { Started = true }, day.AddDays(7));
        var (good, goodError) = CustomTests.Parse(problems[1], [new("[1, 2]\n3", "[0, 1]"), new("", "")]);
        var (_, badJson) = CustomTests.Parse(problems[1], [new("[1, 2\n3", "[0, 1]")]);
        var (_, badCount) = CustomTests.Parse(problems[1], [new("[1, 2]", "[0, 1]")]);
        var (_, noGuess) = CustomTests.Parse(problems[1], [new("[1, 2]\n3", "")]);
        var (wrongGuess, _) = CustomTests.Parse(problems[128], [new("[1, 2]", "99")]);
        var reference = "public int LongestConsecutive(int[] n) { return n.Distinct().Count(x => !n.Contains(x)) + Run(n); } " +
                        "int Run(int[] n) { int m = 0; foreach (int x in n) { int l = 1; while (n.Contains(x + l)) l++; m = Math.Max(m, l); } return m; }";
        var judged = await Judge.RunAsync(problems[128], reference, submit: false, wrongGuess);
        return
        [
            ("first solve is due in 3 days", first.Due == day.AddDays(3)),
            ("solving again without starting a review changes nothing", Reviews.Solved(first, day.AddDays(3)) == first),
            ("first review leads to day 7", second.Due == day.AddDays(7) && !second.Started),
            ("a late first review still leaves 3 days", late.Due == day.AddDays(13)),
            ("second review finishes", done.Due is null),
            ("own test is parsed and an empty one skipped", goodError is null && good.Count == 1 && good[0].Args.Count == 2),
            ("own test with bad JSON is refused", badJson is not null),
            ("own test with a missing parameter is refused", badCount is not null),
            ("own test needs a prediction", noGuess is not null),
            ("a wrong prediction doesn't fail the solution", judged.Status == "passed" && judged.Tests[^1] is { Custom: true, Status: "wrong-answer" })
        ];
    }
}