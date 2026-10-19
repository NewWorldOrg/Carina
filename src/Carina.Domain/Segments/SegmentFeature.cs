using System.Globalization;

namespace Carina.Domain.Segments;

/// <summary>
/// The names of the features computed for each segment, as the weights and the standardisation of a
/// version of the model name them. A genre is one feature per kind of genre, set for the programme's
/// first genre. None of them reads the catalog: what the catalog hears fixes the segments it covers when
/// the sequence is decided (<see cref="SegmentSequence"/>), so that a recording judged without its entries
/// is judged on what the regression learned from everything else.
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

    public const string FromStart = "fromStart";

    public const string FromEnd = "fromEnd";

    public const string OutsideGuide = "outsideGuide";

    public const string PastCmShare = "pastCmShare";

    public const string PastOpeningShare = "pastOpeningShare";

    public const string PastEndingShare = "pastEndingShare";

    public const string CaptionShare = "captionShare";

    public const string ChannelCorrelation = "channelCorrelation";

    public const string HeadChannelChange = "headChannelChange";

    public const string TailChannelChange = "tailChannelChange";

    public const string SceneChangesPer10s = "sceneChangesPer10s";

    public const string MultipleOf15sWithoutLogo = "multipleOf15sWithoutLogo";

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
        FromStart,
        FromEnd,
        OutsideGuide,
        PastCmShare,
        CaptionShare,
        ChannelCorrelation,
        HeadChannelChange,
        TailChannelChange,
        SceneChangesPer10s,
        .. Enumerable.Range(0, GenreKinds).Select(Genre),
        MultipleOf15sWithoutLogo,
        PastOpeningShare,
        PastEndingShare,
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
