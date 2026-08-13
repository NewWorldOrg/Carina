using Carina.Driver.Sessions;

namespace Carina.Driver.Tests;

public sealed class SessionBroadcasterTests
{
    [Fact]
    public void ASessionTakesOnlySoManySubscribers()
    {
        using var broadcaster = new SessionBroadcaster(subscriberLimit: 2);

        Assert.True(broadcaster.TrySubscribe(SubscriberKind.Viewer, out _));
        Assert.True(broadcaster.TrySubscribe(SubscriberKind.Survey, out _));
        Assert.False(broadcaster.TrySubscribe(SubscriberKind.Viewer, out var refused));

        Assert.Null(refused);
        Assert.Equal(2, broadcaster.SubscriberCount);
    }

    [Fact]
    public void ASubscriberThatLeavesMakesRoomForTheNext()
    {
        using var broadcaster = new SessionBroadcaster(subscriberLimit: 1);

        Assert.True(broadcaster.TrySubscribe(SubscriberKind.Viewer, out var first));
        Assert.False(broadcaster.TrySubscribe(SubscriberKind.Viewer, out _));

        broadcaster.Unsubscribe(first);

        Assert.True(broadcaster.TrySubscribe(SubscriberKind.Viewer, out _));
    }

    [Fact]
    public void TheLimitHoldsWhenEveryoneArrivesAtOnce()
    {
        using var broadcaster = new SessionBroadcaster(subscriberLimit: 4);
        var taken = 0;

        Parallel.For(
            0,
            64,
            index =>
            {
                if (broadcaster.TrySubscribe(SubscriberKind.Survey, out _))
                {
                    Interlocked.Increment(ref taken);
                }
            }
        );

        Assert.Equal(4, taken);
        Assert.Equal(4, broadcaster.SubscriberCount);
    }

    [Fact]
    public void TheSurveyBufferIsBoundedByTheLimitAndNotByTheNumberOfCallers()
    {
        using var broadcaster = new SessionBroadcaster(
            surveyCapacity: 256,
            subscriberLimit: SessionBroadcaster.DefaultSubscriberLimit
        );

        for (var index = 0; index < 100; index++)
        {
            broadcaster.TrySubscribe(SubscriberKind.Survey, out _);
        }

        Assert.Equal(SessionBroadcaster.DefaultSubscriberLimit, broadcaster.SubscriberCount);
    }
}
