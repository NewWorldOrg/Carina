namespace Carina.Domain.Streaming;

public enum SoundTrack
{
    Main = 1,

    Secondary = 2,

    Third = 3,
}

public static class SoundTracks
{
    public const string MainIsCalled = "main";

    public const string SecondaryIsCalled = "secondary";

    public const string ThirdIsCalled = "third";

    private const int MostStreamsOfTheirOwn = 2;

    public static readonly IReadOnlyList<SoundTrack> InOrder = [SoundTrack.Main, SoundTrack.Secondary, SoundTrack.Third];

    public static readonly IReadOnlyList<string> Names = [MainIsCalled, SecondaryIsCalled, ThirdIsCalled];

    public static string NameOf(SoundTrack track) => track switch
    {
        SoundTrack.Main => MainIsCalled,
        SoundTrack.Secondary => SecondaryIsCalled,
        SoundTrack.Third => ThirdIsCalled,
        _ => throw Unknown(track),
    };

    public static int Ordinal(SoundTrack track) => track switch
    {
        SoundTrack.Main => 0,
        SoundTrack.Secondary => 1,
        SoundTrack.Third => 2,
        _ => throw Unknown(track),
    };

    public static SoundTrack? Find(string? name)
        => InOrder.Cast<SoundTrack?>().FirstOrDefault(track =>
            string.Equals(NameOf(track!.Value), name, StringComparison.Ordinal));

    /// <summary>
    /// The sounds offered by this many streams carried whole: one for each, as far as the secondary.
    /// </summary>
    public static IReadOnlyList<SoundTrack> OutOf(int sounds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sounds);

        return [.. InOrder.Take(Math.Min(sounds, MostStreamsOfTheirOwn))];
    }

    private static ArgumentOutOfRangeException Unknown(SoundTrack track)
        => new(nameof(track), track, "A picture is carried with one of the sounds named here.");
}
