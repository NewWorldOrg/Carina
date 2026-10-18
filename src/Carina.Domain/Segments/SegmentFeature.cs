using System.Globalization;

namespace Carina.Domain.Segments;

/// <summary>
/// The names of the features computed for each segment, as the weights and the standardisation of a
/// version of the model name them. A genre is one feature per kind of genre, set for the programme's
/// first genre.
/// </summary>
public static class SegmentFeature
{
    public const string Length = "length";

    public const string GridOffset = "gridOffset";

    public const string IsMultipleOf15s = "isMultipleOf15s";

    public const string HeadSilenceDepth = "headSilenceDepth";

    public const string HeadSilenceLength = "headSilenceLength";

    public const string TailSilenceDepth = "tailSilenceDepth";

    public const string TailSilenceLength = "tailSilenceLength";

    public const string EdgeSilence = "edgeSilence";

    public const string LogoShare = "logoShare";

    public const string CatalogShare = "catalogShare";

    public const string CatalogCmShare = "catalogCmShare";

    public const string CatalogProgrammeShare = "catalogProgrammeShare";

    public const string CatalogOpeningShare = "catalogOpeningShare";

    public const string CatalogEndingShare = "catalogEndingShare";

    public const string CatalogOccurrences = "catalogOccurrences";

    public const string CatalogSeries = "catalogSeries";

    public const string CatalogStations = "catalogStations";

    public const string FromStart = "fromStart";

    public const string FromEnd = "fromEnd";

    public const string OutsideGuide = "outsideGuide";

    public const string PastCmShare = "pastCmShare";

    public const string CaptionShare = "captionShare";

    public const string ChannelCorrelation = "channelCorrelation";

    public const string MultipleOf15sWithoutLogo = "multipleOf15sWithoutLogo";

    public const string CatalogCmMultipleOf15s = "catalogCmMultipleOf15s";

    public const int GenreKinds = 16;

    private static readonly string[] Listed =
    [
        Length,
        GridOffset,
        IsMultipleOf15s,
        HeadSilenceDepth,
        HeadSilenceLength,
        TailSilenceDepth,
        TailSilenceLength,
        EdgeSilence,
        LogoShare,
        CatalogShare,
        CatalogCmShare,
        CatalogProgrammeShare,
        CatalogOpeningShare,
        CatalogEndingShare,
        CatalogOccurrences,
        CatalogSeries,
        CatalogStations,
        FromStart,
        FromEnd,
        OutsideGuide,
        PastCmShare,
        CaptionShare,
        ChannelCorrelation,
        .. Enumerable.Range(0, GenreKinds).Select(Genre),
        MultipleOf15sWithoutLogo,
        CatalogCmMultipleOf15s,
    ];

    private static readonly HashSet<string> Known = new(Listed, StringComparer.Ordinal);

    public static IReadOnlyList<string> Names => Listed;

    public static string Genre(int kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(kind);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(kind, GenreKinds);

        return string.Create(CultureInfo.InvariantCulture, $"genre{kind}");
    }

    public static bool IsKnown(string name) => Known.Contains(name);
}
