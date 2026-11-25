using System.Text.Json;
using System.Text.Json.Nodes;

namespace Practice;

/// <summary>When to solve a problem again: 3 and 7 days after the first time.</summary>
public record Review(DateOnly FirstSolved, int Stage, DateOnly? Due, bool Started = false);

public static class Reviews
{
    // Only a solve that began with "Start review", from a blank editor, moves the schedule on.
    public static Review Solved(Review? review, DateOnly today)
    {
        if (review is null) return new Review(today, 0, today.AddDays(3));
        if (!review.Started) return review;
        if (review.Stage > 0) return review with { Stage = 2, Due = null, Started = false };

        var daySeven = review.FirstSolved.AddDays(7);
        var soonest = today.AddDays(3);
        return review with { Stage = 1, Due = daySeven > soonest ? daySeven : soonest, Started = false };
    }
}

/// <summary>A test written by the user: one JSON value per line, plus the predicted answer.</summary>
public record CustomTest(string Input, string Expected);

public static class CustomTests
{
    public static (List<TestCase> Tests, string? Error) Parse(Problem problem, List<CustomTest>? customs)
    {
        var tests = new List<TestCase>();
        int count = problem.Tests[0].Args.Count, number = 0;
        foreach (var custom in customs ?? [])
        {
            number++;
            var lines = custom.Input.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 && string.IsNullOrWhiteSpace(custom.Expected)) continue;
            if (custom.Input.Length > 20_000 || number > 10) return ([], "Use up to 10 own tests of up to 20,000 characters.");
            if (string.IsNullOrWhiteSpace(custom.Expected)) return ([], $"Own test {number}: write your predicted answer first.");
            if (lines.Length != count) return ([], $"Own test {number}: write {count} input line(s), one per parameter.");

            var args = new JsonArray();
            foreach (var line in lines)
            {
                if (!TryParse(line, out var value)) return ([], $"Own test {number}: \"{line}\" isn't valid. Write values like [1, 2], 5 or \"text\".");
                args.Add(value);
            }
            if (!TryParse(custom.Expected, out var expected)) return ([], $"Own test {number}: the predicted answer isn't valid. Write values like [1, 2], 5, true or \"text\".");
            tests.Add(new TestCase($"Own test {number}", args, expected, Custom: true));
        }
        return (tests, null);
    }

    static bool TryParse(string text, out JsonNode? value)
    {
        try { value = JsonNode.Parse(text); return true; }
        catch (JsonException) { value = null; return false; }
    }
}
