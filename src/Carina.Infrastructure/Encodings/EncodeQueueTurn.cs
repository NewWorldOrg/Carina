using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Asks the one question the encode queue and the screens both need answered the same way: whether
/// the card is being used for someone watching right now, so a job bound for it waits. The three
/// things it is worked out from — what this machine was asked to encode on, whether the card can be
/// used at all, and how many transcoders are up — are read here and nowhere else, so the queue and
/// the list of jobs can never say different things about the same moment.
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
