using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Programmes;

public sealed class ProgrammeSupersessionTests
{
    private static readonly DateTime Start = new(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime HeardTheDayBefore = new(2026, 8, 17, 20, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime HeardToday = new(2026, 8, 18, 3, 0, 0, DateTimeKind.Utc);

    private static readonly int[] OldScheduleMinutes = [0, 30, 60, 85, 120, 150, 180, 185, 210, 240];

    [Fact]
    public void AReplacementHeardLaterLeavesOutTheOldScheduleItCoversAndKeepsTheProgrammesAroundIt()
    {
        Programme before = On(1, Start.AddHours(-1), Start, HeardTheDayBefore);
        Programme replacement = On(101, Start, Start.AddMinutes(250), HeardToday);
        Programme after = On(102, Start.AddMinutes(250), Start.AddHours(5), HeardToday);

        IReadOnlyList<Programme> standing = ProgrammeSupersession.Standing(
            [before, .. OldSchedule(HeardTheDayBefore), replacement, after]);

        Assert.Equal([1, 101, 102], Events(standing));
    }

    [Fact]
    public void AnOldProgrammeTheReplacementOnlyPartlyCoversIsLeftOutAsWell()
    {
        Programme replacement = On(101, Start, Start.AddMinutes(250), HeardToday);
        Programme tail = On(8, Start.AddMinutes(185), Start.AddMinutes(240), HeardTheDayBefore);
        Programme overrun = On(9, Start.AddMinutes(240), Start.AddMinutes(255), HeardTheDayBefore);
        Programme announcedAfter = On(102, Start.AddMinutes(250), Start.AddHours(5), HeardToday);

        IReadOnlyList<Programme> standing = ProgrammeSupersession.Standing(
            [replacement, tail, overrun, announcedAfter]);

        Assert.Equal([101, 102], Events(standing));
    }

    [Fact]
    public void ProgrammesHeardAtTheSameMomentAreBothKeptWhereTheyShareAirtime()
    {
        Programme one = On(1, Start, Start.AddHours(1), HeardToday);
        Programme other = On(2, Start.AddMinutes(30), Start.AddHours(2), HeardToday);

        Assert.Equal([1, 2], Events(ProgrammeSupersession.Standing([one, other])));
    }

    [Fact]
    public void AProgrammeThatOnlyTouchesANewerOneAtEitherEndIsKept()
    {
        Programme newer = On(101, Start.AddHours(1), Start.AddHours(2), HeardToday);
        Programme endingAsItStarts = On(1, Start, Start.AddHours(1), HeardTheDayBefore);
        Programme startingAsItEnds = On(2, Start.AddHours(2), Start.AddHours(3), HeardTheDayBefore);

        Assert.Equal(
            [1, 2, 101],
            Events(ProgrammeSupersession.Standing([endingAsItStarts, startingAsItEnds, newer])));
    }

    [Fact]
    public void ANewerProgrammeWhoseEndIsNotAnnouncedReachesAnHourPastItsStart()
    {
        Programme newer = On(101, Start, null, HeardToday);
        Programme insideTheHour = On(1, Start.AddMinutes(59), Start.AddMinutes(90), HeardTheDayBefore);
        Programme pastTheHour = On(2, Start.AddHours(1), Start.AddMinutes(90), HeardTheDayBefore);

        Assert.Equal([2, 101], Events(ProgrammeSupersession.Standing([newer, insideTheHour, pastTheHour])));
    }

    [Fact]
    public void AnOlderProgrammeWhoseEndIsNotAnnouncedReachesAnHourPastItsStart()
    {
        Programme older = On(1, Start, null, HeardTheDayBefore);

        Assert.True(ProgrammeSupersession.Supersedes(
            On(101, Start.AddMinutes(59), Start.AddMinutes(90), HeardToday),
            older));
        Assert.False(ProgrammeSupersession.Supersedes(
            On(102, Start.AddHours(1), Start.AddMinutes(90), HeardToday),
            older));
    }

    [Fact]
    public void APlaceholderNeitherTakesTheSlotNorLosesIt()
    {
        Programme placeholder = On(101, Start, Start.AddHours(1), HeardToday, shadow: true);
        Programme older = On(1, Start, Start.AddHours(1), HeardTheDayBefore);
        Programme placeholderHeardBefore = On(2, Start, Start.AddHours(1), HeardTheDayBefore, shadow: true);
        Programme newer = On(102, Start, Start.AddHours(1), HeardToday);

        Assert.Equal([1, 101], Events(ProgrammeSupersession.Standing([placeholder, older])));
        Assert.Equal([2, 102], Events(ProgrammeSupersession.Standing([placeholderHeardBefore, newer])));
    }

    [Fact]
    public void AProgrammeNoWholeReadingHasMarkedCountsAsHeardBeforeEveryOther()
    {
        Programme unheard = On(1, Start, Start.AddHours(1), null);
        Programme heard = On(101, Start.AddMinutes(30), Start.AddHours(2), HeardTheDayBefore);
        Programme unheardOverTheHeard = On(2, Start.AddHours(1), Start.AddHours(2), null);
        Programme alsoUnheard = On(3, Start.AddMinutes(10), Start.AddMinutes(20), null);

        Assert.True(ProgrammeSupersession.Supersedes(heard, unheard));
        Assert.False(ProgrammeSupersession.Supersedes(unheardOverTheHeard, heard));
        Assert.False(ProgrammeSupersession.Supersedes(alsoUnheard, unheard));
        Assert.False(ProgrammeSupersession.Supersedes(unheard, alsoUnheard));
    }

    [Fact]
    public void AProgrammeThatAlreadyAiredStaysWhenNothingNewerSharesItsAirtime()
    {
        Programme aired = On(1, Start.AddDays(-1), Start.AddDays(-1).AddHours(1), HeardTheDayBefore.AddDays(-1));
        Programme announcedSince = On(101, Start.AddDays(-1).AddHours(1), Start.AddDays(-1).AddHours(2), HeardToday);

        Assert.Equal([1, 101], Events(ProgrammeSupersession.Standing([aired, announcedSince])));
    }

    [Fact]
    public void AProgrammeOnAnotherServiceTakesNothingFromThisOne()
    {
        Programme here = On(1, Start, Start.AddHours(1), HeardTheDayBefore);
        Programme elsewhere = On(101, Start, Start.AddHours(1), HeardToday, service: 1050);

        Assert.Equal([1, 101], Events(ProgrammeSupersession.Standing([here, elsewhere])));
    }

    [Fact]
    public void TheOldScheduleStaysLeftOutAfterItAndItsReplacementMoveToTheArchive()
    {
        Programme replacement = On(101, Start, Start.AddMinutes(250), HeardToday);
        Programme after = On(102, Start.AddMinutes(250), Start.AddHours(5), HeardToday);
        ArchivedProgramme[] kept = [.. new[] { replacement, after }.Concat(OldSchedule(HeardTheDayBefore)).Select(Moved)];

        Assert.Equal([101, 102], Events(ProgrammeSupersession.Standing(kept, [])));
    }

    [Fact]
    public void AnOldScheduleKeptWithoutAHeardTimeIsLeftOutBesideAReplacementThatHasOne()
    {
        ArchivedProgramme[] unheard = [.. OldSchedule(null).Select(Moved)];
        Programme replacement = On(101, Start, Start.AddMinutes(250), HeardToday);

        Assert.Equal([101], Events(ProgrammeSupersession.Standing([.. unheard, Moved(replacement)], [])));
        Assert.Empty(ProgrammeSupersession.Standing(unheard, [replacement]));
    }

    [Fact]
    public void TwoKeptProgrammesWithoutAHeardTimeBothStay()
    {
        ArchivedProgramme one = Moved(On(1, Start, Start.AddHours(1), null));
        ArchivedProgramme other = Moved(On(2, Start.AddMinutes(30), Start.AddHours(2), null));

        Assert.Equal([1, 2], Events(ProgrammeSupersession.Standing([one, other], [])));
    }

    [Fact]
    public void AKeptProgrammeIsComparedWithTheGuideOnlyThroughProgrammesThatAreNotPlaceholders()
    {
        ArchivedProgramme kept = Moved(On(1, Start, Start.AddHours(1), HeardTheDayBefore));
        Programme placeholder = On(101, Start, Start.AddHours(1), HeardToday, shadow: true);
        Programme openEnded = On(102, Start.AddMinutes(-59), null, HeardToday);
        Programme touching = On(103, Start.AddHours(1), Start.AddHours(2), HeardToday);

        Assert.Single(ProgrammeSupersession.Standing([kept], [placeholder, touching]));
        Assert.Empty(ProgrammeSupersession.Standing([kept], [openEnded]));
    }

    [Fact]
    public void AnEndNotAnnouncedIsTheSameHourTheReservationsCountOn()
        => Assert.Equal(
            Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced,
            ProgrammeSupersession.LengthWhenTheEndIsNotAnnounced);

    private static Programme[] OldSchedule(DateTime? heard)
        =>
        [
            .. OldScheduleMinutes.Zip(OldScheduleMinutes.Skip(1)).Select((minutes, index) => On(
                index + 2,
                Start.AddMinutes(minutes.First),
                Start.AddMinutes(minutes.Second),
                heard)),
        ];

    private static int[] Events(IReadOnlyList<Programme> programmes)
        => [.. programmes.Select(programme => programme.EventId.Value).Order()];

    private static int[] Events(IReadOnlyList<ArchivedProgramme> programmes)
        => [.. programmes.Select(programme => programme.EventId.Value).Order()];

    private static ArchivedProgramme Moved(Programme programme)
        => ArchivedProgramme.Of(new EndedProgramme(programme, programme.EndsAt!.Value), Start.AddDays(2))!;

    private static Programme On(
        int eventId,
        DateTime startsAt,
        DateTime? endsAt,
        DateTime? heard,
        bool shadow = false,
        int service = 1049)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(4), new ServiceId(service), new EventId(eventId)),
            new TransportStreamId(32736),
            startsAt,
            endsAt,
            shadow ? string.Empty : $"番組{eventId}",
            string.Empty,
            shadow,
            HeardTheDayBefore,
            lastHeardAt: heard);
}
