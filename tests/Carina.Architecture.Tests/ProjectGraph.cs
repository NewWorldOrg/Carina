using System.Xml.Linq;

namespace Carina.Architecture.Tests;

/// <summary>
/// The reference graph of the production projects, read from the project files.
/// </summary>
public sealed class ProjectGraph
{
    private readonly Dictionary<string, ProjectNode> nodes;

    private ProjectGraph(IEnumerable<ProjectNode> projects)
        => nodes = projects.ToDictionary(project => project.Name, StringComparer.Ordinal);

    /// <summary>Names of every project in the graph.</summary>
    public IReadOnlyCollection<string> ProjectNames => nodes.Keys;

    /// <summary>Reads the graph of every project under the given directory.</summary>
    public static ProjectGraph Load(string directory)
        => new(Directory
            .EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)
            .Select(Read));

    /// <summary>Builds a graph from explicit nodes, used to check the rules themselves.</summary>
    public static ProjectGraph FromNodes(params ProjectNode[] projects) => new(projects);

    /// <summary>The node of a single project.</summary>
    public ProjectNode Node(string name)
        => nodes.TryGetValue(name, out var node)
            ? node
            : throw new InvalidOperationException(
                $"Unknown project '{name}'. Known projects: {string.Join(", ", nodes.Keys.Order(StringComparer.Ordinal))}.");

    /// <summary>Every project reachable from the given project, directly or indirectly.</summary>
    public IReadOnlySet<string> TransitiveReferencesOf(string name)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(Node(name).ProjectReferences);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!reached.Add(current))
            {
                continue;
            }

            foreach (var next in Node(current).ProjectReferences)
            {
                pending.Push(next);
            }
        }

        return reached;
    }

    /// <summary>
    /// Projects reachable from <paramref name="name"/> that are not on its allow list.
    /// </summary>
    public IReadOnlyList<string> ForbiddenReferencesOf(string name, params string[] allowed)
        => TransitiveReferencesOf(name)
            .Where(reference => !allowed.Contains(reference, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Projects that declare a reference to <paramref name="name"/>.</summary>
    public IReadOnlyList<string> DependentsOf(string name)
        => nodes.Values
            .Where(node => node.ProjectReferences.Contains(name, StringComparer.Ordinal))
            .Select(node => node.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static ProjectNode Read(string path)
    {
        var document = XDocument.Load(path);

        var projectReferences = document
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .ToArray();

        var packageReferences = document
            .Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToArray();

        return new ProjectNode(Path.GetFileNameWithoutExtension(path), projectReferences, packageReferences);
    }
}

/// <summary>A single project and the references it declares.</summary>
/// <param name="Name">Project name without extension.</param>
/// <param name="ProjectReferences">Names of the directly referenced projects.</param>
/// <param name="PackageReferences">Ids of the directly referenced packages.</param>
public sealed record ProjectNode(
    string Name,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences);
