using Carina.Domain.Machines;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// Which encoder the live path uses, from what live asked for and whether
/// <see cref="IMachineCapabilityReader"/> says the card can be reached.
/// </summary>
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
        if (settings.Prefer is not LiveEncoder.Vaapi)
        {
            return LiveEncoderChoice.Asked(settings.Prefer);
        }

        MachineCapabilities can = await machine.ReadAsync(cancellationToken);

        return can.CardIsUsable
            ? LiveEncoderChoice.Asked(LiveEncoder.Vaapi)
            : LiveEncoderChoice.FellBackToSoftware(can.Card, can.Note);
    }

    /// <remarks>
    /// H.265 and AV1 are encoded on the card alone, and only when live is set to encode on it.
    /// </remarks>
    public async Task<IReadOnlyList<VideoCodec>> FormatsAsync(CancellationToken cancellationToken)
    {
        if (settings.Prefer is not LiveEncoder.Vaapi)
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
