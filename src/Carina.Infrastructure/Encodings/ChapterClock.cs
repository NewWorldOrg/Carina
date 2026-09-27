using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Puts a moment another programme reported onto the artefact's own clock.
/// </summary>
/// <remarks>
/// A reported moment is on the source's own clock, measured from where the container begins. That
/// beginning is taken off first, and a moment below it falls outside the artefact. Then the head the
/// encode skips is taken off. A moment that lands outside the artefact is thrown away rather than
/// clamped, and <see cref="TooMuchOutOfReach"/> says when so many were thrown away that none of the
/// reading can be believed.
/// </remarks>
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

        TimeSpan onTheArtefact = reported - sourceStart - headSkip;

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

    /// <summary>
    /// Puts a moment on the artefact's clock onto the clock the metadata file handed to the encode is
    /// written on, by adding back the head skip that ffmpeg takes off every chapter it copies. Where
    /// the source's own clock began does not come into it.
    /// </summary>
    public static TimeSpan InTheMetadata(TimeSpan onTheArtefact, TimeSpan headSkip)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(onTheArtefact, TimeSpan.Zero, nameof(onTheArtefact));
        ArgumentOutOfRangeException.ThrowIfLessThan(headSkip, TimeSpan.Zero, nameof(headSkip));

        return onTheArtefact + headSkip;
    }

    public static bool TooMuchOutOfReach(int outOfReach, int reported)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outOfReach);
        ArgumentOutOfRangeException.ThrowIfNegative(reported);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(outOfReach, reported);

        return outOfReach > reported * MostOutOfReach;
    }
}
