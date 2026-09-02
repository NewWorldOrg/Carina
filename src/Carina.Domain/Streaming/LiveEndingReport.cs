namespace Carina.Domain.Streaming;

public enum LiveEndingFault
{
    NotAsLongAsAnEndingReport = 1,

    NotMarkedAsAnEndingReport = 2,

    AReasonNoSupplyEndsFor = 3,
}

public sealed record LiveEndingReading(LiveEndingReport? Report, LiveEndingFault? Fault)
{
    public static LiveEndingReading Read(LiveEndingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new LiveEndingReading(report, null);
    }

    public static LiveEndingReading Broken(LiveEndingFault fault)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                "An ending report is refused for one of the reasons named here.");
        }

        return new LiveEndingReading(null, fault);
    }
}

public sealed class LiveEndingReport
{
    public const int PayloadLength = 2;

    public const byte Mark = 0xe0;

    private LiveEndingReport(LiveSupplyEnd why)
    {
        Why = why;
    }

    public LiveSupplyEnd Why { get; }

    public static LiveEndingReport Of(LiveSupplyEnding ending)
    {
        ArgumentNullException.ThrowIfNull(ending);

        return new LiveEndingReport(ending.Why);
    }

    public byte[] ToPayload() => [Mark, (byte)Why];

    public static LiveEndingReading Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is not PayloadLength)
        {
            return LiveEndingReading.Broken(LiveEndingFault.NotAsLongAsAnEndingReport);
        }

        if (payload[0] is not Mark)
        {
            return LiveEndingReading.Broken(LiveEndingFault.NotMarkedAsAnEndingReport);
        }

        LiveSupplyEnd why = (LiveSupplyEnd)payload[1];

        return Enum.IsDefined(why)
            ? LiveEndingReading.Read(new LiveEndingReport(why))
            : LiveEndingReading.Broken(LiveEndingFault.AReasonNoSupplyEndsFor);
    }
}
