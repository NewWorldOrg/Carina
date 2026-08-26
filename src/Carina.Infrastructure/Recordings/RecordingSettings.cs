using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Recordings;

public sealed record RecordingSettings
{
    public const string FileExtension = ".ts";

    public static readonly TimeSpan NoticingItIsDue = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan WaitingForASeat = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan WaitingForALock = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan WaitingForTheFirstByte = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan LongestWayToTheFirstByte =
        NoticingItIsDue + WaitingForASeat + WaitingForALock + WaitingForTheFirstByte;

    public static readonly RecordingSettings Default = new(
        TimeSpan.FromSeconds(10),
        NoticingItIsDue,
        LongestWayToTheFirstByte,
        new OutputRoot("primary"));

    public RecordingSettings(
        TimeSpan beforeFirstTick,
        TimeSpan betweenTicks,
        TimeSpan tuningLead,
        OutputRoot outputRoot)
    {
        ArgumentNullException.ThrowIfNull(outputRoot);

        if (beforeFirstTick <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(beforeFirstTick),
                beforeFirstTick,
                "The recorder waits for the driver to answer before its first tick, so it waits for some time.");
        }

        if (betweenTicks <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(betweenTicks),
                betweenTicks,
                "A tick that follows the one before it with no gap is a loop with nothing between its turns.");
        }

        if (tuningLead <= betweenTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tuningLead),
                tuningLead,
                "A recording is noticed as due up to one tick after it became due, so a head shorter than "
                + $"{betweenTicks} does not even cover the noticing, let alone the tuning.");
        }

        BeforeFirstTick = beforeFirstTick;
        BetweenTicks = betweenTicks;
        TuningLead = tuningLead;
        OutputRoot = outputRoot;
    }

    public TimeSpan BeforeFirstTick { get; }

    public TimeSpan BetweenTicks { get; }

    public TimeSpan TuningLead { get; }

    public OutputRoot OutputRoot { get; }
}
