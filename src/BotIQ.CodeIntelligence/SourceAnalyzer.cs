using BotIQ.Core;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BotIQ.CodeIntelligence;

public sealed record SourceFile(string Name, string Content);
public sealed record SourceEvidence(string Kind, string Key, string File, int Line, string Method, string Detail);
public sealed record SourceReport(IReadOnlyList<SourceEvidence> Evidence, IReadOnlyList<string> Diagnostics, int FileCount);
public sealed record ImpactCandidate(string Area, string Path, string Reason, string Confidence, SourceEvidence Location);

// Syntax-based discovery only. No loading assemblies, compilation, evaluation or HTTP execution.
public static class SourceAnalyzer
{
    private static readonly HashSet<string> HttpNames = new(StringComparer.OrdinalIgnoreCase)
    { "GetAsync", "PostAsync", "PutAsync", "DeleteAsync", "PatchAsync", "SendAsync", "HttpGet", "HttpPost", "HttpPut", "GetResponse", "GetResponseAsync", "ExecuteAsync", "DownloadString", "UploadString" };
    private static readonly HashSet<string> ParserNames = new(StringComparer.OrdinalIgnoreCase)
    { "GetProperty", "TryGetProperty", "SelectToken", "SelectTokens", "Value", "GetStringResult" };

