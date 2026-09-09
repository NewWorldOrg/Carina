using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class EpgComparisonTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Opens = Now.AddDays(4);

    [Fact]
    public void AGuideSayingWhatTheReservationAlreadySaysHasNothingToReport()
        => Assert.Empty(EpgComparison.Of(Booked(), Announced(Opens, Opens.AddHours(1)), Now));

    [Fact]
    public void ABroadcastSlippingByLessThanAMinuteIsNotWorthTellingAnybodyAbout()
        => Assert.Empty(EpgComparison.Of(
            Booked(),
            Announced(Opens.AddSeconds(59), Opens.AddHours(1).AddSeconds(59)),
            Now));

    [Fact]
    public void ABroadcastMovedFurtherThanThatSaysWhereItWasAndWhereItIsNow()
    {
        EpgDivergence moved = Assert.Single(
            EpgComparison.Of(Booked(), Announced(Opens.AddMinutes(30), Opens.AddMinutes(90)), Now),
            divergence => divergence.Field is DivergedField.StartAt);

        Assert.Equal(Opens.ToString("O"), moved.Before);
        Assert.Equal(Opens.AddMinutes(30).ToString("O"), moved.After);
        Assert.Equal(Now, moved.DetectedAt);
    }

    [Fact]
    public void ABroadcastThatOnlyRunsLongerReportsItsEndAndNothingElse()
    {
        EpgDivergence ends = Assert.Single(
            EpgComparison.Of(Booked(), Announced(Opens, Opens.AddHours(2)), Now));

        Assert.Equal(DivergedField.EndAt, ends.Field);
        Assert.Equal(Opens.AddHours(2).ToString("O"), ends.After);
    }

    [Fact]
    public void ARenamedBroadcastIsTheSameBroadcastUnderANewTitle()
    {
        EpgDivergence renamed = Assert.Single(
            EpgComparison.Of(Booked(), Announced(Opens, Opens.AddHours(1), "その後の題名"), Now));

        Assert.Equal(DivergedField.Name, renamed.Field);
        Assert.Equal("もとの題名", renamed.Before);
        Assert.Equal("その後の題名", renamed.After);
    }

    [Fact]
    public void ABroadcastWithNoTitleInTheGuideDoesNotWipeTheOneThatWasReserved()
        => Assert.Empty(EpgComparison.Of(Booked(), Announced(Opens, Opens.AddHours(1), string.Empty), Now));

    [Fact]
    public void ABroadcastThatMovedAndWasRenamedReportsBoth()
    {
        IReadOnlyList<EpgDivergence> found = EpgComparison.Of(
            Booked(),
            Announced(Opens.AddHours(1), Opens.AddHours(2), "その後の題名"),
            Now);

        Assert.Equal(
            [DivergedField.StartAt, DivergedField.EndAt, DivergedField.Name],
            found.Select(divergence => divergence.Field));
    }

    [Fact]
    public void ABroadcastWithNoAnnouncedEndIsHeldAgainstTheProvisionalLength()
        => Assert.Equal(
            Opens + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced,
            EpgComparison.EndOf(Announced(Opens, null)));

    private static Reservation Booked()
        => Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(1), new ServiceId(2), new EventId(3), Opens),
            null,
            Priority.Default,
            Opens,
            Opens.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot("もとの題名", "何の話か", string.Empty, [], Now),
            null,
            BroadcastGroupRole.Standalone,
            ReservationState.Scheduled,
            null,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Now);

    private static Programme Announced(DateTime startsAt, DateTime? endsAt, string name = "もとの題名")
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(1), new ServiceId(2), new EventId(3)),
            new TransportStreamId(4),
            startsAt,
            endsAt,
            name,
            "何の話か",
            false,
            Now);
}
