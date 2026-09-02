using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class TranscodeBudgetTypeTests
{
    [Fact]
    public void TheMachineRunsFourTranscodersAtOnceUnlessItIsToldOtherwise()
    {
        Assert.Equal(4, new TranscodeBudgetSettings().AtOnce);
    }

    [Fact]
    public void ACeilingOfNoneIsRefusedBecauseItIsARouteThatNeverPlaysRatherThanABoundedOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscodeBudgetSettings { AtOnce = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscodeBudgetSettings { AtOnce = -1 });
        Assert.Equal(1, new TranscodeBudgetSettings { AtOnce = 1 }.AtOnce);
        Assert.Equal(TranscodeBudgetSettings.Fewest, new TranscodeBudgetSettings { AtOnce = 1 }.AtOnce);
    }

    [Fact]
    public void ACeilingSaysHowManyAreRunningAndHowManyTheMachineIsAskedToRun()
    {
        TranscodeCeiling ceiling = new(3, 3);

        Assert.Equal(3, ceiling.Running);
        Assert.Equal(3, ceiling.AtOnce);
        Assert.Contains("3 transcoder", ceiling.Said, StringComparison.Ordinal);
        Assert.Contains("live and playback together", ceiling.Said, StringComparison.Ordinal);
    }

    [Fact]
    public void ACeilingIsOnlyReachedWhenAsManyAreRunningAsTheMachineIsAskedTo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscodeCeiling(2, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscodeCeiling(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscodeCeiling(1, -1));
        Assert.Equal(4, new TranscodeCeiling(4, 3).Running);
    }

    [Fact]
    public void AClaimThatWasSeatedCarriesTheSeatAndNoRefusal()
    {
        HeldSeat seat = new(TranscodePurpose.Live, 1, 4);

        TranscodeClaim claim = TranscodeClaim.Seated(seat);

        Assert.True(claim.Taken);
        Assert.Same(seat, claim.Seat);
        Assert.Null(claim.Refusal);
    }

    [Fact]
    public void AClaimThatWasRefusedCarriesTheCeilingAndNoSeat()
    {
        TranscodeCeiling ceiling = new(4, 4);

        TranscodeClaim claim = TranscodeClaim.Refused(ceiling);

        Assert.False(claim.Taken);
        Assert.Null(claim.Seat);
        Assert.Same(ceiling, claim.Refusal);
    }

    [Fact]
    public void AClaimIsMadeOfSomethingRatherThanNothing()
    {
        Assert.Throws<ArgumentNullException>(() => TranscodeClaim.Seated(null!));
        Assert.Throws<ArgumentNullException>(() => TranscodeClaim.Refused(null!));
    }

    [Fact]
    public void ATranscoderIsRaisedForOneOfTheTwoPurposesThereAre()
    {
        Assert.Equal(
            [TranscodePurpose.Live, TranscodePurpose.Playback],
            Enum.GetValues<TranscodePurpose>().Order().ToArray());
    }

    [Fact]
    public void ALiveStartRefusedAtTheCeilingSaysSoInTypeAndInWords()
    {
        TranscodeCeiling ceiling = new(4, 4);

        LiveTranscoderStart refused = LiveTranscoderStart.Refused(ceiling);

        Assert.False(refused.Running);
        Assert.Null(refused.Transcoder);
        Assert.Equal(TranscoderFault.TooManyAlready, refused.Fault);
        Assert.Same(ceiling, refused.Ceiling);
        Assert.Equal(ceiling.Said, refused.Note);
    }

    [Fact]
    public void ALiveStartThatFailedAnotherWayCarriesNoCeiling()
    {
        LiveTranscoderStart failed = LiveTranscoderStart.Failed(TranscoderFault.ProgrammeMissing, "gone");

        Assert.Null(failed.Ceiling);
        Assert.Throws<ArgumentNullException>(() => LiveTranscoderStart.Refused(null!));
    }

    private sealed class HeldSeat(TranscodePurpose purpose, int place, int atOnce) : ITranscodeSeat
    {
        public TranscodePurpose Purpose { get; } = purpose;

        public int Place { get; } = place;

        public int AtOnce { get; } = atOnce;

        public void Dispose()
        {
        }
    }
}
