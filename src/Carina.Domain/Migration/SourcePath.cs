namespace Carina.Domain.Migration;

public static class SourcePath
{
    public static string Of(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(path, parameterName);

        if (path.StartsWith('/'))
        {
            throw new ArgumentException(
                "A path is read from the source output directory down, so it does not start at the top of the disk.",
                parameterName);
        }

        return path;
    }
}
