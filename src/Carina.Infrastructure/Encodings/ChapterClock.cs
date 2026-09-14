using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// The one place a moment another programme reported is put on the artefact's own clock. A run
/// that looks for the breaks reads the source as it lies, keeping the source's own clock, so what
/// comes back is measured from where the container begins — which for a broadcast is the hour of
/// the day the recorder happened to be started in, not zero. A moment at or past that beginning is
/// read as being on it and has it taken off; one before it cannot be on it and is taken as already
/// counted from zero. Then the head the encode skips comes off, because the artefact begins where
/// the first picture that could be decoded was. A moment that lands outside the artefact after all
/// that is not placed at all: it is thrown away rather than clamped, and
/// <see cref="TooMuchOutOfReach"/> says when so many were thrown away that the reading was against
/// some other clock and none of it can be believed.
/// </summary>
public static class ChapterClock
{
    public const double MostOutOfReach = 0.25;

    public static TimeSpan? OnTheArtefact(
        TimeSpan reported,
        TimeSpan sourceStart,
        TimeSpan headSkip,
        TimeSpan artefactLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceStart, TimeSpan.Zero, nameof(sourceStart));
        ArgumentOutOfRangeException.ThrowIfLessThan(headSkip, TimeSpan.Zero, nameof(headSkip));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(artefactLength, TimeSpan.Zero, nameof(artefactLength));

        TimeSpan onTheSource = reported >= sourceStart ? reported - sourceStart : reported;
        TimeSpan onTheArtefact = onTheSource - headSkip;

        return onTheArtefact >= TimeSpan.Zero && onTheArtefact <= artefactLength ? onTheArtefact : null;
    }

    public static ChapterSpan? OnTheArtefact(
        ChapterSpan reported,
        TimeSpan sourceStart,
        TimeSpan headSkip,
        TimeSpan artefactLength)
    {
        TimeSpan? from = OnTheArtefact(reported.Starts, sourceStart, headSkip, artefactLength);
        TimeSpan? until = OnTheArtefact(reported.Ends, sourceStart, headSkip, artefactLength);

        return from is { } starts && until is { } ends && ends > starts ? new ChapterSpan(starts, ends) : null;
    }

    public static bool TooMuchOutOfReach(int outOfReach, int reported)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outOfReach);
        ArgumentOutOfRangeException.ThrowIfNegative(reported);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(outOfReach, reported);

        return outOfReach > reported * MostOutOfReach;
    }
}
