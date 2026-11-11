using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class ProgrammeEndTests
{
    private static readonly DateTime Starts = new(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc);

    [Theory(DisplayName = "BR-CD-005: a recorded programme ends when the guide says, else when its reservation says once that end was confirmed, else where its recording was promised to end")]
    [InlineData(30, 45, true, 60, 30)]
    [InlineData(null, 45, true, 60, 45)]
    [InlineData(null, 45, false, 60, 60)]
    [InlineData(null, null, false, 60, 60)]
    [InlineData(null, null, false, null, null)]
    public void ARecordedProgrammeEndsByTheGuideTheReservationOrThePromise(
        int? guided,
        int? reserved,
        bool confirmed,
        int? promised,
        int? ends)
    {
        Assert.Equal(At(ends), ProgrammeEnd.Of(Starts, At(guided), At(reserved), confirmed, At(promised)));
    }

    [Theory(DisplayName = "BR-CD-005: an end no later than the programme starts is passed over for the next one that says")]
    [InlineData(0, 45, true, 60, 45)]
    [InlineData(-10, 45, true, 60, 45)]
    [InlineData(-10, 0, true, 60, 60)]
    [InlineData(null, -5, true, 60, 60)]
    [InlineData(null, null, false, 0, null)]
    [InlineData(-10, -10, true, -10, null)]
    public void AnEndNoLaterThanTheStartIsPassedOver(int? guided, int? reserved, bool confirmed, int? promised, int? ends)
    {
        Assert.Equal(At(ends), ProgrammeEnd.Of(Starts, At(guided), At(reserved), confirmed, At(promised)));
    }

    [Fact(DisplayName = "BR-CD-005: a programme the guide no longer holds, whose reservation never had its end confirmed, ends where its recording was promised to")]
    public void WithoutTheGuideOrAConfirmedEndTheProgrammeEndsWhereTheRecordingWasPromised()
    {
        Assert.Equal(Starts.AddMinutes(30), ProgrammeEnd.Of(Starts, null, Starts.AddMinutes(45), false, Starts.AddMinutes(30)));
    }

    private static DateTime? At(int? minutes) => minutes is { } after ? Starts.AddMinutes(after) : null;
}
