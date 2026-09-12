using System.Reflection;

namespace Carina.Api.Common;

public static class DeclaredVersion
{
    public static string Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return WrittenAs(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }

    public static string WrittenAs(string? informational)
    {
        string version = (informational ?? string.Empty).Split('+')[0].Trim();

        if (version.Length == 0)
        {
            throw new InvalidOperationException(
                "This build carries no version. The repository declares one in Directory.Build.props.");
        }

        return version;
    }
}
