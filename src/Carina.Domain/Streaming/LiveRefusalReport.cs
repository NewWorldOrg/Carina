using System.Buffers.Binary;

namespace Carina.Domain.Streaming;

public enum LiveRefusalFault
{
    NotAsLongAsARefusalReport = 1,

    AReasonNoViewerIsRefusedFor = 2,

    AFullBudgetWithoutItsCeiling = 3,

    ACeilingWithoutAFullBudget = 4,

    ADetailThisReasonDoesNotTake = 5,
}

public sealed record LiveRefusalReading(LiveRefusalReport? Report, LiveRefusalFault? Fault)
{
    public static LiveRefusalReading Read(LiveRefusalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new LiveRefusalReading(report, null);
    }

    public static LiveRefusalReading Broken(LiveRefusalFault fault)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                "A refusal report is refused for one of the reasons named here.");
        }

        return new LiveRefusalReading(null, fault);
    }
}

public sealed class LiveRefusalReport
{
    public const int PayloadLength = 5;

    private LiveRefusalReport(LiveRefusal refusal, TranscodeCeiling? ceiling, LiveRefusalDetail detail)
    {
        Refusal = refusal;
        Ceiling = ceiling;
        Detail = detail;
    }

    public LiveRefusal Refusal { get; }

    public TranscodeCeiling? Ceiling { get; }

    public LiveRefusalDetail Detail { get; }

    public static LiveRefusalReport Of(LiveJoin refused)
    {
        ArgumentNullException.ThrowIfNull(refused);

        if (refused.Refusal is not { } refusal)
        {
            throw new ArgumentException("A viewer that was seated has no refusal to report.", nameof(refused));
        }

        return new LiveRefusalReport(refusal, refused.Ceiling, refused.Detail);
    }

    public byte[] ToPayload()
    {
        byte[] payload = new byte[PayloadLength];

        payload[0] = (byte)Refusal;

        if (Ceiling is { } ceiling)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1), Counted(ceiling.Running));
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(3), Counted(ceiling.AtOnce));

            return payload;
        }

        payload[1] = Detail.Said;

        return payload;
    }

    public static LiveRefusalReading Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is not PayloadLength)
        {
            return LiveRefusalReading.Broken(LiveRefusalFault.NotAsLongAsARefusalReport);
        }

        LiveRefusal refusal = (LiveRefusal)payload[0];

        if (!Enum.IsDefined(refusal))
        {
            return LiveRefusalReading.Broken(LiveRefusalFault.AReasonNoViewerIsRefusedFor);
        }

        if (refusal is not LiveRefusal.TooManyAlready)
        {
            if (payload[2] is not 0 || payload[3] is not 0 || payload[4] is not 0)
            {
                return LiveRefusalReading.Broken(LiveRefusalFault.ACeilingWithoutAFullBudget);
            }

            return LiveRefusalDetail.Read(refusal, payload[1]) is { } detail
                ? LiveRefusalReading.Read(new LiveRefusalReport(refusal, null, detail))
                : LiveRefusalReading.Broken(LiveRefusalFault.ADetailThisReasonDoesNotTake);
        }

        int running = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(1, 2));
        int atOnce = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(3, 2));

        return atOnce >= TranscodeBudgetSettings.Fewest && running >= atOnce
            ? LiveRefusalReading.Read(
                new LiveRefusalReport(refusal, new TranscodeCeiling(running, atOnce), LiveRefusalDetail.Unsaid))
            : LiveRefusalReading.Broken(LiveRefusalFault.AFullBudgetWithoutItsCeiling);
    }

    private static ushort Counted(int count) => count >= ushort.MaxValue ? ushort.MaxValue : (ushort)count;
}
