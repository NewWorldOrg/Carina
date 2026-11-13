using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Tests.Unit.Recordings;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class RecordingTickTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    private static readonly DateTime Opens = Now.AddMinutes(1);

    private static readonly DateTime Closes = Now.AddMinutes(31);

    [Fact(DisplayName = "BR-KD-024: a start tried before the reservation's window opens is still ahead of it, and one tried on the instant it opens is not")]
    public void AStartBeforeTheWindowOpensIsStillAhead()
    {
        RecordingTick due = Tick(TimeSpan.Zero);

        Assert.True(due.IsStillAhead(Opens.AddTicks(-1)));
        Assert.False(due.IsStillAhead(Opens));
        Assert.False(due.IsStillAhead(Closes));
    }

    [Fact(DisplayName = "BR-KS-003: the programme of a reservation is still on until its window, less the margin after it, closes")]
    public void TheProgrammeIsStillOnUntilTheWindowLessItsMarginCloses()
    {
        RecordingTick due = Tick(TimeSpan.FromMinutes(2));
        DateTime ends = Closes.AddMinutes(-2);

        Assert.True(due.ProgrammeStillOn(ends.AddTicks(-1)));
        Assert.False(due.ProgrammeStillOn(ends));
        Assert.False(due.ProgrammeStillOn(Closes.AddTicks(-1)));
    }

    [Fact(DisplayName = "BR-KS-003: with no margin after it, the programme is on until the window closes")]
    public void WithNoMarginTheProgrammeIsOnUntilTheWindowCloses()
    {
        RecordingTick due = Tick(TimeSpan.Zero);

        Assert.True(due.ProgrammeStillOn(Closes.AddTicks(-1)));
        Assert.False(due.ProgrammeStillOn(Closes));
    }

    private static RecordingTick Tick(TimeSpan marginAfter)
    {
        ProgrammeRef programme = RecordingFactory.Programme();

        return new RecordingTick(
            ReservationId.New(),
            programme.NetworkId,
            programme.ServiceId,
            programme.EventId,
            programme.StartsAt,
            RecordingFactory.Snapshot(),
            Priority.Default,
            null,
            BroadcastGroupRole.Standalone,
            Opens,
            Closes,
            true,
            marginAfter,
            null);
    }
}
