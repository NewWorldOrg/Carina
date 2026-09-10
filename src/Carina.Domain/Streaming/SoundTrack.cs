namespace Carina.Domain.Streaming;

public enum SoundTrack
{
    Main = 1,

    Secondary = 2,
}

public static class SoundTracks
{
    public const string MainIsCalled = "main";

    public const string SecondaryIsCalled = "secondary";

    public static readonly IReadOnlyList<SoundTrack> InOrder = [SoundTrack.Main, SoundTrack.Secondary];

    public static readonly IReadOnlyList<string> Names = [MainIsCalled, SecondaryIsCalled];

    public static string NameOf(SoundTrack track) => track switch
    {
        SoundTrack.Main => MainIsCalled,
        SoundTrack.Secondary => SecondaryIsCalled,
        _ => throw Unknown(track),
    };

    public static int Ordinal(SoundTrack track) => track switch
    {
        SoundTrack.Main => 0,
        SoundTrack.Secondary => 1,
        _ => throw Unknown(track),
    };

    public static SoundTrack? Find(string? name)
        => InOrder.Cast<SoundTrack?>().FirstOrDefault(track =>
            string.Equals(NameOf(track!.Value), name, StringComparison.Ordinal));

    public static IReadOnlyList<SoundTrack> OutOf(int sounds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sounds);

        return [.. InOrder.Take(sounds)];
    }

    private static ArgumentOutOfRangeException Unknown(SoundTrack track)
        => new(nameof(track), track, "A picture is carried with one of the sounds named here.");
}
