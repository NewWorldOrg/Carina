namespace Carina.Domain.Encodings;

public readonly record struct ChapterSpan(TimeSpan Starts, TimeSpan Ends);

public readonly record struct ChapterScene(TimeSpan At, double Score);

public readonly record struct WatermarkSighting(TimeSpan At, bool Seen);

/// <summary>
/// What a run observed of a source before anything was made of it: where it went quiet, where the
/// picture went black, and where the picture changed enough to be worth a score — and, when a
/// station's watermark had been learned ahead from another recording of the same service, whether
/// that mark was on screen at each moment the picture was looked at. It carries observations and
/// no judgement, so the reading it leads to can be worked out without a source.
/// </summary>
public sealed record ChapterEvidence
{
    public IReadOnlyList<ChapterSpan> Silences { get; init; } = [];

    public IReadOnlyList<ChapterSpan> Blacks { get; init; } = [];

    public IReadOnlyList<ChapterScene> Scenes { get; init; } = [];

    public IReadOnlyList<WatermarkSighting> Sightings { get; init; } = [];
}
