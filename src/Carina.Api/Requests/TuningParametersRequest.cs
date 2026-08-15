using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Api.Requests;

public sealed record TuningParametersRequest
{
    public TuneSystem System { get; init; }

    public int? PhysicalChannel { get; init; }

    public int? TransportStreamId { get; init; }

    public TuningParameters? ToParameters(out string? problem)
    {
        problem = null;

        if (System is TuneSystem.Unspecified)
        {
            problem = "system: expected one of isdbT, isdbSBs or isdbSCs110;"
                + " no other broadcast system can be expressed.";

            return null;
        }

        if (PhysicalChannel is not { } channel)
        {
            problem = "physicalChannel: missing.";

            return null;
        }

        if (System is TuneSystem.IsdbSBs && TransportStreamId is null)
        {
            problem = "transportStreamId: a BS slot carries several streams, so it names the one it wants.";

            return null;
        }

        if (System is not TuneSystem.IsdbSBs && TransportStreamId is not null)
        {
            problem = $"transportStreamId: {TuneSystemConverter.WireName(System)} filters no stream,"
                + " so naming one would mean nothing.";

            return null;
        }

        try
        {
            return System switch
            {
                TuneSystem.IsdbT => TuningParameters.Terrestrial(channel),
                TuneSystem.IsdbSBs => TuningParameters.Bs(
                    channel,
                    new TransportStreamId(TransportStreamId!.Value)),
                _ => TuningParameters.Cs110(channel),
            };
        }
        catch (ArgumentOutOfRangeException failure)
        {
            problem = failure.Message.Split(" (Parameter")[0];

            return null;
        }
    }
}
