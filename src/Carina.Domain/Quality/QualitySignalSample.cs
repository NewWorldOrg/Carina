using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Quality;

public sealed class QualitySignalSample
{
    public const int DriverInstanceIdMaxLength = 64;

    private QualitySignalSample()
    {
    }

    public string DriverInstanceId { get; private set; } = null!;

    public SessionId Session { get; private set; }

    public DateTime TakenAt { get; private set; }

    public SessionPurpose Purpose { get; private set; }

    public TunerDeviceId Tuner { get; private set; } = null!;

    public NetworkId Network { get; private set; } = null!;

    public ServiceId Service { get; private set; } = null!;

    public SignalSample Signal { get; private set; } = null!;

    public static QualitySignalSample Rehydrate(
        string driverInstanceId,
        SessionId session,
        DateTime takenAt,
        SessionPurpose purpose,
        TunerDeviceId tuner,
        NetworkId network,
        ServiceId service,
        SignalSample signal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverInstanceId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            driverInstanceId.Length,
            DriverInstanceIdMaxLength,
            nameof(driverInstanceId));

        if (session.IsUnset)
        {
            throw new ArgumentException(
                "A sample without a session cannot be told from one taken across a session boundary.",
                nameof(session));
        }

        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "A sample is taken during a session the driver names.");
        }

        ArgumentNullException.ThrowIfNull(tuner);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(signal);

        return new QualitySignalSample
        {
            DriverInstanceId = driverInstanceId,
            Session = session,
            TakenAt = UtcTimes.Required(takenAt, nameof(takenAt)),
            Purpose = purpose,
            Tuner = tuner,
            Network = network,
            Service = service,
            Signal = signal,
        };
    }
}
