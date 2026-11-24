using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BotIQ.CodeIntelligence;

public sealed record MethodNode(string Id, string Name, string File, int Line);
public sealed record CallEdge(string From, string To, int Line, string Confidence);
public sealed record DataLink(string Method, string Target, string[] Inputs, string Kind, int Line);
public sealed record OutputColumn(string Method, string Name, int Line);
public sealed record DependencyReport(IReadOnlyList<MethodNode> Methods, IReadOnlyList<CallEdge> Calls,
    IReadOnlyList<DataLink> DataLinks, IReadOnlyList<OutputColumn> OutputColumns, bool Truncated);

// Conservative syntax graph. No compilation, evaluation or execution of uploaded code.
public static class DependencyAnalyzer
{
    private static readonly HashSet<string> HttpNames = new(StringComparer.OrdinalIgnoreCase)
    { "HttpGet", "HttpPost", "HttpPut", "GetAsync", "PostAsync", "SendAsync", "GetResponse", "GetResponseAsync", "DownloadString" };
    private static readonly HashSet<string> ParserNames = new(StringComparer.OrdinalIgnoreCase)
    { "GetProperty", "SelectToken", "GetStringResult", "Parse", "DeserializeObject", "TryGetProperty" };
    public static DependencyReport Analyze(IReadOnlyList<SourceFile> files)
    {
        var methods = new List<MethodNode>();
        var syntax = new List<(MethodNode Node, MethodDeclarationSyntax Syntax)>();
        var calls = new List<CallEdge>(); var links = new List<DataLink>(); var columns = new List<OutputColumn>();
        bool truncated = false;
        foreach (var file in files)
        {
            var root = CSharpSyntaxTree.ParseText(file.Content, new CSharpParseOptions(LanguageVersion.CSharp14), path: file.Name).GetRoot();
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (methods.Count >= 2000) { truncated = true; break; }
                var name = method.Identifier.ValueText;
                var line = Line(method);
                var node = new MethodNode($"{file.Name}:{line}:{name}", name, file.Name, line);
                methods.Add(node); syntax.Add((node, method));
            }
        }
        var byName = methods.ToLookup(m => m.Name, StringComparer.Ordinal);
        foreach (var (method, declaration) in syntax)
        {
            foreach (var node in declaration.DescendantNodes(n => n is not LocalFunctionStatementSyntax and not AnonymousFunctionExpressionSyntax))
            {
                if (calls.Count + links.Count + columns.Count >= 20_000) { truncated = true; break; }
                if (node is InvocationExpressionSyntax invoke)
                {
                    var name = CallName(invoke);
                    var candidates = byName[name].ToArray();
                    if (candidates.Length == 1 && candidates[0].Id != method.Id)
                        calls.Add(new(method.Id, candidates[0].Id, Line(node), "Unique name candidate; receiver/overload unverified"));
                    else if (candidates.Length > 1)
                        foreach (var target in candidates.Take(8)) calls.Add(new(method.Id, target.Id, Line(node), "Ambiguous name candidate"));
                    if (HttpNames.Contains(name)) links.Add(new(method.Id, name, Inputs(invoke.ArgumentList), "HTTP invocation", Line(node)));
                    if (ParserNames.Contains(name)) links.Add(new(method.Id, name, Inputs(invoke), "Parser invocation", Line(node)));
                    if (name == "Add" && invoke.Expression is MemberAccessExpressionSyntax member && member.Expression.ToString().EndsWith(".Columns", StringComparison.Ordinal) && invoke.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                        AddColumn(literal.Token.ValueText, node);
                }
                if (node is VariableDeclaratorSyntax v && v.Initializer is not null) AddLink(v.Identifier.ValueText, v.Initializer.Value, node);
                if (node is AssignmentExpressionSyntax a)
                {
                    if (a.Left is IdentifierNameSyntax id) AddLink(id.Identifier.ValueText, a.Right, node);
                    if (a.Left is ElementAccessExpressionSyntax access && access.Expression.ToString().Contains("row", StringComparison.OrdinalIgnoreCase) && access.ArgumentList.Arguments.Count == 1 && access.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax col && col.IsKind(SyntaxKind.StringLiteralExpression)) AddColumn(col.Token.ValueText, node);
                }
            }
            void AddLink(string target, ExpressionSyntax expr, SyntaxNode node)
            {
                var inputs = Inputs(expr); if (inputs.Length == 0) return;
                var kind = target.Contains("url", StringComparison.OrdinalIgnoreCase) ? "URL construction" :
                    target.Contains("postdata", StringComparison.OrdinalIgnoreCase) || target.Contains("payload", StringComparison.OrdinalIgnoreCase) || target.Contains("body", StringComparison.OrdinalIgnoreCase) ? "Payload construction" :
                    target.Contains("header", StringComparison.OrdinalIgnoreCase) ? "Header construction" :
                    expr.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(i => ParserNames.Contains(CallName(i))) ? "Parsed value" : "Assignment";
                links.Add(new(method.Id, target, inputs, kind, Line(node)));
            }
            void AddColumn(string name, SyntaxNode node) { if (name.Length is > 0 and <= 128) columns.Add(new(method.Id, name, Line(node))); }
        }
        return new(methods, calls.Distinct().ToArray(), links.ToArray(), columns.Distinct().ToArray(), truncated);
    }
    private static string[] Inputs(SyntaxNode expr) => expr.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
        .Select(n => n.Identifier.ValueText).Where(n => n.Length <= 128).Distinct(StringComparer.Ordinal).Take(40).ToArray();
    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    private static string CallName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    { MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText, MemberBindingExpressionSyntax m => m.Name.Identifier.ValueText, SimpleNameSyntax s => s.Identifier.ValueText, _ => "" };
}
