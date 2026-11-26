using System.Collections;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Practice;

public record DebugVar(string Name, string Value);
public record DebugStep(int Line, List<DebugVar> Vars);

/// <summary>
/// Adds a call before every statement that records the line and the value of each variable in scope.
/// Running the result once gives every step, so the browser can move forward and back freely.
/// </summary>
public static class Instrumenter
{
    public const int MaxSteps = 1000;

    public const string Recorder = """
        public static class __Dbg
        {
            public static System.Action<int, object?[]>? OnStep;
            public static void Step(int line, object?[] namesAndValues) => OnStep?.Invoke(line, namesAndValues);
        }
        """;

    public static SyntaxTree Rewrite(SyntaxTree tree, IEnumerable<MetadataReference> references)
    {
        var options = (CSharpParseOptions)tree.Options;
        // "if (x) return;" becomes "if (x) { return; }" so a step can go before the return.
        // The added braces have no line breaks, so line numbers stay the same.
        var braced = CSharpSyntaxTree.Create((CSharpSyntaxNode)new AddBraces().Visit(tree.GetRoot()), options);
        var model = CSharpCompilation.Create("analysis", [braced], references).GetSemanticModel(braced);
        var root = braced.GetRoot();

        var containers = root.DescendantNodes().Where(n => n is BlockSyntax or SwitchSectionSyntax);
        var rewritten = root.ReplaceNodes(containers, (original, updated) => original switch
        {
            BlockSyntax b => ((BlockSyntax)updated).WithStatements(WithSteps(b.Statements, ((BlockSyntax)updated).Statements, model)),
            SwitchSectionSyntax s => ((SwitchSectionSyntax)updated).WithStatements(WithSteps(s.Statements, ((SwitchSectionSyntax)updated).Statements, model)),
            _ => updated
        });
        return CSharpSyntaxTree.Create((CSharpSyntaxNode)rewritten, options);
    }

    static SyntaxList<StatementSyntax> WithSteps(SyntaxList<StatementSyntax> original, SyntaxList<StatementSyntax> updated, SemanticModel model)
    {
        var result = new List<StatementSyntax>();
        for (int i = 0; i < original.Count; i++)
        {
            if (original[i] is not (LocalFunctionStatementSyntax or LabeledStatementSyntax))
                result.Add(Step(original[i], model));
            result.Add(updated[i]);
        }
        return SyntaxFactory.List(result);
    }

    static StatementSyntax Step(StatementSyntax statement, SemanticModel model)
    {
        int line = statement.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1;
        var flow = model.AnalyzeDataFlow(statement);
        var assigned = flow is { Succeeded: true } ? flow.DefinitelyAssignedOnEntry : [];
        var boundary = StaticBoundary(model.GetEnclosingSymbol(statement.SpanStart));
        var names = model.LookupSymbols(statement.SpanStart)
            .Where(s => boundary is null || IsInside(s.ContainingSymbol, boundary))
            .Where(s => s switch
            {
                ILocalSymbol local => assigned.Contains(local) && CanBox(local.Type),
                IParameterSymbol p => p.RefKind != RefKind.Out && CanBox(p.Type),
                _ => false
            })
            .Where(s => !s.Name.StartsWith("__"))
            .OrderBy(s => s is ILocalSymbol)
            .ThenBy(s => s.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0)
            .Select(s => SyntaxFacts.GetKeywordKind(s.Name) == SyntaxKind.None ? s.Name : "@" + s.Name);
        var pairs = string.Join(", ", names.Select(n => $"\"{n.TrimStart('@')}\", (object?){n}"));
        return SyntaxFactory.ParseStatement($"__Dbg.Step({line}, new object?[] {{ {pairs} }});");
    }

    // A static local function or lambda can't read variables from outside it.
    static ISymbol? StaticBoundary(ISymbol? symbol)
    {
        for (var s = symbol; s is IMethodSymbol { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction } m; s = s.ContainingSymbol)
            if (m.IsStatic) return m;
        return null;
    }

