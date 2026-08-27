using Carina.Domain.Channels;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class AllocationValueTests
{
    private static readonly DateTime Now = ReservationFactory.Now;

    private static readonly TuningParameters Somewhere = TuningParameters.Terrestrial(27);

    [Fact]
    public void TheProvisionalHorizonIsHalfAnHour()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), RollingHorizon.Provisional);
        Assert.Equal(RollingHorizon.Provisional, RollingHorizon.Default.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void AHorizonReachesForwards(int seconds)
    {
        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RollingHorizon(TimeSpan.FromSeconds(seconds)));

        Assert.Equal("value", refused.ParamName);
    }

    [Fact]
    public void OneSecondIsAHorizon()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), new RollingHorizon(TimeSpan.FromSeconds(1)).Value);
    }

    [Fact]
    public void AHorizonIsAWholeNumberOfSeconds()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => new RollingHorizon(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1)));

        Assert.Equal("value", refused.ParamName);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("programme")]
    [InlineData("priority")]
    public void ACandidateNamesTheReservationItStandsFor(string missing)
    {
        ArgumentNullException refused = Assert.Throws<ArgumentNullException>(() => new AllocationCandidate(
            missing is "id" ? null! : ReservationId.New(),
            missing is "programme" ? null! : ReservationFactory.Programme(),
            missing is "priority" ? null! : Priority.Default,
            Somewhere,
            Now,
            Now.AddHours(1),
            endAtConfirmed: true,
            pinned: false));

        Assert.Equal(missing, refused.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ACandidateHoldsATunerOverAWindowThatEndsAfterItOpens(int ticks)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => Candidate(Now, Now.AddTicks(ticks)));

        Assert.Equal("effectiveEndAt", refused.ParamName);
    }

    [Fact]
    public void OneTickIsAWindow()
    {
        Assert.Equal(Now.AddTicks(1), Candidate(Now, Now.AddTicks(1)).EffectiveEndAt);
    }

    [Theory]
    [InlineData("effectiveStartAt")]
    [InlineData("effectiveEndAt")]
    public void ACandidateKeepsItsWindowInUtc(string local)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => Candidate(
            local is "effectiveStartAt" ? DateTime.SpecifyKind(Now, DateTimeKind.Local) : Now,
            local is "effectiveEndAt"
                ? DateTime.SpecifyKind(Now.AddHours(1), DateTimeKind.Local)
                : Now.AddHours(1)));

        Assert.Equal(local, refused.ParamName);
    }

    [Fact]
    public void ACandidateIsMadeFromTheReservationItStandsFor()
    {
        Reservation reservation = ReservationFactory.Planned(
            priority: new Priority(42),
            marginBefore: Margin.OfSeconds(10),
            marginAfter: Margin.OfSeconds(30));

        AllocationCandidate candidate = AllocationCandidate.Of(reservation, Somewhere);

        Assert.Equal(reservation.Id, candidate.Id);
        Assert.Equal(reservation.Programme, candidate.Programme);
        Assert.Equal(42, candidate.Priority.Value);
        Assert.Equal(Somewhere, candidate.Tuning);
        Assert.Equal(reservation.EffectiveStartAt, candidate.EffectiveStartAt);
        Assert.Equal(reservation.EffectiveEndAt, candidate.EffectiveEndAt);
        Assert.True(candidate.EndAtConfirmed);
        Assert.False(candidate.Pinned);
    }

    [Fact]
    public void ACandidateMadeFromAClaimedReservationIsPinned()
    {
        Assert.True(AllocationCandidate.Of(ReservationFactory.Claimed(), Somewhere).Pinned);
    }

    [Fact]
    public void ACandidateSaysWhenTheEndWasNeverAnnounced()
    {
        Reservation reservation = ReservationFactory.Planned();
        reservation.Reframe(reservation.StartAt, reservation.EndAt, endAtConfirmed: false);

        Assert.False(AllocationCandidate.Of(reservation, Somewhere).EndAtConfirmed);
    }

    [Fact]
    public void ACandidateWithNowhereToTuneSaysSo()
    {
        Assert.Null(AllocationCandidate.Of(ReservationFactory.Planned(), null).Tuning);
    }

    [Fact]
    public void ACandidateIsMadeFromAReservation()
    {
        ArgumentNullException refused = Assert.Throws<ArgumentNullException>(
            () => AllocationCandidate.Of(null!, Somewhere));

        Assert.Equal("reservation", refused.ParamName);
    }

    private static AllocationCandidate Candidate(DateTime opens, DateTime closes)
        => new(
            ReservationId.New(),
            ReservationFactory.Programme(),
            Priority.Default,
            Somewhere,
            opens,
            closes,
            endAtConfirmed: true,
            pinned: false);
}
