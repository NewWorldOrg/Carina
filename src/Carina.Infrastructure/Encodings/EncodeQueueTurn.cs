using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Answers, for both the encode queue and the job list, whether the card is being used for someone
/// watching right now, from what this machine was asked to encode on, whether the card is usable,
/// and how many transcoders are up.
/// </summary>
public sealed class EncodeQueueTurn(
    EncodeSettings settings,
    ITranscodeBudget transcoders,
    IMachineCapabilityReader machine)
{
    public async Task<bool> YieldsToAViewerAsync(CancellationToken cancellationToken)
    {
        MachineCapabilities can = await machine.ReadAsync(cancellationToken);

        return EncodeQueues.YieldsToAViewer(settings.Prefer, can.CardIsUsable, transcoders.Running);
    }
}
