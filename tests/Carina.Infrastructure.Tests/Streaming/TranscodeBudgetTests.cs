using System.Collections.Concurrent;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class TranscodeBudgetTests
{
    [Fact]
    public void AsManyAreSeatedAsTheMachineIsAskedToRunAndTheNextIsTurnedAway()
    {
        TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 3 });

        TranscodeClaim first = budget.Claim(TranscodePurpose.Playback);
        TranscodeClaim second = budget.Claim(TranscodePurpose.Playback);
        TranscodeClaim third = budget.Claim(TranscodePurpose.Playback);
        TranscodeClaim fourth = budget.Claim(TranscodePurpose.Playback);

        Assert.True(first.Taken);
        Assert.True(second.Taken);
        Assert.True(third.Taken);
        Assert.False(fourth.Taken);
        Assert.Equal(3, budget.Running);
    }

    [Fact]
    public void TheRefusalSaysHowManyAreRunningAndWhatTheCeilingIs()
    {
        TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 2 });

        budget.Claim(TranscodePurpose.Live);
        budget.Claim(TranscodePurpose.Playback);
        TranscodeClaim refused = budget.Claim(TranscodePurpose.Live);

        Assert.Null(refused.Seat);
        Assert.Equal(2, refused.Refusal!.Running);
        Assert.Equal(2, refused.Refusal.AtOnce);
        Assert.Contains("2 transcoder", refused.Refusal.Said, StringComparison.Ordinal);
    }

    [Fact]
    public void ASeatSaysWhichPlaceItIsAndWhatItWasClaimedFor()
    {
        TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

        ITranscodeSeat live = budget.Claim(TranscodePurpose.Live).Seat!;
        ITranscodeSeat playback = budget.Claim(TranscodePurpose.Playback).Seat!;

        Assert.Equal(TranscodePurpose.Live, live.Purpose);
        Assert.Equal(1, live.Place);
        Assert.Equal(TranscodePurpose.Playback, playback.Purpose);
        Assert.Equal(2, playback.Place);
        Assert.Equal(4, playback.AtOnce);
    }

    [Fact]
    public void ASeatComesBackWhenItIsLetGoAndNotBefore()
    {
        TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 1 });

        ITranscodeSeat held = budget.Claim(TranscodePurpose.Playback).Seat!;

        Assert.False(budget.Claim(TranscodePurpose.Playback).Taken);
        Assert.False(budget.Claim(TranscodePurpose.Live).Taken);
        Assert.Equal(1, budget.Running);

        held.Dispose();

        Assert.Equal(0, budget.Running);
        Assert.True(budget.Claim(TranscodePurpose.Live).Taken);
    }

    [Fact]
    public void LettingTheSameSeatGoTwiceHandsBackOnePlaceRatherThanTwo()
    {
        TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 2 });

        ITranscodeSeat first = budget.Claim(TranscodePurpose.Live).Seat!;
        budget.Claim(TranscodePurpose.Live);

        first.Dispose();
        first.Dispose();

        Assert.Equal(1, budget.Running);
        Assert.True(budget.Claim(TranscodePurpose.Live).Taken);
        Assert.False(budget.Claim(TranscodePurpose.Live).Taken);
    }

    [Fact]
    public void LiveAndPlaybackDrawOnTheOneBudget()
    {
        TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 2 });

        Assert.True(budget.Claim(TranscodePurpose.Live).Taken);
        Assert.True(budget.Claim(TranscodePurpose.Playback).Taken);

        TranscodeClaim live = budget.Claim(TranscodePurpose.Live);
        TranscodeClaim playback = budget.Claim(TranscodePurpose.Playback);

        Assert.False(live.Taken);
        Assert.False(playback.Taken);
        Assert.Equal(2, live.Refusal!.Running);
    }

    [Fact]
    public void TheCeilingIsWhateverTheSettingsSay()
    {
        TranscodeBudget one = new(new TranscodeBudgetSettings { AtOnce = 1 });
        TranscodeBudget six = new(new TranscodeBudgetSettings { AtOnce = 6 });

        Assert.Single(Seated(one, 10));
        Assert.Equal(6, Seated(six, 10).Count);
    }

    [Fact]
    public void ClaimsMadeAtOnceFromManyThreadsNeverSeatMoreThanTheCeiling()
    {
        TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 5 });
        ConcurrentBag<ITranscodeSeat> seated = [];

        Parallel.For(0, 200, _ =>
        {
            if (budget.Claim(TranscodePurpose.Live).Seat is { } seat)
            {
                seated.Add(seat);
            }
        });

        Assert.Equal(5, seated.Count);
        Assert.Equal(5, budget.Running);
        Assert.Equal([1, 2, 3, 4, 5], seated.Select(seat => seat.Place).Order().ToArray());
    }

    [Fact]
    public void ABudgetIsGivenItsSettingsRatherThanNone()
    {
        Assert.Throws<ArgumentNullException>(() => new TranscodeBudget(null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TranscodeBudget(new TranscodeBudgetSettings()).Claim((TranscodePurpose)9));
    }

    private static List<ITranscodeSeat> Seated(TranscodeBudget budget, int asked)
    {
        List<ITranscodeSeat> seats = [];

        for (int attempt = 0; attempt < asked; attempt++)
        {
            if (budget.Claim(TranscodePurpose.Playback).Seat is { } seat)
            {
                seats.Add(seat);
            }
        }

        return seats;
    }
}
