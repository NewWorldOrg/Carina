using Carina.Api.Events;
using Carina.Domain.Programmes;

namespace Carina.Api.Tests.Unit;

public sealed class ProgrammeFeedReadersTests
{
    [Fact]
    public void HowManyFeedsRunAtOnceIsWhatTheSettingSays()
    {
        var readers = new ProgrammeFeedReaders(new ProgrammeFeedSettings { ConcurrentReaders = 3 });

        Assert.Equal(3, readers.Limit);
        Assert.Equal(3, readers.Free);
    }

    [Fact]
    public void EveryPlaceTheSettingAllowsIsHandedOut()
    {
        var readers = new ProgrammeFeedReaders(new ProgrammeFeedSettings { ConcurrentReaders = 3 });

        Assert.True(readers.TryTake(out IDisposable? first));
        Assert.True(readers.TryTake(out IDisposable? second));
        Assert.True(readers.TryTake(out IDisposable? third));
        Assert.Equal(0, readers.Free);

        first.Dispose();
        second.Dispose();
        third.Dispose();
    }

    [Fact]
    public void TheOneAfterTheLastPlaceIsTurnedAwayWithoutWaiting()
    {
        var readers = new ProgrammeFeedReaders(new ProgrammeFeedSettings { ConcurrentReaders = 2 });

        Assert.True(readers.TryTake(out IDisposable? first));
        Assert.True(readers.TryTake(out IDisposable? second));
        Assert.False(readers.TryTake(out IDisposable? turnedAway));
        Assert.Null(turnedAway);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void AFeedThatEndsLetsTheNextOneIn()
    {
        var readers = new ProgrammeFeedReaders(new ProgrammeFeedSettings { ConcurrentReaders = 1 });

        Assert.True(readers.TryTake(out IDisposable? first));
        Assert.False(readers.TryTake(out IDisposable? _));

        first.Dispose();

        Assert.True(readers.TryTake(out IDisposable? next));
        Assert.Equal(0, readers.Free);

        next.Dispose();
    }

    [Fact]
    public void APlaceGivenBackTwiceIsStillOnePlace()
    {
        var readers = new ProgrammeFeedReaders(new ProgrammeFeedSettings { ConcurrentReaders = 1 });

        Assert.True(readers.TryTake(out IDisposable? only));

        only.Dispose();
        only.Dispose();

        Assert.Equal(1, readers.Free);
    }

    [Fact]
    public void TheTimeOneStatementIsGivenIsCarriedAlongsideThePlaces()
        => Assert.Equal(
            TimeSpan.FromSeconds(5),
            new ProgrammeFeedReaders(new ProgrammeFeedSettings
            {
                StatementTimeout = TimeSpan.FromSeconds(5),
            }).StatementTimeout);
}
