using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Quality;

public sealed class QualitySessionMeasurement
{
    public const int DriverInstanceIdMaxLength = 64;

    private QualitySessionMeasurement()
    {
    }

    public string DriverInstanceId { get; private set; } = null!;

    public SessionId Session { get; private set; }

    public SessionPurpose Purpose { get; private set; }

    public TunerDeviceId Tuner { get; private set; } = null!;

    public NetworkId Network { get; private set; } = null!;

    public ServiceId Service { get; private set; } = null!;

    public DateTime StartedAt { get; private set; }

    public DateTime? EndedAt { get; private set; }

    public bool CcMeasured { get; private set; }

    public long? CcDroppedPackets { get; private set; }

    public long? CcTotalPackets { get; private set; }

    public long EovfCount { get; private set; }

    public DateTime? MeasuredUpdatedAt { get; private set; }

    public bool HasEnded => EndedAt is not null;

    public static QualitySessionMeasurement Open(
        string driverInstanceId,
        SessionId session,
        SessionPurpose purpose,
        TunerDeviceId tuner,
        NetworkId network,
        ServiceId service,
        DateTime startedAt)
        => Rehydrate(
            driverInstanceId,
            session,
            purpose,
            tuner,
            network,
            service,
            startedAt,
            null,
            false,
            null,
            null,
            0,
            null);

    public static QualitySessionMeasurement Rehydrate(
        string driverInstanceId,
        SessionId session,
        SessionPurpose purpose,
        TunerDeviceId tuner,
        NetworkId network,
        ServiceId service,
        DateTime startedAt,
        DateTime? endedAt,
        bool ccMeasured,
        long? ccDroppedPackets,
        long? ccTotalPackets,
        long eovfCount,
        DateTime? measuredUpdatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverInstanceId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            driverInstanceId.Length,
            DriverInstanceIdMaxLength,
            nameof(driverInstanceId));

        if (session.IsUnset)
        {
            throw new ArgumentException(
                "A session measurement is kept under the session it was taken from.",
                nameof(session));
        }

        if (purpose is SessionPurpose.Recording)
        {
            throw new ArgumentException(
                "What a recording session measured belongs to the recording ledger, one row per recording.",
                nameof(purpose));
        }

        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "A session is opened for a purpose the driver names.");
        }

        ArgumentNullException.ThrowIfNull(tuner);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentOutOfRangeException.ThrowIfNegative(eovfCount);

        if (ccMeasured != (ccDroppedPackets is not null && ccTotalPackets is not null))
        {
            throw new ArgumentException(
                "Nothing counted this and this was counted are different answers, so an unmeasured session carries no counts.",
                nameof(ccMeasured));
        }

        if (ccDroppedPackets is < 0 || ccTotalPackets is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ccDroppedPackets), "A count of packets is never below none.");
        }

        UtcTimes.Required(startedAt, nameof(startedAt));
        UtcTimes.Optional(endedAt, nameof(endedAt));
        UtcTimes.Optional(measuredUpdatedAt, nameof(measuredUpdatedAt));

        if (endedAt < startedAt)
        {
            throw new ArgumentException("A session does not end before it starts.", nameof(endedAt));
        }

        if (ccMeasured != (measuredUpdatedAt is not null))
        {
            throw new ArgumentException(
                "A measured session says when it was last measured, and an unmeasured one has no such time.",
                nameof(measuredUpdatedAt));
        }

        return new QualitySessionMeasurement
        {
            DriverInstanceId = driverInstanceId,
            Session = session,
            Purpose = purpose,
            Tuner = tuner,
            Network = network,
            Service = service,
            StartedAt = startedAt,
            EndedAt = endedAt,
            CcMeasured = ccMeasured,
            CcDroppedPackets = ccDroppedPackets,
            CcTotalPackets = ccTotalPackets,
            EovfCount = eovfCount,
            MeasuredUpdatedAt = measuredUpdatedAt,
        };
    }

    public void Observe(long droppedPackets, long totalPackets, long eovfCount, DateTime at)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(droppedPackets);
        ArgumentOutOfRangeException.ThrowIfNegative(totalPackets);
        ArgumentOutOfRangeException.ThrowIfNegative(eovfCount);
        UtcTimes.Required(at, nameof(at));

        CcMeasured = true;
        CcDroppedPackets = droppedPackets;
        CcTotalPackets = totalPackets;
        EovfCount = eovfCount;
        MeasuredUpdatedAt = at;
    }

    public void Close(DateTime at)
    {
        UtcTimes.Required(at, nameof(at));

        if (at < StartedAt)
        {
            throw new ArgumentException("A session does not end before it starts.", nameof(at));
        }

        EndedAt = at;
    }
}
