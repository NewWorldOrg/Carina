using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class TranscodeClaimTests
{
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