    public static SourceReport Analyze(IReadOnlyList<SourceFile> files)
    {
        var evidence = new List<SourceEvidence>();
        var diagnostics = new List<string>();
        foreach (var file in files)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Content, new CSharpParseOptions(LanguageVersion.CSharp14), path: file.Name);
            foreach (var d in tree.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).Take(30))
                diagnostics.Add($"{file.Name}:{d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.Id} (syntax error)");
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                switch (node)
                {
                    case InvocationExpressionSyntax invocation:
                        var name = invocation.Expression switch
                        {
                            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
                            MemberBindingExpressionSyntax m => m.Name.Identifier.ValueText,
                            SimpleNameSyntax s => s.Identifier.ValueText,
                            _ => ""
                        };
                        var args = invocation.ArgumentList.Arguments;
                        if (HttpNames.Contains(name)) Add("HttpCall", name, node, "Candidate HTTP call; receiver is not semantically resolved.");
                        if (ParserNames.Contains(name) && args.Count > 0 && Literal(args[0].Expression) is { } property)
                            Add("ParserProperty", property, node, $"{name} literal argument; JSON path not fully resolved.");
                        if ((name is "Add" or "TryAddWithoutValidation" or "Append") && args.Count >= 2 &&
                            invocation.Expression is MemberAccessExpressionSyntax receiver &&
                            receiver.Expression.ToString().Contains("header", StringComparison.OrdinalIgnoreCase) && Literal(args[0].Expression) is { } header)
                            Add("Header", header, node, "Header-like collection call; values are not extracted.");
                        break;
                    case ElementAccessExpressionSyntax indexer when indexer.ArgumentList.Arguments.Count == 1 && Literal(indexer.ArgumentList.Arguments[0].Expression) is { } key:
                        Add(indexer.Expression.ToString().Contains("header", StringComparison.OrdinalIgnoreCase) ? "Header" : "IndexerKey", key, node, "String indexer; collection type is not resolved.");
                        break;
                    case ElementBindingExpressionSyntax conditionalIndex when conditionalIndex.ArgumentList.Arguments.Count == 1 && Literal(conditionalIndex.ArgumentList.Arguments[0].Expression) is { } conditionalKey:
                        Add("IndexerKey", conditionalKey, node, "Null-conditional string indexer.");
                        break;
                    case AnonymousObjectMemberDeclaratorSyntax member:
                        if (member.NameEquals?.Name.Identifier.ValueText is { } memberName)
                        {
                            Add("PayloadMember", memberName, node, "Anonymous-object member; may be serialized in a request.");
                            if (memberName.Equals("operationName", StringComparison.OrdinalIgnoreCase) && Literal(member.Expression) is { } op)
                                Add("Operation", op, node, "Literal operationName member.");
                        }
                        break;
                    case VariableDeclaratorSyntax variable when variable.Initializer is not null:
                        if (variable.Identifier.ValueText.Equals("operationName", StringComparison.OrdinalIgnoreCase) && Literal(variable.Initializer.Value) is { } variableOp)
                            Add("Operation", variableOp, node, "Literal operationName variable.");
                        break;
                    case AssignmentExpressionSyntax assignment:
                        if (assignment.Left.ToString().Equals("operationName", StringComparison.OrdinalIgnoreCase) && Literal(assignment.Right) is { } assignmentOp)
                            Add("Operation", assignmentOp, node, "Literal operationName assignment.");
                        break;
                    case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                        var text = literal.Token.ValueText;
                        if (text.StartsWith('/') && !text.StartsWith("//") && text.Length > 1 && !text.Any(char.IsWhiteSpace))
                            Add("EndpointLiteral", text, node, "Relative path literal; runtime URL construction is not resolved.");
                        // Legacy bots often construct POST JSON by concatenating escaped string literals.
                        var owner = literal.Ancestors().FirstOrDefault(a => a is VariableDeclaratorSyntax or AssignmentExpressionSyntax);
                        var target = owner switch
                        {
                            VariableDeclaratorSyntax v => v.Identifier.ValueText,
                            AssignmentExpressionSyntax a => a.Left.ToString(),
                            _ => ""
                        };
                        if (target.Contains("postdata", StringComparison.OrdinalIgnoreCase) || target.Contains("payload", StringComparison.OrdinalIgnoreCase) || target.Contains("body", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (Match match in Regex.Matches(text, "\"([A-Za-z_][A-Za-z0-9_.-]*)\"\\s*:", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)))
                                Add("PayloadMember", match.Groups[1].Value, node, "JSON-looking key in a request-body string fragment; runtime construction is not resolved.");
                            var operation = Regex.Match(text, "\"operationName\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
                            if (operation.Success) Add("Operation", operation.Groups[1].Value, node, "Literal operationName in a request-body string fragment.");
                        }
                        break;
                    case ObjectCreationExpressionSyntax creation when creation.Type.ToString().EndsWith("HttpRequestMessage", StringComparison.Ordinal):
                        Add("HttpCall", "HttpRequestMessage", node, "HTTP request construction.");
                        break;
                }
            }
            void Add(string kind, string key, SyntaxNode node, string detail)
            {
                if (key.Length == 0 || key.Length > 300 || evidence.Count >= 10000) return;
                var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText
                    ?? node.Ancestors().OfType<ConstructorDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "(outside method)";
                evidence.Add(new(kind, key, file.Name, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, method, detail));
            }
        }
        if (evidence.Count >= 10000) diagnostics.Add("Evidence capped at 10,000 entries; analysis incomplete.");
        return new(evidence.Distinct().ToArray(), diagnostics, files.Count);
    }

    public static IReadOnlyList<ImpactCandidate> MapImpact(SourceReport report, IEnumerable<Finding> findings)
    {
        var result = new List<ImpactCandidate>();
        foreach (var f in findings.Distinct())
        foreach (var e in report.Evidence)
        {
            var leaf = f.Path.Split('.').Last().Replace("[*]", "", StringComparison.Ordinal);
            string? reason = null;
            var confidence = "Heuristic";
            if (f.Area == "Request" && f.Path == "endpoint" && e.Kind == "EndpointLiteral" && e.Key == f.Before)
            { reason = "Old endpoint literal matches."; confidence = "Exact literal"; }
            else if (f.Area == "Header" && e.Kind == "Header" && e.Key.Equals(f.Path, StringComparison.OrdinalIgnoreCase))
            { reason = "Header key matches; value and runtime binding are unverified."; confidence = "Exact key"; }
            else if (f.Area == "POST data" && f.Path == "$.operationName" && e.Kind == "Operation" && e.Key == f.Before)
            { reason = "Old operation literal matches."; confidence = "Exact literal"; }
            else if (f.Area == "POST data" && f.Before != "(missing)" && e.Kind == "PayloadMember" && e.Key == leaf)
                reason = "Existing payload member name matches the changed JSON leaf.";
            else if (f.Area == "Response schema" && (e.Kind is "ParserProperty" or "IndexerKey") && e.Key == leaf)
                reason = "Parser property name matches the changed response leaf; full JSON path is unverified.";
            if (reason is not null) result.Add(new(f.Area, f.Path, reason, confidence, e));
            if (result.Count >= 1000) return result.Distinct().ToArray();
        }
        return result.Distinct().ToArray();
    }

    private static string? Literal(ExpressionSyntax expression) => expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) ? literal.Token.ValueText : null;
}
