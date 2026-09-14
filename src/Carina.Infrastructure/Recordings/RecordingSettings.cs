using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Recordings;

public sealed record RecordingSettings
{
    public const string FileExtension = RecordingFile.Extension;

    public static readonly TimeSpan NoticingItIsDue = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan WaitingForASeat = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan WaitingForALock = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan WaitingForTheFirstByte = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan LongestWayToTheFirstByte =
        NoticingItIsDue + WaitingForASeat + WaitingForALock + WaitingForTheFirstByte;

    public static readonly TimeSpan HoldingAnUnannouncedEnd = TimeSpan.FromMinutes(20);

    public static readonly RecordingSettings Default = new(
        TimeSpan.FromSeconds(10),
        NoticingItIsDue,
        LongestWayToTheFirstByte,
        new OutputRoot("primary"),
        HoldingAnUnannouncedEnd);

    public RecordingSettings(
        TimeSpan beforeFirstTick,
        TimeSpan betweenTicks,
        TimeSpan tuningLead,
        OutputRoot outputRoot,
        TimeSpan undecidedEndAhead)
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

        if (undecidedEndAhead <= betweenTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(undecidedEndAhead),
                undecidedEndAhead,
                "A recording whose end nobody has announced is carried forward tick by tick, so a horizon no "
                + $"longer than the {betweenTicks} between two ticks is a window that has already run out.");
        }

        BeforeFirstTick = beforeFirstTick;
        BetweenTicks = betweenTicks;
        TuningLead = tuningLead;
        OutputRoot = outputRoot;
        UndecidedEndAhead = undecidedEndAhead;
    }

    public TimeSpan BeforeFirstTick { get; }

    public TimeSpan BetweenTicks { get; }

    public TimeSpan TuningLead { get; }

    public OutputRoot OutputRoot { get; }

    /// <summary>
    /// How far ahead of now a recording is promised while the programme it is recording announces
    /// no end. It is the recording's own horizon and not the one the allocation rolls a tuner seat
    /// on: the seat has to outlast the window it is held for, so this is the shorter of the two.
    /// </summary>
    public TimeSpan UndecidedEndAhead { get; }
}
