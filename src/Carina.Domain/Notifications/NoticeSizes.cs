using System.Globalization;

namespace Carina.Domain.Notifications;

/// <summary>
/// Says the size of a file, and of the file made from it, in the units a person reads.
/// </summary>
internal static class NoticeSizes
{
    private const long Kilobyte = 1024;

    private const long Megabyte = Kilobyte * 1024;

    private const long Gigabyte = Megabyte * 1024;

    public static string? Shrunk(long? sourceBytes, long? artefactBytes)
    {
        if (sourceBytes is not { } source || artefactBytes is not { } artefact || source <= 0 || artefact <= 0)
        {
            return null;
        }

        return $"{Said(source)} → {Said(artefact)}(元の {Share(source, artefact)})";
    }

    private static string Said(long bytes)
    {
        double megabytes = Math.Round((double)bytes / Megabyte);
        double kilobytes = Math.Max(1, Math.Round((double)bytes / Kilobyte));

        if (megabytes * Megabyte >= Gigabyte)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(double)bytes / Gigabyte:0.0} GB");
        }

        return kilobytes * Kilobyte >= Megabyte
            ? string.Create(CultureInfo.InvariantCulture, $"{megabytes:0} MB")
            : string.Create(CultureInfo.InvariantCulture, $"{kilobytes:0} KB");
    }

    private static string Share(long source, long artefact)
    {
        double percent = Math.Round(artefact * 100d / source);

        return percent < 1 ? "1% 未満" : string.Create(CultureInfo.InvariantCulture, $"{percent:0}%");
    }
}
