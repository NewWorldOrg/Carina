using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// The one place a moment another programme reported is put on the artefact's own clock. A run
/// that looks for the breaks reads the source as it lies, keeping the source's own clock, so what
/// comes back is measured from where the container begins — which for a broadcast is the hour of
/// the day the recorder happened to be started in, not zero. That beginning comes off whatever the
/// moment is, because every run that reports one keeps the source's clock: a moment below the
/// beginning is on no clock this source is on, and falls out as being outside the artefact rather
/// than being read as a second measurement counted from zero. Then the head the encode skips comes
/// off, because the artefact begins where the first picture that could be decoded was. A moment
/// that lands outside the artefact after all that is not placed at all: it is thrown away rather
/// than clamped, and <see cref="TooMuchOutOfReach"/> says when so many were thrown away that the
/// reading was against some other clock and none of it can be believed.
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
    /// The other way round: a moment on the artefact's clock put onto the clock the metadata file
    /// handed to the encode has to be written on. ffmpeg moves every chapter it copies back by the
    /// output seek and throws away what that puts outside the output, so a moment written on the
    /// artefact's own clock would arrive at the artefact a head skip early. The head skip is added
    /// back here so that it is taken off again there and the chapter lands where it was meant to.
    /// Measured, not assumed: <c>ChapterEmbeddingMaterialTests</c> encodes a broadcast with a head
    /// to skip and reads the chapters back off the artefact.
    /// <para>
    /// Only the output seek comes back into it. Where the source's own clock began does not,
    /// because the seek and the timestamps it is measured against are both counted from the first
    /// moment of the source rather than from the hour of the day the recorder was started in.
    /// </para>
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
