using System.Text.Json;
using System.Text.Json.Nodes;

namespace Practice;

public record TestCase(string Name, JsonArray Args, JsonNode? Expected, bool Example = false);

public record Problem(
    int Id, string Slug, string Title, string Lesson, string Description, string Method,
    string Starter, string TracePrompt, string Compare, List<TestCase> Tests, List<string>? Generated)
{
    public string Key => $"{Id}-{Slug}";
}

public static class Problems
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Dictionary<int, Problem> Load()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "problems");
        return Directory.GetFiles(dir, "*.json")
            .Select(f => JsonSerializer.Deserialize<Problem>(File.ReadAllText(f), Json)
                         ?? throw new InvalidDataException(f))
            .ToDictionary(p => p.Id);
    }

    public static List<TestCase> TestsFor(Problem p, bool submit)
    {
        var tests = p.Tests.Where(t => submit || t.Example).ToList();
        if (submit)
            tests.AddRange((p.Generated ?? []).Select(Generators.Create));
        return tests;
    }
}

/// <summary>Large inputs, built in code so they don't have to be stored.</summary>
public static class Generators
{
    public static TestCase Create(string name) => name switch
    {
        "two-sum-large" => TwoSum(200_000),
        "anagram-large" => Anagram(200_000),
        "palindrome-large" => Case("Large palindrome", true, new string('a', 100_000) + "," + new string('A', 100_000)),
        "group-large" => Group(10_000),
        "consecutive-long-run" => LongRun(100_000),
        "consecutive-duplicates" => Duplicates(50_000),
        _ => throw new InvalidDataException($"Unknown generator '{name}'")
    };

    static TestCase Case(string name, object expected, params object[] args) =>
        new(name, new JsonArray(args.Select(a => JsonSerializer.SerializeToNode(a)).ToArray()),
            JsonSerializer.SerializeToNode(expected));

    // Every number is even except the last, so only one pair has an odd sum.
    static TestCase TwoSum(int n)
    {
        var nums = Enumerable.Range(0, n).Select(i => 2 * i).ToArray();
        nums[n - 1] = 1;
        return Case("Large input: O(n²) is too slow", new[] { n - 2, n - 1 }, nums, nums[n - 2] + 1);
    }

    static TestCase Anagram(int n)
    {
        var s = string.Create(n, 0, (span, _) => { for (int i = 0; i < span.Length; i++) span[i] = (char)('a' + i % 26); });
        var t = s.ToCharArray();
        Array.Reverse(t);
        return Case("Large input", true, s, new string(t));
    }

    static TestCase Group(int pairs)
    {
        var words = new List<string>();
        var expected = new List<string[]>();
        for (int a = 0; a < 26; a++)
        for (int b = a + 1; b < 26; b++)
        for (int c = b + 1; c < 26; c++)
        for (int d = c + 1; d < 26; d++)
        {
            if (expected.Count == pairs) break;
            var chars = new[] { (char)('a' + a), (char)('a' + b), (char)('a' + c), (char)('a' + d) };
            var word = new string(chars);
            Array.Reverse(chars);
            expected.Add([word, new string(chars)]);
        }
        words.AddRange(expected.Select(p => p[0]));
        words.AddRange(expected.Select(p => p[1]));
        return Case("Many groups: comparing every pair is too slow", expected, words);
    }

    static TestCase LongRun(int n)
    {
        var nums = Enumerable.Range(0, n).ToArray();
        new Random(42).Shuffle(nums);
        return Case("One long run: counting from every number is too slow", n, nums);
    }

    static TestCase Duplicates(int n)
    {
        var nums = Enumerable.Repeat(1, n).Concat(Enumerable.Range(2, n - 1)).ToArray();
        return Case("Many copies of the start: counting each copy is too slow", n, nums);
    }
}
