namespace Carina.Domain.Segments;

/// <summary>
/// What marks a place as a boundary candidate. A silent cut is a silence the picture corroborates.
/// </summary>
[Flags]
public enum BoundaryCue
{
    None = 0,

    Silence = 1,

    SilentCut = 2,

    Black = 4,

    SceneChange = 8,

    ChannelChange = 16,

    LogoChange = 32,

    CaptionChange = 64,

    CatalogEdge = 128,

    GuideEdge = 256,

    RecordingEdge = 512,
}

/// <summary>
/// A boundary candidate after the candidates near one another are merged: the second of the strongest
/// of them, every cue they carried, the earliest and latest of their silent cuts, catalog edges and
/// recording ends (its own second when it took in none), and the depth below the threshold, in decibels,
/// and the length of the longest silence among them.
/// </summary>
public sealed record BoundaryCandidate(
    TimeSpan At,
    TimeSpan Earliest,
    TimeSpan Latest,
    BoundaryCue Cues,
    double SilenceDepth,
    TimeSpan SilenceLength)
{
    public bool IsSilentCut => Cues.HasFlag(BoundaryCue.SilentCut);

    public bool IsRecordingEdge => Cues.HasFlag(BoundaryCue.RecordingEdge);
}
