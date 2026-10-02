using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Carina.Conventions.Tests;

/// <summary>
/// Reads source for conditional expressions joined directly to one another: one standing as the
/// condition or as either answer of the next, through any parentheses. A run of more than two is
/// named by the line it starts on. It reads the syntax tree, so a conditional inside an argument
/// starts a run of its own, and generated migrations are left alone.
/// </summary>
public static class NestingConventionRules
{
    public const int MostConditionalsJoinedInOneExpression = 2;

    public static IReadOnlyList<string> ConditionalsJoinedPastTheLimit(string directory)
        => Scanned(directory)
            .SelectMany(file => Runs(file.Source)
                .Where(run => run.Count > MostConditionalsJoinedInOneExpression)
                .Select(run => $"{file.Relative}:{run.Line} joins {run.Count}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<Run> Runs(string source)
        => CSharpSyntaxTree.ParseText(source)
            .GetRoot()
            .DescendantNodes()
            .OfType<ConditionalExpressionSyntax>()
            .Where(conditional => Outside(conditional) is not ConditionalExpressionSyntax)
            .Select(first => new Run(
                first.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                Joined(first)));

    private static int Joined(ConditionalExpressionSyntax conditional)
        => 1 + new[] { conditional.Condition, conditional.WhenTrue, conditional.WhenFalse }
            .Select(Inside)
            .OfType<ConditionalExpressionSyntax>()
            .Sum(Joined);

    private static SyntaxNode? Outside(SyntaxNode node)
    {
        SyntaxNode? parent = node.Parent;

        while (parent is ParenthesizedExpressionSyntax)
        {
            parent = parent.Parent;
        }

        return parent;
    }

    private static ExpressionSyntax Inside(ExpressionSyntax expression)
    {
        ExpressionSyntax held = expression;

        while (held is ParenthesizedExpressionSyntax parenthesized)
        {
            held = parenthesized.Expression;
        }

        return held;
    }

    private static IEnumerable<SourceFile> Scanned(string directory)
        => Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutputOrGenerated(Path.GetRelativePath(directory, file)))
            .Select(file => new SourceFile(
                "/" + Path.GetRelativePath(directory, file).Replace('\\', '/'),
                File.ReadAllText(file)));

    private static bool IsBuildOutputOrGenerated(string relative)
    {
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal)
               || segments.Contains("bin", StringComparer.Ordinal)
               || segments.Contains("Migrations", StringComparer.Ordinal)
               || relative.EndsWith(".Designer.cs", StringComparison.Ordinal);
    }

    private readonly record struct Run(int Line, int Count);

    private readonly record struct SourceFile(string Relative, string Source);
}
