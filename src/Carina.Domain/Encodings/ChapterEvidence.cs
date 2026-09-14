namespace Carina.Domain.Encodings;

public readonly record struct ChapterSpan(TimeSpan Starts, TimeSpan Ends);

public readonly record struct ChapterScene(TimeSpan At, double Score);

/// <summary>
/// What a run observed of a source before anything was made of it: where it went quiet, where the
/// picture went black, and where the picture changed enough to be worth a score. It carries
/// observations and no judgement, so the reading it leads to can be worked out without a source.
/// </summary>
public sealed record ChapterEvidence
{
    public IReadOnlyList<ChapterSpan> Silences { get; init; } = [];

    public IReadOnlyList<ChapterSpan> Blacks { get; init; } = [];

    public IReadOnlyList<ChapterScene> Scenes { get; init; } = [];
}
