namespace Carina.Domain.Encodings;

/// <summary>
/// Where the artefact's clock stands against the source's, kept with the job so that anything drawn
/// from the source — a caption is drawn from the source, the picture is served from the artefact —
/// can be put on the artefact's clock by one subtraction (BR-ED2-006). The source's clock is the
/// broadcast's own, so <see cref="SourceStart"/> is a reading like 30499.474 s; the head skip is how
/// far past that the first picture that could be decoded lies, and is what the run throws away;
/// their sum, <see cref="CaptionShift"/>, is the reading on the source's clock the artefact calls
/// zero. A head skip longer than <see cref="MostHeadSkip"/> is refused before anything is written,
/// because a run handed the broadcast clock instead of the skip would throw seventeen hours away
/// and finish with nothing in it. The lengths are kept so the two clocks can be compared at the
/// end: an artefact whose length disagrees with what the source had left is noted, not failed.
/// </summary>
public sealed record EncodeTimeline
{
    public static readonly TimeSpan MostHeadSkip = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

    public EncodeTimeline(TimeSpan sourceStart, TimeSpan headSkip, TimeSpan? sourceLength, TimeSpan? artefactLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceStart, TimeSpan.Zero, nameof(sourceStart));
        ArgumentOutOfRangeException.ThrowIfLessThan(headSkip, TimeSpan.Zero, nameof(headSkip));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(headSkip, MostHeadSkip, nameof(headSkip));

        if (sourceLength is { } whole)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(whole, TimeSpan.Zero, nameof(sourceLength));
        }

        if (artefactLength is { } made)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(made, TimeSpan.Zero, nameof(artefactLength));
        }

        SourceStart = sourceStart;
        HeadSkip = headSkip;
        SourceLength = sourceLength;
        ArtefactLength = artefactLength;
    }

    public TimeSpan SourceStart { get; }

    public TimeSpan HeadSkip { get; }

    public TimeSpan? SourceLength { get; }

    public TimeSpan? ArtefactLength { get; }

    /// <summary>
    /// The reading on the source's clock that the artefact calls zero: take it off a caption's
    /// presentation time and the caption lands where the artefact shows that moment.
    /// </summary>
    public TimeSpan CaptionShift => SourceStart + HeadSkip;

    /// <summary>What the source had left once the head was skipped, which is what the artefact should measure.</summary>
    public TimeSpan? Expected
        => SourceLength is { } whole && whole > HeadSkip ? whole - HeadSkip : null;

    public TimeSpan? Drift
        => ArtefactLength is { } made && Expected is { } expected ? made - expected : null;

    public bool? LengthsAgree
        => Drift is { } drift ? drift.Duration() <= Tolerance : null;

    public static bool WithinReach(TimeSpan headSkip) => headSkip >= TimeSpan.Zero && headSkip <= MostHeadSkip;

    public EncodeTimeline Measured(TimeSpan artefactLength) => new(SourceStart, HeadSkip, SourceLength, artefactLength);
}
