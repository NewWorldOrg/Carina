using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Encodings;

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
