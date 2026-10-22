namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Puts a moment on the artefact's own clock onto the clock the metadata file handed to an encode is written on.
/// </summary>
public static class ChapterClock
{
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
}
