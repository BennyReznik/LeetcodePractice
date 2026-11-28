using System.Text.Json;
using System.Text.Json.Nodes;

namespace Practice;

public record TestCase(string Name, JsonArray Args, JsonNode? Expected, bool Example = false, bool Custom = false);

public record Problem(
    int Id, string Slug, string Title, string Lesson, string Description, string Method,
    string Starter, string TracePrompt, string Compare, List<TestCase> Tests, List<string>? Generated,
    List<string> TraceColumns, int TraceRows, int? ResultArg, int Order = 0) // Order: teaching order within the lesson
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
        "two-sum-sorted-large" => TwoSumSorted(200_000),
        "move-zeroes-large" => MoveZeroes(200_000),
        "three-sum-large" => ThreeSum(3_000),
        "three-sum-zeroes" => Case("Many zeroes", new[] { new[] { 0, 0, 0 } }, new int[3_000]),
        "container-large" => Container(200_000),
        "average-large" => Average(200_000, 50_000),
        "stock-large" => Stock(200_000),
        "substring-large" => Case("Large input: checking every substring is too slow",
            95, new string(Enumerable.Range(0, 200_000).Select(i => (char)(' ' + i % 95)).ToArray())),
        "min-subarray-large" => Case("Large input: restarting from each start is too slow", 100_000, 100_000, Enumerable.Repeat(1, 200_000).ToArray()),
        "replacement-large" => Replacement(200_000, 1_000),
        "permutation-large" => Permutation(10_000, 190_000),
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

    // Sorted and even, except the last number, so only one pair has an odd sum.
    static TestCase TwoSumSorted(int n)
    {
        var nums = Enumerable.Range(0, n).Select(i => 2 * i).ToArray();
        nums[n - 1]++;
        return Case("Large input: O(n²) is too slow", new[] { n - 1, n }, nums, nums[n - 2] + nums[n - 1]);
    }

    static TestCase MoveZeroes(int n)
    {
        var nums = Enumerable.Range(0, n).Select(i => i % 2 == 0 ? 0 : i).ToArray();
        return Case("Large input: shifting for each zero is too slow", nums.Where(x => x != 0).Concat(new int[n / 2]).ToArray(), nums);
    }

    static TestCase ThreeSum(int n)
    {
        var random = new Random(42);
        var nums = Enumerable.Range(0, n).Select(_ => random.Next(-1_000_000, 1_000_001)).ToArray();
        var sorted = nums.Distinct().Order().ToArray();
        var count = nums.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var expected = new List<int[]>();
        for (int i = 0; i < sorted.Length; i++)
        for (int j = i; j < sorted.Length; j++)
        {
            int c = -sorted[i] - sorted[j];
            if (c < sorted[j] || !count.ContainsKey(c)) continue;
            int[] triple = [sorted[i], sorted[j], c];
            if (triple.GroupBy(x => x).All(g => g.Count() <= count[g.Key])) expected.Add(triple);
        }
        return Case("Large input: three loops are too slow", expected, nums);
    }

    static TestCase Container(int n)
    {
        // Heights of 1 with two tall lines, so the best area is known without solving the problem.
        var heights = Enumerable.Repeat(1, n).ToArray();
        heights[1] = heights[n - 2] = 10_000;
        return Case("Large input: O(n²) is too slow", (n - 3) * 10_000, heights);
    }

    static TestCase Average(int n, int k)
    {
        var random = new Random(42);
        var nums = Enumerable.Range(0, n).Select(_ => random.Next(-10_000, 10_001)).ToArray();
        long sum = nums.Take(k).Sum(x => (long)x), best = sum;
        for (int i = k; i < n; i++) best = Math.Max(best, sum += nums[i] - nums[i - k]);
        return Case("Large input: adding up every window is too slow", (double)best / k, nums, k);
    }

    static TestCase Stock(int n)
    {
        var random = new Random(7);
        var prices = Enumerable.Range(0, n).Select(_ => random.Next(0, 10_001)).ToArray();
        int low = int.MaxValue, best = 0;
        foreach (var p in prices) { low = Math.Min(low, p); best = Math.Max(best, p - low); }
        return Case("Large input: trying every pair of days is too slow", best, prices);
    }

    // Mostly 'A' with a 'B' every 100 characters, so the best window is long.
    static TestCase Replacement(int n, int k)
    {
        var s = new string(Enumerable.Range(0, n).Select(i => i % 100 == 99 ? 'B' : 'A').ToArray());
        int left = 0, most = 0, best = 0;
        var count = new int[26];
        for (int right = 0; right < n; right++)
        {
            most = Math.Max(most, ++count[s[right] - 'A']);
            while (right - left + 1 - most > k) count[s[left++] - 'A']--;
            best = Math.Max(best, right - left + 1);
        }
        return Case("Large input: growing a window from every start is too slow", best, s, k);
    }

    // s2 is all 'z' until a shuffled copy of s1 at the very end.
    static TestCase Permutation(int m, int filler)
    {
        var s1 = Enumerable.Range(0, m).Select(i => (char)('a' + i % 25)).ToArray();
        var copy = (char[])s1.Clone();
        new Random(42).Shuffle(copy);
        return Case("Large input: sorting every window is too slow", true, new string(s1), new string('z', filler) + new string(copy));
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
