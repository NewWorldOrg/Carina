namespace Carina.Domain.Streaming;

public sealed record LiveWireSettings
{
    public LiveWireSettings(
        TimeSpan? betweenPings = null,
        TimeSpan? writePatience = null,
        TimeSpan? silenceCeiling = null,
        int largestFrameFromAViewer = 64)
    {
        TimeSpan pings = betweenPings ?? TimeSpan.FromSeconds(15);
        TimeSpan patience = writePatience ?? TimeSpan.FromSeconds(5);
        TimeSpan ceiling = silenceCeiling ?? TimeSpan.FromSeconds(100);

        if (pings <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(betweenPings),
                pings,
                "A wire that never says anything of its own is cut by whatever sits in front of it.");
        }

        if (patience <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(writePatience),
                patience,
                "A viewer is given some time to take a frame, not none.");
        }

        if (ceiling <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(silenceCeiling),
                ceiling,
                "A gateway in front cuts a wire it has heard nothing from, so the ceiling is a span, not none.");
        }

        if (largestFrameFromAViewer <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(largestFrameFromAViewer),
                largestFrameFromAViewer,
                "A viewer says a numbered message, so the room for it is small but not nothing.");
        }

        if (pings >= ceiling)
        {
            throw new ArgumentOutOfRangeException(
                nameof(betweenPings),
                pings,
                "A wire that waits as long as the ceiling before saying anything is cut while it is still carrying.");
        }

        BetweenPings = pings;
        WritePatience = patience;
        SilenceCeiling = ceiling;
        LargestFrameFromAViewer = largestFrameFromAViewer;
    }

    public TimeSpan BetweenPings { get; }

    public TimeSpan WritePatience { get; }

    public TimeSpan SilenceCeiling { get; }

    public int QuietsBeforeTheCeiling => (int)(SilenceCeiling / BetweenPings);

    public int LargestFrameFromAViewer { get; }
}
