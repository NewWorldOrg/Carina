using System.Diagnostics.CodeAnalysis;

namespace Carina.Driver.Tuning.Dvb;

public sealed record DvbDevicePaths(string Frontend, string Demux, string Dvr)
{
    public const string DeviceRoot = "/dev/dvb/";

    private const string AdapterPrefix = "adapter";
    private const string FrontendPrefix = "frontend";
    private const string DemuxPrefix = "demux";
    private const string DvrPrefix = "dvr";

    public static bool TryDerive(
        string? frontendPath,
        [NotNullWhen(true)] out DvbDevicePaths? paths,
        out string problem
    )
    {
        paths = null;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(frontendPath))
        {
            problem = "devicePath: a dvb device needs a frontend path, and none was given.";

            return false;
        }

        if (!frontendPath.StartsWith(DeviceRoot, StringComparison.Ordinal))
        {
            problem =
                $"devicePath: expected a frontend under {DeviceRoot}, got '{frontendPath}'.";

            return false;
        }

        string? adapter = Path.GetDirectoryName(frontendPath);
        string node = Path.GetFileName(frontendPath);

        if (adapter is null || !Path.GetFileName(adapter).StartsWith(AdapterPrefix, StringComparison.Ordinal))
        {
            problem =
                $"devicePath: expected a frontend inside an '{AdapterPrefix}N' directory, got '{frontendPath}'.";

            return false;
        }

        if (!TryReadIndex(node, FrontendPrefix, out string? index))
        {
            problem =
                $"devicePath: expected the node to be named '{FrontendPrefix}N', got '{node}'.";

            return false;
        }

        paths = new DvbDevicePaths(
            frontendPath,
            Path.Combine(adapter, DemuxPrefix + index),
            Path.Combine(adapter, DvrPrefix + index)
        );

        return true;
    }

    private static bool TryReadIndex(string node, string prefix, out string index)
    {
        index = string.Empty;

        if (!node.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string digits = node[prefix.Length..];

        if (digits.Length is 0 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        index = digits;

        return true;
    }
}
