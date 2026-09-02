namespace Carina.Domain.Streaming;

public enum LiveSupplyEnd
{
    LetGo = 1,

    TakenForARecording = 2,

    DriverDraining = 3,

    WindowClosed = 4,

    TunerFailed = 5,

    StoppedByAnother = 6,

    DriverLost = 7,
}

public sealed record LiveSupplyEnding
{
    private LiveSupplyEnding(LiveSupplyEnd why, string note)
    {
        Why = why;
        Note = note;
    }

    public LiveSupplyEnd Why { get; }

    public string Note { get; }

    public static LiveSupplyEnding Of(LiveSupplyEnd why, string note)
    {
        if (!Enum.IsDefined(why))
        {
            throw new ArgumentOutOfRangeException(
                nameof(why),
                why,
                "A supply ends for one of the reasons named here.");
        }

        return new LiveSupplyEnding(why, TranscoderNote.Of(note));
    }
}
