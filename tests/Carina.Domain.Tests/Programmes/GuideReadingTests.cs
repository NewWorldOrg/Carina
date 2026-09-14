using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class GuideReadingTests
{
    private static readonly DateTime Heard = new(2026, 8, 26, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AGuideNoReadingHasEverHeardWholeSaysNothing()
        => Assert.Equal(GuideStanding.NothingKnown, GuideReading.Of(null, null));

    [Fact]
    public void ABroadcastMissingFromAServiceThatWasHeardWholeIsNoLongerAnnounced()
        => Assert.Equal(GuideStanding.NoLongerAnnounced, GuideReading.Of(null, Heard));

    [Fact]
    public void ARowCarryingAnOlderMarkThanTheNewestWholeReadingIsNoLongerAnnounced()
        => Assert.Equal(
            GuideStanding.NoLongerAnnounced,
            GuideReading.Of(Announced(heardAt: Heard.AddMinutes(-30)), Heard));

    [Fact]
    public void ARowCarryingTheNewestMarkIsStillAnnounced()
        => Assert.Equal(GuideStanding.Announced, GuideReading.Of(Announced(heardAt: Heard), Heard));

    [Fact]
    public void ARowThatNoWholeReadingHasEverCoveredIsStillAnnounced()
        => Assert.Equal(GuideStanding.Announced, GuideReading.Of(Announced(), null));

    [Fact]
    public void ASkeletonSaysNothingAboutTheBroadcastItStandsFor()
        => Assert.Equal(GuideStanding.NothingKnown, GuideReading.Of(Announced(shadow: true), null));

    private static Programme Announced(DateTime? heardAt = null, bool shadow = false)
    {
        Programme programme = Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(32736), new ServiceId(1024), new EventId(4001)),
                new TransportStreamId(32736),
                Heard.AddHours(1),
                Heard.AddHours(2),
                shadow ? string.Empty : "A programme",
                string.Empty,
                shadow),
            Heard.AddHours(-1));

        if (heardAt is { } at)
        {
            programme.Heard(at);
        }

        return programme;
    }
}
