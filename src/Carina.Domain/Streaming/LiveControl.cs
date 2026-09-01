namespace Carina.Domain.Streaming;

public enum LiveControl : byte
{
    Ping = 0x01,

    Pong = 0x02,

    Leaving = 0x03,
}

public static class LiveControls
{
    public static IReadOnlyList<LiveControl> FromTheServer { get; } = [LiveControl.Ping];

    public static IReadOnlyList<LiveControl> FromTheViewer { get; } = [LiveControl.Pong, LiveControl.Leaving];

    public static LiveFrame Frame(LiveControl said)
    {
        if (!Enum.IsDefined(said))
        {
            throw new ArgumentOutOfRangeException(
                nameof(said),
                said,
                "The control channel carries one of the messages named here and nothing else.");
        }

        return new LiveFrame(LiveChannel.Control, LivePts.Start, new[] { (byte)said });
    }

    public static LiveControl? SaidByAViewer(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is not 1)
        {
            return null;
        }

        var said = (LiveControl)payload[0];

        return FromTheViewer.Contains(said) ? said : null;
    }
}
