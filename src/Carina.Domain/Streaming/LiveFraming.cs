namespace Carina.Domain.Streaming;

public enum LiveFrameFault
{
    ShorterThanAHeader = 1,

    AChannelNobodySetAside = 2,
}

public sealed record LiveFraming(LiveFrame? Frame, LiveFrameFault? Fault)
{
    public static LiveFraming Read(LiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return new LiveFraming(frame, null);
    }

    public static LiveFraming Broken(LiveFrameFault fault)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                "A frame is refused for one of the reasons named here.");
        }

        return new LiveFraming(null, fault);
    }
}
