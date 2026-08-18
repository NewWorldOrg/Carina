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
        Assert.False(broadcaster.TrySubscribe(SubscriberKind.Viewer, out SessionSubscription? refused));

        Assert.Null(refused);
        Assert.Equal(2, broadcaster.SubscriberCount);
    }

    [Fact]
    public void ASubscriberThatLeavesMakesRoomForTheNext()
    {
        using var broadcaster = new SessionBroadcaster(subscriberLimit: 1);

        Assert.True(broadcaster.TrySubscribe(SubscriberKind.Viewer, out SessionSubscription? first));
        Assert.False(broadcaster.TrySubscribe(SubscriberKind.Viewer, out _));

        broadcaster.Unsubscribe(first);

        Assert.True(broadcaster.TrySubscribe(SubscriberKind.Viewer, out _));
    }

    [Fact]
    public void TheLimitHoldsWhenEveryoneArrivesAtOnce()
    {
        using var broadcaster = new SessionBroadcaster(subscriberLimit: 4);
        int taken = 0;

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
    public void ASurveyReaderThatIsCutOffHasItsMissingChunkCounted()
    {
        using var broadcaster = new SessionBroadcaster(
            surveyCapacity: 1,
            surveyBlockLimit: TimeSpan.FromMilliseconds(50)
        );

        Assert.True(broadcaster.TrySubscribe(SubscriberKind.Survey, out SessionSubscription? subscription));

        broadcaster.Publish(new byte[188]);
        broadcaster.Publish(new byte[188]);

        Assert.Equal(1, subscription.DroppedChunks);
        Assert.Equal(1, broadcaster.DroppedChunks);
        Assert.True(subscription.IsDisconnected);
        Assert.True(subscription.IsTruncated);
        Assert.Equal(0, broadcaster.SubscriberCount);
    }

    [Fact]
    public void TheSurveyBufferIsBoundedByTheLimitAndNotByTheNumberOfCallers()
    {
        using var broadcaster = new SessionBroadcaster(
            surveyCapacity: 256,
            subscriberLimit: SessionBroadcaster.DefaultSubscriberLimit
        );

        for (int index = 0; index < 100; index++)
        {
            broadcaster.TrySubscribe(SubscriberKind.Survey, out _);
        }

        Assert.Equal(SessionBroadcaster.DefaultSubscriberLimit, broadcaster.SubscriberCount);
    }
}
