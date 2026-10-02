using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Carina.Conventions.Tests;

/// <summary>
/// Reads source for conditional expressions joined directly to one another: one standing as the
/// condition or as either answer of the next, through any parentheses. A run of more than two is
/// named by the line it starts on. It reads the syntax tree, so a conditional inside an argument
/// starts a run of its own, and generated migrations are left alone.
/// <para>
/// Reads the same source for blocks standing inside one another. The statements that open a block
/// are <c>if</c>, <c>for</c>, <c>foreach</c>, <c>while</c>, <c>do</c>, <c>switch</c>, <c>try</c>,
/// a <c>using</c> statement, <c>lock</c>, <c>fixed</c>, <c>checked</c>, <c>unchecked</c> and
/// <c>unsafe</c>; an <c>else</c>, a <c>catch</c>, a <c>finally</c> and a <c>switch</c> section
/// stand level with the statement they belong to, and an <c>else if</c> stands level with the
/// <c>if</c> before it. The count starts again at each method, accessor, local function and
/// lambda. A function that goes deeper than three is named by the line of its deepest block.
/// </para>
/// </summary>
public static class NestingConventionRules
{
    public const int MostConditionalsJoinedInOneExpression = 2;

    public const int MostBlocksNestedInOneFunction = 3;

    public static IReadOnlyList<string> ConditionalsJoinedPastTheLimit(string directory)
        => Scanned(directory)
            .SelectMany(file => Runs(file.Source)
                .Where(run => run.Count > MostConditionalsJoinedInOneExpression)
                .Select(run => $"{file.Relative}:{run.Line} joins {run.Count}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> BlocksNestedPastTheLimit(string directory)
        => Scanned(directory)
            .SelectMany(file => DeepestNests(file.Source)
                .Where(nest => nest.Depth > MostBlocksNestedInOneFunction)
                .Select(nest => $"{file.Relative}:{nest.Line} nests {nest.Depth}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<Nest> DeepestNests(string source)
        => CSharpSyntaxTree.ParseText(source)
            .GetRoot()
            .DescendantNodes()
            .Where(OpensABlock)
            .Select(block => new { Function = block.Ancestors().FirstOrDefault(IsAFunction), Nest = NestOf(block) })
            .GroupBy(block => block.Function, block => block.Nest)
            .Select(nests => nests.MaxBy(nest => nest.Depth));

    private static Nest NestOf(SyntaxNode block)
        => new(
            block.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            block.AncestorsAndSelf().TakeWhile(node => !IsAFunction(node)).Count(OpensABlock));

    private static bool OpensABlock(SyntaxNode node)
        => node is IfStatementSyntax { Parent: not ElseClauseSyntax }
            or ForStatementSyntax
            or CommonForEachStatementSyntax
            or WhileStatementSyntax
            or DoStatementSyntax
            or SwitchStatementSyntax
            or TryStatementSyntax
            or UsingStatementSyntax
            or LockStatementSyntax
            or FixedStatementSyntax
            or CheckedStatementSyntax
            or UnsafeStatementSyntax;

    private static bool IsAFunction(SyntaxNode node)
        => node is BaseMethodDeclarationSyntax
            or AccessorDeclarationSyntax
            or LocalFunctionStatementSyntax
            or AnonymousFunctionExpressionSyntax;

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

    private readonly record struct Nest(int Line, int Depth);

    private readonly record struct SourceFile(string Relative, string Source);
}
