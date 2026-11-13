namespace Carina.Api.Tests.FeatureTest;

/// <summary>
/// Removes the directories a feature made for itself, every one of them even when removing another fails.
/// </summary>
internal static class TemporaryDirectories
{
    public static void RemoveAll(params DirectoryInfo[] directories)
    {
        ArgumentNullException.ThrowIfNull(directories);

        List<Exception> refused = [];

        foreach (DirectoryInfo directory in directories)
        {
            try
            {
                if (Directory.Exists(directory.FullName))
                {
                    directory.Delete(recursive: true);
                }
            }
            catch (IOException removing)
            {
                refused.Add(removing);
            }
            catch (UnauthorizedAccessException removing)
            {
                refused.Add(removing);
            }
        }

        if (refused.Count > 0)
        {
            throw new AggregateException(refused);
        }
    }
}
