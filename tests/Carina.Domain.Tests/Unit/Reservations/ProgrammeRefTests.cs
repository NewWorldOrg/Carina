using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class ProgrammeRefTests
{
    [Fact]
    public void TwoProgrammeReferencesWithTheSameEventIdButDifferentStartsAreNotTheSameProgramme()
    {
        DateTime first = new(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);
        var earlier = new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), first);
        var later = new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), first.AddDays(7));

        Assert.NotEqual(earlier, later);
        Assert.Equal(earlier.Id, later.Id);
        Assert.Equal(earlier, new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), first));
    }

    [Fact]
    public void AProgrammeReferenceKeepsItsTimeInUtc()
        => Assert.Throws<ArgumentException>(() => new ProgrammeRef(
            new NetworkId(32736),
            new ServiceId(1024),
            new EventId(4001),
            new DateTime(2026, 8, 24, 20, 0, 0, DateTimeKind.Local)));
}
