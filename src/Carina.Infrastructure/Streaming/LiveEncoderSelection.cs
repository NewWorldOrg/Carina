using Carina.Domain.Machines;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveEncoderSelection(LiveTranscodeSettings settings, IMachineCapabilityReader machine)
    : ILiveEncoderSelector
{
    private static readonly IReadOnlyList<(VideoCodec Format, Faculty Needs)> OnTheCardAlone =
    [
        (VideoCodec.H265, Faculty.EncodeH265OnTheCard),
        (VideoCodec.Av1, Faculty.EncodeAv1OnTheCard),
    ];

    public async Task<LiveEncoderChoice> ChooseAsync(CancellationToken cancellationToken)
    {
        if (!LiveEncoders.OnTheCard(settings.Prefer))
        {
            return LiveEncoderChoice.Asked(settings.Prefer);
        }

        MachineCapabilities can = await machine.ReadAsync(cancellationToken);

        return can.CardIsUsable
            ? LiveEncoderChoice.Asked(settings.Prefer)
            : LiveEncoderChoice.FellBackToSoftware(can.Card, can.Note);
    }

    public async Task<IReadOnlyList<VideoCodec>> FormatsAsync(CancellationToken cancellationToken)
    {
        if (!LiveEncoders.OnTheCard(settings.Prefer))
        {
            return [VideoCodecs.Unasked];
        }

        MachineCapabilities can = await machine.ReadAsync(cancellationToken);

        return
        [
            VideoCodecs.Unasked,
            .. OnTheCardAlone.Where(format => can.Has(format.Needs)).Select(format => format.Format),
        ];
    }
}
