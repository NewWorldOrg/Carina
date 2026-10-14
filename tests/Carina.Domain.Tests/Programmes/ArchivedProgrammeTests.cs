using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class ArchivedProgrammeTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AProgrammeThatEndedIsKept()
        => Assert.NotNull(ArchivedProgramme.Of(
            new EndedProgramme(Programme(endsAt: At.AddMinutes(30)), At.AddMinutes(30)),
            At));

    [Fact]
    public void AProgrammeWhoseEndWasNeverToldIsKeptWithTheEndItWasFoundToHave()
    {
        ArchivedProgramme? kept = ArchivedProgramme.Of(
            new EndedProgramme(Programme(endsAt: null), At.AddMinutes(45)),
            At);

        Assert.NotNull(kept);
        Assert.Equal(At.AddMinutes(45), kept.EndsAt);
    }

    [Fact]
    public void AShadowIsNotKept()
        => Assert.Null(ArchivedProgramme.Of(
            new EndedProgramme(Programme(endsAt: At.AddMinutes(30), isShadow: true), At.AddMinutes(30)),
            At));

    [Fact]
    public void TheMoveCarriesWhenTheProgrammeWasLastHeard()
    {
        Programme programme = Programme(endsAt: At.AddMinutes(30));
        programme.Heard(At.AddHours(-2));

        Assert.Equal(
            At.AddHours(-2),
            ArchivedProgramme.Of(new EndedProgramme(programme, At.AddMinutes(30)), At)?.LastHeardAt);
    }

    [Fact]
    public void AProgrammeNoWholeReadingMarkedIsKeptWithoutAHeardTime()
        => Assert.Null(ArchivedProgramme.Of(
            new EndedProgramme(Programme(endsAt: At.AddMinutes(30)), At.AddMinutes(30)),
            At)?.LastHeardAt);

    [Fact]
    public void KeepingTheSameProgrammeAgainKeepsTheLaterHeardTime()
    {
        ArchivedProgramme held = Archived("ニュース", string.Empty, heard: At.AddHours(-1));

        Assert.False(held.AbsorbTheRicherOf(Archived("ニュース", string.Empty, heard: At.AddHours(-3))));
        Assert.True(held.AbsorbTheRicherOf(Archived("ニュース", string.Empty, heard: At)));
        Assert.Equal(At, held.LastHeardAt);
    }

    [Fact]
    public void TheFullerNameWins()
    {
        ArchivedProgramme held = Archived("ニュース", string.Empty);

        Assert.True(held.AbsorbTheRicherOf(Archived("ニュース7 首都圏", string.Empty)));
        Assert.Equal("ニュース7 首都圏", held.Name);
    }

    [Fact]
    public void AThinnerNameDoesNotReplaceAFullerOne()
    {
        ArchivedProgramme held = Archived("ニュース7 首都圏", "詳しく");

        Assert.False(held.AbsorbTheRicherOf(Archived("ニュース", string.Empty)));
        Assert.Equal("ニュース7 首都圏", held.Name);
        Assert.Equal("詳しく", held.Summary);
    }

    [Fact]
    public void SubtitlesSeenByEitherSideAreRemembered()
    {
        ArchivedProgramme held = Archived("ニュース", string.Empty);

        held.AbsorbTheRicherOf(Archived("ニュース", string.Empty, hasSubtitles: true));

        Assert.True(held.HasSubtitles);
    }

    [Fact]
    public void SubtitlesAreNotForgottenByALaterViewThatMissedThem()
    {
        ArchivedProgramme held = Archived("ニュース", string.Empty, hasSubtitles: true);

        held.AbsorbTheRicherOf(Archived("ニュース", string.Empty));

        Assert.True(held.HasSubtitles);
    }

    private static ArchivedProgramme Archived(
        string name,
        string summary,
        bool hasSubtitles = false,
        DateTime? heard = null)
        => ArchivedProgramme.Rehydrate(
            new NetworkId(4),
            new ServiceId(1049),
            new EventId(1),
            At,
            At.AddMinutes(30),
            name,
            summary,
            hasSubtitles,
            [],
            [],
            At,
            heard);

    private static Programme Programme(DateTime? endsAt, bool isShadow = false)
        => Domain.Programmes.Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(4), new ServiceId(1049), new EventId(1)),
                new TransportStreamId(32_736),
                At,
                endsAt,
                "ニュース",
                string.Empty,
                isShadow),
            At);
}
