namespace Carina.Domain.Segments;

[Flags]
public enum BoundaryCue
{
    None = 0,

    Silence = 1,

    SilentCut = 2,

    Black = 4,

    LogoChange = 8,

    CatalogEdge = 16,

    GuideEdge = 32,

    RecordingEdge = 64,
}

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

    public int Strength => StrengthOf(Cues);

    public static int StrengthOf(BoundaryCue cues) => cues switch
    {
        _ when cues.HasFlag(BoundaryCue.RecordingEdge) => 5,
        _ when cues.HasFlag(BoundaryCue.SilentCut) => 4,
        _ when cues.HasFlag(BoundaryCue.CatalogEdge) => 3,
        _ when (cues & (BoundaryCue.Silence | BoundaryCue.Black)) is not BoundaryCue.None => 2,
        _ => 1,
    };
}