    static bool IsInside(ISymbol? symbol, ISymbol boundary)
    {
        for (var s = symbol; s is not null; s = s.ContainingSymbol)
            if (SymbolEqualityComparer.Default.Equals(s, boundary)) return true;
        return false;
    }

    static bool CanBox(ITypeSymbol type) => !type.IsRefLikeType && type.TypeKind is not (TypeKind.Pointer or TypeKind.FunctionPointer);

    sealed class AddBraces : CSharpSyntaxRewriter
    {
        static StatementSyntax Brace(StatementSyntax s) => s is BlockSyntax ? s : SyntaxFactory.Block(s);
        public override SyntaxNode? VisitIfStatement(IfStatementSyntax n) { var v = (IfStatementSyntax)base.VisitIfStatement(n)!; return v.WithStatement(Brace(v.Statement)); }
        // "else if" is braced too, so each condition in a chain gets its own step.
        public override SyntaxNode? VisitElseClause(ElseClauseSyntax n) { var v = (ElseClauseSyntax)base.VisitElseClause(n)!; return v.WithStatement(Brace(v.Statement)); }
        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax n) { var v = (WhileStatementSyntax)base.VisitWhileStatement(n)!; return v.WithStatement(Brace(v.Statement)); }
        public override SyntaxNode? VisitDoStatement(DoStatementSyntax n) { var v = (DoStatementSyntax)base.VisitDoStatement(n)!; return v.WithStatement(Brace(v.Statement)); }
        public override SyntaxNode? VisitForStatement(ForStatementSyntax n) { var v = (ForStatementSyntax)base.VisitForStatement(n)!; return v.WithStatement(Brace(v.Statement)); }
        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax n) { var v = (ForEachStatementSyntax)base.VisitForEachStatement(n)!; return v.WithStatement(Brace(v.Statement)); }
        public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax n) { var v = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(n)!; return v.WithStatement(Brace(v.Statement)); }
        public override SyntaxNode? VisitUsingStatement(UsingStatementSyntax n) { var v = (UsingStatementSyntax)base.VisitUsingStatement(n)!; return v.WithStatement(Brace(v.Statement)); }
        public override SyntaxNode? VisitLockStatement(LockStatementSyntax n) { var v = (LockStatementSyntax)base.VisitLockStatement(n)!; return v.WithStatement(Brace(v.Statement)); }
    }
}

/// <summary>Turns a variable's current value into short text, so later changes don't affect earlier steps.</summary>
public static class Snapshot
{
    const int MaxItems = 40, MaxLength = 300;
    static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        MaxDepth = 8
    };

    public static string Describe(object? value)
    {
        try
        {
            string text = value switch
            {
                null => "null",
                char c => $"'{c}'",
                string s => JsonSerializer.Serialize(s, Options),
                System.Text.StringBuilder sb => JsonSerializer.Serialize(sb.ToString(), Options),
                IDictionary d => Collection(d.Cast<DictionaryEntry>().Select(e => $"{Describe(e.Key)}: {Describe(e.Value)}"), "{", "}"),
                IEnumerable e => Collection(e.Cast<object?>().Select(Describe), "[", "]"),
                _ when value.GetType().IsPrimitive || value is decimal => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
                _ => JsonSerializer.Serialize(value, value.GetType(), Options)
            };
            return text.Length <= MaxLength ? text : text[..MaxLength] + "…";
        }
        catch (Exception)
        {
            return value?.ToString() ?? "null";
        }
    }

    static string Collection(IEnumerable<string> items, string open, string close)
    {
        var list = items.Take(MaxItems + 1).ToList();
        var more = list.Count > MaxItems ? ", …" : "";
        return open + string.Join(", ", list.Take(MaxItems)) + more + close;
    }
}

public sealed class StepLimitException() : Exception($"Stopped after {Instrumenter.MaxSteps} steps. The loop may never end.");
