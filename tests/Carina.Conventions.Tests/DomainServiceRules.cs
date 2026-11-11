using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Carina.Conventions.Tests;

/// <summary>
/// A business decision that needs a lookup through a repository, or spans several aggregates, is made by a
/// domain service: a sealed, non-static class of the domain whose name ends in <see cref="Suffix"/>, which
/// takes only interfaces the domain declares, and <see cref="TimeProvider"/>, through its constructor. The
/// domain is the namespace handed in and every namespace under it. A class of the domain that takes such a
/// dependency under another name is reported, unless it hands that dependency on through a public property of
/// its type, as an outcome carrying a live stream or a socket does; a type of that name outside the domain or
/// of another shape is reported too. The source rule reads every declaration whose name ends in <see cref="Suffix"/> and reports one
/// outside the domain's folder or in a file not named after it; generated migrations are left alone.
/// </summary>
public static class DomainServiceRules
{
    public const string Suffix = "DomainService";

    public const string Domain = "Carina.Domain";

    public static IReadOnlyList<string> DomainServices(IEnumerable<Type> types, string domain)
        => types
            .Where(type => IsNamedADomainService(type) && IsIn(type, domain))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> DomainClassesTakingAPortUnderAnotherName(IEnumerable<Type> types, string domain)
        => types
            .Where(type => type.IsClass && IsIn(type, domain) && !IsGenerated(type) && !IsNamedADomainService(type))
            .Where(type => Parameters(type).Any(parameter => IsAPort(parameter.ParameterType, domain) && !HandsOn(type, parameter.ParameterType)))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> DomainServicesOutsideTheDomain(IEnumerable<Type> types, string domain)
        => types
            .Where(type => IsNamedADomainService(type) && !IsIn(type, domain))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> MisshapenDomainServices(IEnumerable<Type> types, string domain)
        => types
            .Where(type => IsNamedADomainService(type) && IsIn(type, domain))
            .SelectMany(type => Faults(type, domain).Select(fault => $"{type.FullName} {fault}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> DomainServicesOutOfPlace(string directory)
        => Scanned(directory)
            .SelectMany(file => Declarations(file.Source)
                .Where(declaration => !IsInPlace(file.Relative, declaration.Name))
                .Select(declaration => $"{file.Relative}:{declaration.Line} {declaration.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> Faults(Type type, string domain)
    {
        if (!type.IsClass)
        {
            yield return "is not a class";

            yield break;
        }

        if (type is { IsAbstract: true, IsSealed: true })
        {
            yield return "is static";

            yield break;
        }

        if (!type.IsSealed)
        {
            yield return "is not sealed";
        }

        foreach (ParameterInfo parameter in Parameters(type).Where(parameter => !IsAPort(parameter.ParameterType, domain)))
        {
            yield return $"takes {parameter.ParameterType.Name}";
        }
    }

    private static IEnumerable<ParameterInfo> Parameters(Type type)
        => type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters());

    private static bool HandsOn(Type type, Type held)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(property => property.PropertyType == held);

    private static bool IsAPort(Type type, string domain)
        => type == typeof(TimeProvider) || (type.IsInterface && IsIn(type, domain));

    private static bool IsNamedADomainService(Type type)
        => !IsGenerated(type) && type.Name.EndsWith(Suffix, StringComparison.Ordinal);

    private static bool IsIn(Type type, string domain)
        => type.Namespace is { } space
           && (string.Equals(space, domain, StringComparison.Ordinal) || space.StartsWith(domain + ".", StringComparison.Ordinal));

    private static bool IsGenerated(Type type)
        => type.Name.Contains('<', StringComparison.Ordinal) || type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static bool IsInPlace(string relative, string name)
        => relative.StartsWith($"/{Domain}/", StringComparison.Ordinal)
           && string.Equals(Path.GetFileName(relative), name + ".cs", StringComparison.Ordinal);

    private static IEnumerable<Declaration> Declarations(string source)
        => CSharpSyntaxTree.ParseText(source)
            .GetRoot()
            .DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Where(declaration => declaration.Identifier.ValueText.EndsWith(Suffix, StringComparison.Ordinal))
            .Select(declaration => new Declaration(
                declaration.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                declaration.Identifier.ValueText));

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

    private readonly record struct Declaration(int Line, string Name);

    private readonly record struct SourceFile(string Relative, string Source);
}
