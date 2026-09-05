namespace Carina.Domain.Encodings;

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

    public TimeSpan CaptionShift => SourceStart + HeadSkip;

    public TimeSpan? Expected
        => SourceLength is { } whole && whole > HeadSkip ? whole - HeadSkip : null;

    public TimeSpan? Drift
        => ArtefactLength is { } made && Expected is { } expected ? made - expected : null;

    public bool? LengthsAgree
        => Drift is { } drift ? drift.Duration() <= Tolerance : null;

    public static bool WithinReach(TimeSpan headSkip) => headSkip >= TimeSpan.Zero && headSkip <= MostHeadSkip;

    public EncodeTimeline Measured(TimeSpan artefactLength) => new(SourceStart, HeadSkip, SourceLength, artefactLength);
}
