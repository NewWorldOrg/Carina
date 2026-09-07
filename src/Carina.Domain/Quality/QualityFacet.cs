using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Quality;

[Flags]
public enum QualityAxis
{
    Whole = 0,

    Channel = 1,

    Tuner = 2,

    TimeOfDay = 4,

    Kind = 8,
}

public sealed record QualityFacet
{
    public const int HoursInADay = 24;

    private QualityFacet(TuneSystem kind, NetworkId network, ServiceId service, TunerDeviceId tuner, int hourOfDay)
    {
        Kind = kind;
        Network = network;
        Service = service;
        Tuner = tuner;
        HourOfDay = hourOfDay;
    }

    public TuneSystem Kind { get; }

    public NetworkId Network { get; }

    public ServiceId Service { get; }

    public TunerDeviceId Tuner { get; }

    public int HourOfDay { get; }

    public static QualityFacet Of(TuneSystem kind, NetworkId network, ServiceId service, TunerDeviceId tuner, int hourOfDay)
    {
        if (!Enum.IsDefined(kind) || kind is TuneSystem.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "An observation comes from a broadcast of a kind the driver named.");
        }

        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(tuner);
        ArgumentOutOfRangeException.ThrowIfNegative(hourOfDay);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(hourOfDay, HoursInADay);

        return new QualityFacet(kind, network, service, tuner, hourOfDay);
    }
}

public sealed record QualityGroupKey
{
    private QualityGroupKey(NetworkId? network, ServiceId? service, TunerDeviceId? tuner, int? hourOfDay, TuneSystem? kind)
    {
        Network = network;
        Service = service;
        Tuner = tuner;
        HourOfDay = hourOfDay;
        Kind = kind;
    }

    public NetworkId? Network { get; }

    public ServiceId? Service { get; }

    public TunerDeviceId? Tuner { get; }

    public int? HourOfDay { get; }

    public TuneSystem? Kind { get; }

    internal static QualityGroupKey Reduced(QualityFacet facet, QualityAxis axis)
        => new(
            axis.HasFlag(QualityAxis.Channel) ? facet.Network : null,
            axis.HasFlag(QualityAxis.Channel) ? facet.Service : null,
            axis.HasFlag(QualityAxis.Tuner) ? facet.Tuner : null,
            axis.HasFlag(QualityAxis.TimeOfDay) ? facet.HourOfDay : null,
            axis.HasFlag(QualityAxis.Kind) ? facet.Kind : null);
}
