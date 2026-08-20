namespace Carina.Api.Authentication;

public sealed class AnonymousSurface
{
    private AnonymousSurface(string method, string path, bool below)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Method = method;
        Path = path;
        AdmitsEverythingBelow = below;
    }

    public string Method { get; }

    public string Path { get; }

    public bool AdmitsEverythingBelow { get; }

    public static AnonymousSurface Exactly(string method, string path)
        => new(method, path, below: false);

    public static AnonymousSurface Below(string method, string path)
        => path.EndsWith('/')
            ? new AnonymousSurface(method, path, below: true)
            : throw new ArgumentException(
                "A surface admitting everything below a directory names the directory with its trailing slash.",
                nameof(path));

    public bool Admits(string method, string path)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);

        if (!string.Equals(method, Method, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AdmitsEverythingBelow
            ? path.StartsWith(Path, StringComparison.OrdinalIgnoreCase)
            : string.Equals(WithoutATrailingSlash(path), Path, StringComparison.OrdinalIgnoreCase);
    }

    private static string WithoutATrailingSlash(string path)
        => path.Length > 1 && path.EndsWith('/') ? path[..^1] : path;
}
