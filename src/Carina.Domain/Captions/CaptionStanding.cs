namespace Carina.Domain.Captions;

/// <summary>
/// Whether the captions of a recording can be drawn over the source it is played from: ready now, still
/// coming, or not at all.
/// </summary>
public enum CaptionStanding
{
    Ready = 1,

    Coming = 2,

    None = 3,
}
