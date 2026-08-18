using System.Xml.Linq;

namespace Carina.Architecture.Tests;

public sealed class ProjectGraph
{
    private readonly Dictionary<string, ProjectNode> nodes;

    private ProjectGraph(IEnumerable<ProjectNode> projects)
        => nodes = projects.ToDictionary(project => project.Name, StringComparer.Ordinal);

    public IReadOnlyCollection<string> ProjectNames => nodes.Keys;

    public static ProjectGraph Load(params string[] directories)
        => new(directories
            .SelectMany(directory =>
                Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories))
            .Select(Read));

    public static ProjectGraph FromNodes(params ProjectNode[] projects) => new(projects);

    public ProjectNode Node(string name)
        => nodes.TryGetValue(name, out ProjectNode? node)
            ? node
            : throw new InvalidOperationException(
                $"Unknown project '{name}'. Known projects: {string.Join(", ", nodes.Keys.Order(StringComparer.Ordinal))}.");

    public IReadOnlySet<string> TransitiveReferencesOf(string name)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(Node(name).ProjectReferences);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if (!reached.Add(current))
            {
                continue;
            }

            foreach (string next in Node(current).ProjectReferences)
            {
                pending.Push(next);
            }
        }

        return reached;
    }

    public IReadOnlyList<string> ForbiddenReferencesOf(string name, params string[] allowed)
        => TransitiveReferencesOf(name)
            .Where(reference => !allowed.Contains(reference, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<string> TestProjectsReferencingAnotherTestProject()
        => nodes.Values
            .Where(node => IsATestProject(node.Name))
            .SelectMany(node => node.ProjectReferences
                .Where(IsATestProject)
                .Select(reference => $"{node.Name} -> {reference}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<string> DependentsOf(string name)
        => nodes.Values
            .Where(node => node.ProjectReferences.Contains(name, StringComparer.Ordinal))
            .Select(node => node.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsATestProject(string name)
        => name.EndsWith(".Tests", StringComparison.Ordinal);

    private static ProjectNode Read(string path)
    {
        var document = XDocument.Load(path);

        string[] projectReferences = document
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .ToArray();

        string[] packageReferences = document
            .Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToArray();

        return new ProjectNode(Path.GetFileNameWithoutExtension(path), projectReferences, packageReferences);
    }
}

public sealed record ProjectNode(
    string Name,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences);
