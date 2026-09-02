namespace Carina.Domain.Streaming;

public sealed record LiveWireSettings
{
    private readonly TimeSpan betweenPings = TimeSpan.FromSeconds(15);

    private readonly TimeSpan writePatience = TimeSpan.FromSeconds(5);

    private readonly TimeSpan silenceCeiling = TimeSpan.FromSeconds(100);

    private readonly int largestFrameFromAViewer = 64;

    public TimeSpan BetweenPings
    {
        get => betweenPings;

        init => betweenPings = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A wire that never says anything of its own is cut by whatever sits in front of it.");
    }

    public TimeSpan WritePatience
    {
        get => writePatience;

        init => writePatience = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A viewer is given some time to take a frame, not none.");
    }

    public TimeSpan SilenceCeiling
    {
        get => silenceCeiling;

        init => silenceCeiling = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A gateway in front cuts a wire it has heard nothing from, so the ceiling is a span, not none.");
    }

    public bool SaysSomethingBeforeTheCeiling => betweenPings < silenceCeiling;

    public int LargestFrameFromAViewer
    {
        get => largestFrameFromAViewer;

        init => largestFrameFromAViewer = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A viewer says a numbered message, so the room for it is small but not nothing.");
    }
}
