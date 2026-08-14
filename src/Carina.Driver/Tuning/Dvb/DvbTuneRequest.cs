using Carina.Contracts;

namespace Carina.Driver.Tuning.Dvb;

public static class DvbTuneRequest
{
    public static DvbChannel Resolve(TuningRequest tuning) =>
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
            : DvbChannel.BroadcastSatellite(slot, transportStreamId: null);
}
