using Carina.Contracts;

namespace Carina.Driver.Tuning.Dvb;

public static class DvbTuneRequest
{
    public static DvbChannel Resolve(TuneParams? tune, TuningRequest tuning) =>
        tune is null ? FromOlderParameters(tuning) : FromTypedParameters(tune);

    private static DvbChannel FromTypedParameters(TuneParams tune) =>
        tune.System switch
        {
            TuneSystem.IsdbT => Terrestrial(tune),
            TuneSystem.IsdbSBs => BroadcastSatellite(tune),
            TuneSystem.IsdbSCs110 => CommunicationSatellite(tune),
            _ => throw DvbFailure.Refused(
                $"tune.system: '{tune.System}' is not a broadcasting system this driver knows how to turn into a frequency, and it will not guess at one."
            ),
        };

    private static DvbChannel Terrestrial(TuneParams tune) =>
        tune.IsdbT is { } terrestrial
            ? DvbChannel.Terrestrial(terrestrial.PhysicalChannel)
            : throw Unfilled(tune.System);

    private static DvbChannel BroadcastSatellite(TuneParams tune) =>
        tune.IsdbSBs is { } broadcast
            ? DvbChannel.BroadcastSatellite(broadcast.BsChannel, broadcast.Tsid)
            : throw Unfilled(tune.System);

    private static DvbChannel CommunicationSatellite(TuneParams tune) =>
        tune.IsdbSCs110 is { } communication
            ? DvbChannel.CommunicationSatellite(communication.CsChannel)
            : throw Unfilled(tune.System);

    private static DvbDeviceException Unfilled(TuneSystem system) =>
        DvbFailure.Refused(
            $"tune: a tune on {TuneSystemConverter.WireName(system)} arrived without the parameters of that system, and a channel number this driver invented would tune somewhere nobody asked for."
        );

    private static DvbChannel FromOlderParameters(TuningRequest tuning) =>
        tuning.Kind switch
        {
            TunerKind.Terrestrial => DvbChannel.Terrestrial(tuning.PhysicalChannel),
            TunerKind.Satellite => Satellite(tuning.PhysicalChannel),
            _ => throw DvbFailure.Refused(
                $"tuning.kind: '{tuning.Kind}' does not say whether channel {tuning.PhysicalChannel} is terrestrial or satellite, and the driver will not guess which aerial to use."
            ),
        };

    private static DvbChannel Satellite(int slot) =>
        slot % 2 is 0
            ? DvbChannel.CommunicationSatellite(slot)
            : throw DvbFailure.Refused(
                $"tuning.physicalChannel: broadcast satellite slot {slot} is tunable only when the request names the transport stream it wants, and the older parameters carry no transport stream identifier, so this tune needs the typed ones."
            );
}
