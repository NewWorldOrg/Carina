namespace Carina.Domain.DataBroadcast;

public enum DataBroadcastLineRefusal
{
    CommunicationIsOff = 1,

    HybridcastIsOff = 2,

    ReceiverIdIsKept = 3,
}

/// <summary>
/// What the viewing settings of the browser asking let a data broadcast document of a live channel do: talk to the
/// broadcaster's servers, start a Hybridcast application on top of that, and hand the receiver's identifier over on
/// top of that. Each is off unless the browser says otherwise, and none of them is a grant: who may ask is still decided
/// by the session.
/// </summary>
public sealed record DataBroadcastLine(bool Communicates, bool Hybridcast = false, bool HandsTheReceiverId = false)
{
    /// <summary>
    /// Why a document may not talk to the broadcaster's servers, or none when it may.
    /// </summary>
    public DataBroadcastLineRefusal? RefusesTheBroadcaster
        => Communicates ? null : DataBroadcastLineRefusal.CommunicationIsOff;

    /// <summary>
    /// Why a document may not read an AIT or start the application it names, or none when it may.
    /// </summary>
    public DataBroadcastLineRefusal? RefusesApplications
        => RefusesTheBroadcaster ?? (Hybridcast ? null : DataBroadcastLineRefusal.HybridcastIsOff);

    /// <summary>
    /// Why a document may not be handed the receiver's identifier, or none when it may.
    /// </summary>
    public DataBroadcastLineRefusal? RefusesTheReceiverId
        => RefusesTheBroadcaster ?? (HandsTheReceiverId ? null : DataBroadcastLineRefusal.ReceiverIdIsKept);
}
