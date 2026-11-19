namespace Carina.Domain.DataBroadcast;

public enum DataBroadcastLineRefusal
{
    CommunicationIsOff = 1,

    HybridcastIsOff = 2,

    ReceiverIdIsKept = 3,
}

public sealed record DataBroadcastLine(bool Communicates, bool Hybridcast = false, bool HandsTheReceiverId = false)
{
    public DataBroadcastLineRefusal? RefusesTheBroadcaster
        => Communicates ? null : DataBroadcastLineRefusal.CommunicationIsOff;

    public DataBroadcastLineRefusal? RefusesApplications
        => RefusesTheBroadcaster ?? (Hybridcast ? null : DataBroadcastLineRefusal.HybridcastIsOff);

    public DataBroadcastLineRefusal? RefusesTheReceiverId
        => RefusesTheBroadcaster ?? (HandsTheReceiverId ? null : DataBroadcastLineRefusal.ReceiverIdIsKept);
}
