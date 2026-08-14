using Carina.Contracts;

namespace Carina.Domain.Channels;

public sealed class TransportStreamId : CommonValueObject<int>
{
    public TransportStreamId(int value)
        : base(Validated(value))
    {
    }

    private static int Validated(int value)
    {
        if (!BroadcastStandards.IsTransportStreamId(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"A transport stream id is {BroadcastStandards.MinTransportStreamId} to {BroadcastStandards.MaxTransportStreamId}.");
        }

        return value;
    }
}
