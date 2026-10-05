using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Carina.Conventions.Tests;

/// <summary>
/// Reads source for the ways the card could be opened somewhere other than the render node the
/// machine is set to: the default node's constant named anywhere but as the default of the setting,
/// a render node written out as a literal, and a flag that opens the card in a file other than the
/// argument builders that are handed the node. It reads the syntax tree, so a node assembled from
/// pieces or a flag held in a variable named elsewhere walks past.
/// </summary>
public static class RenderNodeConventionRules
{
    public const string TheDefault = "TheRenderNode";

    public const string Settings = "MachineSettings";

    public const string Setting = "RenderNode";

    public static readonly IReadOnlyList<string> FlagsThatOpenTheCard =
    [
        "-vaapi_device",
        "-init_hw_device",
        "-hwaccel",
        "-hwaccel_device",
        "-filter_hw_device",
        "-qsv_device",
    ];

    public static IReadOnlyList<string> DefaultNamedOutsideTheSetting(string directory)
        => Scanned(directory)
            .SelectMany(file => Root(file).DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(name => name.Identifier.ValueText == TheDefault && !IsTheSettingsDefault(name))
                .Select(name => $"{file.Relative}:{Line(name)}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> RenderNodesWrittenOut(string directory)
        => Scanned(directory)
            .SelectMany(file => Texts(file)
                .Where(text => text.Text.Contains("/dev/dri", StringComparison.Ordinal) && !IsTheDefaultsDeclaration(text.Node))
                .Select(text => $"{file.Relative}:{Line(text.Node)}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> FilesThatOpenTheCard(string directory)
        => Scanned(directory)
            .Where(file => Texts(file).Any(text => FlagsThatOpenTheCard.Contains(text.Text, StringComparer.Ordinal)))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsTheSettingsDefault(SyntaxNode name)
        => name.Ancestors().OfType<EqualsValueClauseSyntax>().FirstOrDefault()?.Parent is PropertyDeclarationSyntax
        {
            Identifier.ValueText: Setting,
            Parent: TypeDeclarationSyntax { Identifier.ValueText: Settings },
        };

    private static bool IsTheDefaultsDeclaration(SyntaxNode literal)
        => literal.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault() is
        {
            Identifier.ValueText: TheDefault,
            Parent.Parent: FieldDeclarationSyntax { Parent: TypeDeclarationSyntax { Identifier.ValueText: Settings } },
        };

    private static IEnumerable<(SyntaxNode Node, string Text)> Texts(SourceFile file)
    {
        foreach (SyntaxNode node in Root(file).DescendantNodes())
        {
            if (TextOf(node) is { } text)
            {
                yield return (node, text);
            }
        }
    }

    private static string? TextOf(SyntaxNode node)
        => node switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            InterpolatedStringTextSyntax piece => piece.TextToken.ValueText,
            _ => null,
        };

    private static SyntaxNode Root(SourceFile file) => CSharpSyntaxTree.ParseText(file.Source).GetRoot();

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

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

    private readonly record struct SourceFile(string Relative, string Source);
}
