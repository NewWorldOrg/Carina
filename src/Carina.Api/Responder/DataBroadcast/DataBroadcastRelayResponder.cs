using Carina.Domain.DataBroadcast;

using Puppis.Contracts;

namespace Carina.Api.Responder.DataBroadcast;

public sealed record DataBroadcastTransmissionResponder(int Result, int? HttpStatus, string? Body)
{
    public static DataBroadcastTransmissionResponder Of(RelayedText relayed)
    {
        ArgumentNullException.ThrowIfNull(relayed);

        return new DataBroadcastTransmissionResponder((int)relayed.Result, relayed.HttpStatus, relayed.Body);
    }
}

public sealed record DataBroadcastFetchFailureResponder(int Result, int? HttpStatus)
{
    public static DataBroadcastFetchFailureResponder Of(RelayedResource relayed)
    {
        ArgumentNullException.ThrowIfNull(relayed);

        return new DataBroadcastFetchFailureResponder((int)relayed.Result, relayed.HttpStatus);
    }
}

public sealed record DataBroadcastReceiverIdResponder(int CaSystemId, string CardId)
{
    public static DataBroadcastReceiverIdResponder Of(ConditionalAccessCardDto card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new DataBroadcastReceiverIdResponder(card.CaSystemId, card.CardId);
    }

    public override string ToString() => $"a card of CA system 0x{CaSystemId:X4}";
}
