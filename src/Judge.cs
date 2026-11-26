using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Practice;

public record Diagnostic(int Line, int Column, string Message);
public record TestResult(string Name, string Status, string Input, string Expected, string? Actual, string? Error, string? Output, double Ms, bool Custom, List<DebugStep>? Steps = null);
public record JudgeResult(string Status, List<Diagnostic> Diagnostics, List<TestResult> Tests)
{
    public int Passed => Tests.Count(t => !t.Custom && t.Status == "passed");
    public int Total => Tests.Count(t => !t.Custom);
}

public record ChildRequest(string Assembly, string Method, List<JsonArray> Tests, int? ResultArg, bool Debug = false);
public record ChildReply(bool Ok, JsonNode? Result, string? Error, string? Output, double Ms, List<DebugStep>? Steps = null);

public static class Judge
{
    static readonly TimeSpan TestLimit = TimeSpan.FromSeconds(2);
    static readonly TimeSpan StartLimit = TimeSpan.FromSeconds(15);

    const string Usings = "using System; using System.Collections.Generic; using System.Linq; using System.Text; ";

    static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(p => Path.GetFileName(p) is var f && (f.StartsWith("System.") || f is "netstandard.dll" or "mscorlib.dll"))
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToArray();

    public static async Task<JudgeResult> RunAsync(Problem problem, string code, bool submit, List<TestCase>? custom = null)
    {
        var (assembly, diagnostics) = Compile(code);
        if (assembly is null) return new("compile-error", diagnostics, []);

        // Own tests go last and never decide whether a solution passes: the prediction may be wrong.
        var tests = Problems.TestsFor(problem, submit).Concat(custom ?? []).ToList();
        var results = await ExecuteAsync(problem, assembly, tests);
        var status = results.FirstOrDefault(r => !r.Custom && r.Status != "passed")?.Status ?? "passed";
        return new(status, [], results);
    }

    /// <summary>Runs one test with every step recorded.</summary>
    public static async Task<JudgeResult> DebugAsync(Problem problem, string code, TestCase test)
    {
        var (assembly, diagnostics) = Compile(code);
        if (assembly is null) return new("compile-error", diagnostics, []);
        var (debugAssembly, debugErrors) = Compile(code, debug: true);
        if (debugAssembly is null)
            return new("debug-unsupported", [new(1, 1, "This code compiles, but the debugger can't follow it. " + debugErrors.FirstOrDefault()?.Message)], []);
        var results = await ExecuteAsync(problem, debugAssembly, [test], debug: true);
        return new(results[0].Status, [], results);
    }

    static (byte[]? Assembly, List<Diagnostic> Diagnostics) Compile(string code, bool debug = false)
    {
        // Accept either a bare method or a full "class Solution", as on LeetCode.
        bool hasClass = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot().Members
            .OfType<ClassDeclarationSyntax>().Any(c => c.Identifier.Text == "Solution");
        var source = hasClass
            ? $"{UsingsAsGlobal()}\n#line 1\n{code}"
            : $"{Usings}public class Solution {{\n#line 1\n{code}\n#line hidden\n}}";

        var tree = CSharpSyntaxTree.ParseText(source);
        SyntaxTree[] trees = debug
            ? [Instrumenter.Rewrite(tree, References), CSharpSyntaxTree.ParseText(Instrumenter.Recorder)]
            : [tree];
        var compilation = CSharpCompilation.Create("Submission_" + Guid.NewGuid().ToString("N"), trees, References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (emit.Success) return (stream.ToArray(), []);

        int lastLine = code.Split('\n').Length;
        var errors = emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d =>
            {
                var pos = d.Location.GetMappedLineSpan().StartLinePosition;
                return new Diagnostic(Math.Clamp(pos.Line + 1, 1, lastLine), pos.Character + 1, $"{d.Id}: {d.GetMessage()}");
            })
            .Distinct().Take(20).ToList();
        return (null, errors);
    }

    static string UsingsAsGlobal() => Usings.Replace("using ", "global using ");

    static async Task<List<TestResult>> ExecuteAsync(Problem problem, byte[] assembly, List<TestCase> tests, bool debug = false)
    {
        var results = new List<TestResult>();
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Practice.dll"));
        info.ArgumentList.Add("child");
        info.Environment["DOTNET_GCHeapHardLimit"] = "0x10000000"; // 256 MB

        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();
        string? failure = null;   // set once the process can't continue
        string failureStatus = "runtime-error";
        try
        {
            var request = new ChildRequest(Convert.ToBase64String(assembly), problem.Method, tests.Select(t => t.Args).ToList(), problem.ResultArg, debug);
            try
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Problems.Json));
                process.StandardInput.Close();
            }
            catch (IOException) { /* the read below reports the crash */ }

            foreach (var test in tests)
            {
                if (failure is not null)
                {
                    results.Add(Result(test, "not-run", null, null, null, 0));
                    continue;
                }
                string? line = null;
                try
                {
                    if (results.Count == 0) // "ready" line, sent once loading is finished
                        line = await process.StandardOutput.ReadLineAsync().WaitAsync(StartLimit);
                    if (results.Count > 0 || line is not null)
                        line = await process.StandardOutput.ReadLineAsync().WaitAsync(TestLimit);
                }
                catch (TimeoutException)
                {
                    failureStatus = "time-limit";
                    failure = $"Time limit exceeded ({TestLimit.TotalSeconds} seconds). The loop may never end, or the algorithm may be too slow.";
                }

                if (failure is null && line is null)
                {
                    var detail = await Task.WhenAny(stderr, Task.Delay(1000)) == stderr ? stderr.Result : "";
                    failure = "Your code crashed the process. " + Short(detail, 600);
                }
                if (failure is not null)
                {
                    results.Add(Result(test, failureStatus, null, failure, null, 0));
                    continue;
                }

                var reply = JsonSerializer.Deserialize<ChildReply>(line!, Problems.Json)!;
                var status = !reply.Ok ? "runtime-error"
                    : Comparer.Same(problem.Compare, test.Expected, reply.Result) ? "passed" : "wrong-answer";
                results.Add(Result(test, status, reply.Ok ? Text(reply.Result) : null, reply.Error, reply.Output, reply.Ms) with { Steps = reply.Steps });
            }
        }
        finally
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
        return results;
    }

    static TestResult Result(TestCase t, string status, string? actual, string? error, string? output, double ms) =>
        new(t.Name, status, Short(string.Join(", ", t.Args.Select(Text)), 300), Short(Text(t.Expected), 300),
            actual is null ? null : Short(actual, 300), error, output, ms, t.Custom);

    static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static string Text(JsonNode? node) => node?.ToJsonString(Readable) ?? "null";
    static string Short(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

public static class Comparer
{
    public static bool Same(string mode, JsonNode? expected, JsonNode? actual) => mode switch
    {
        "unordered" => JsonNode.DeepEquals(Sorted(expected), Sorted(actual)),
        "unordered-groups" => JsonNode.DeepEquals(SortedGroups(expected), SortedGroups(actual)),
        _ => JsonNode.DeepEquals(expected, actual)
    };

    static JsonNode? Sorted(JsonNode? node) => node is JsonArray a
        ? new JsonArray(a.OrderBy(x => x?.ToJsonString(), StringComparer.Ordinal).Select(x => x?.DeepClone()).ToArray())
        : node;

    static JsonNode? SortedGroups(JsonNode? node) => node is JsonArray a
        ? Sorted(new JsonArray(a.Select(x => Sorted(x)?.DeepClone()).ToArray()))
        : node;
}
