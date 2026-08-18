using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class ArchivedProgrammeTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AProgrammeThatEndedIsKept()
        => Assert.NotNull(ArchivedProgramme.Of(Programme(endsAt: At.AddMinutes(30)), At));

    [Fact]
    public void AProgrammeWhoseEndIsStillOpenIsNotKeptYet()
        => Assert.Null(ArchivedProgramme.Of(Programme(endsAt: null), At));

    [Fact]
    public void AShadowIsNotKept()
        => Assert.Null(ArchivedProgramme.Of(Programme(endsAt: At.AddMinutes(30), isShadow: true), At));

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

    private static ArchivedProgramme Archived(string name, string summary, bool hasSubtitles = false)
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
            At);

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
