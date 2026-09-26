namespace Carina.Domain.Encodings;

public readonly record struct ChapterSpan(TimeSpan Starts, TimeSpan Ends);

public readonly record struct ChapterScene(TimeSpan At, double Score);

public readonly record struct WatermarkSighting(TimeSpan At, bool Seen);

/// <summary>
/// What a run observed of a source: where it went quiet, where the picture went black, where the
/// picture changed enough to be scored, and, when a station watermark was learned ahead, whether it
/// was on screen at each moment looked at. It carries observations and no judgement.
/// </summary>
public sealed record ChapterEvidence
{
    public IReadOnlyList<ChapterSpan> Silences { get; init; } = [];

    public IReadOnlyList<ChapterSpan> Blacks { get; init; } = [];

    public IReadOnlyList<ChapterScene> Scenes { get; init; } = [];

    public IReadOnlyList<WatermarkSighting> Sightings { get; init; } = [];
}
