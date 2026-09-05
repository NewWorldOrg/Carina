using Carina.Domain.Machines;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// Which encoder the live path uses. Whether the card can be reached at all is a fact about the
/// machine, not about live viewing, so it is read from <see cref="IMachineCapabilityReader"/>;
/// what is left here is only what live asked for.
/// </summary>
public sealed class LiveEncoderSelection(LiveTranscodeSettings settings, IMachineCapabilityReader machine)
    : ILiveEncoderSelector
{
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
}
