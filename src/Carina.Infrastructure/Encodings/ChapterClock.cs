namespace Carina.Infrastructure.Encodings;

public static class ChapterClock
{
    public static TimeSpan InTheMetadata(TimeSpan onTheArtefact, TimeSpan headSkip)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(onTheArtefact, TimeSpan.Zero, nameof(onTheArtefact));
        ArgumentOutOfRangeException.ThrowIfLessThan(headSkip, TimeSpan.Zero, nameof(headSkip));

        return onTheArtefact + headSkip;
    }
}
