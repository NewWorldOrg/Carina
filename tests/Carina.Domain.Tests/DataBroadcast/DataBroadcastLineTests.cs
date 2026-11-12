using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class DataBroadcastLineTests
{
    [Theory(DisplayName = "BR-BD-011: a document talks to the broadcaster's servers only where the browser allows communication")]
    [InlineData(false, false, false, DataBroadcastLineRefusal.CommunicationIsOff)]
    [InlineData(false, true, true, DataBroadcastLineRefusal.CommunicationIsOff)]
    [InlineData(true, false, false, null)]
    public void ADocumentTalksToTheBroadcasterOnlyWhereCommunicationIsAllowed(
        bool communicates,
        bool hybridcast,
        bool hands,
        DataBroadcastLineRefusal? refusal)
        => Assert.Equal(refusal, new DataBroadcastLine(communicates, hybridcast, hands).RefusesTheBroadcaster);

    [Theory(DisplayName = "BR-BD-011: a Hybridcast application is read and started only where the browser allows both communication and Hybridcast, communication being asked first")]
    [InlineData(false, false, DataBroadcastLineRefusal.CommunicationIsOff)]
    [InlineData(false, true, DataBroadcastLineRefusal.CommunicationIsOff)]
    [InlineData(true, false, DataBroadcastLineRefusal.HybridcastIsOff)]
    [InlineData(true, true, null)]
    public void AnApplicationStartsOnlyWhereBothAreAllowed(bool communicates, bool hybridcast, DataBroadcastLineRefusal? refusal)
        => Assert.Equal(refusal, new DataBroadcastLine(communicates, hybridcast).RefusesApplications);

    [Theory(DisplayName = "BR-BD-014: the receiver's identifier is handed over only where the browser allows both communication and handing it over, communication being asked first")]
    [InlineData(false, false, DataBroadcastLineRefusal.CommunicationIsOff)]
    [InlineData(false, true, DataBroadcastLineRefusal.CommunicationIsOff)]
    [InlineData(true, false, DataBroadcastLineRefusal.ReceiverIdIsKept)]
    [InlineData(true, true, null)]
    public void TheReceiverIdIsHandedOverOnlyWhereBothAreAllowed(bool communicates, bool hands, DataBroadcastLineRefusal? refusal)
        => Assert.Equal(refusal, new DataBroadcastLine(communicates, HandsTheReceiverId: hands).RefusesTheReceiverId);

    [Fact(DisplayName = "BR-BD-011: everything is off unless the browser says otherwise")]
    public void EverythingIsOffUnlessSaidOtherwise()
    {
        DataBroadcastLine line = new(true);

        Assert.Equal(DataBroadcastLineRefusal.HybridcastIsOff, line.RefusesApplications);
        Assert.Equal(DataBroadcastLineRefusal.ReceiverIdIsKept, line.RefusesTheReceiverId);
    }
}
