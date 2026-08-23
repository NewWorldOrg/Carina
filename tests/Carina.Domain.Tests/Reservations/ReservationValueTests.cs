using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationValueTests
{
    [Theory]
    [InlineData(Priority.MinValue - 1)]
    [InlineData(Priority.MaxValue + 1)]
    public void APriorityOutsideItsRangeIsRefused(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Priority(value));

    [Theory]
    [InlineData(Priority.MinValue)]
    [InlineData(Priority.DefaultValue)]
    [InlineData(Priority.MaxValue)]
    public void APriorityInsideItsRangeIsKept(int value)
        => Assert.Equal(value, new Priority(value).Value);

    [Fact]
    public void AMarginIsAWholeNumberOfSecondsWithinAnHour()
    {
        Assert.Equal(30, Margin.OfSeconds(30).Seconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => Margin.OfSeconds(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Margin(Margin.Longest + TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => new Margin(TimeSpan.FromMilliseconds(1500)));
    }

    [Fact]
    public void ABroadcastGroupKeyCarriesAName()
    {
        Assert.Equal("relay-4001", new BroadcastGroupKey("relay-4001").Value);
        Assert.Throws<ArgumentException>(() => new BroadcastGroupKey(" "));
        Assert.Throws<ArgumentException>(() => new BroadcastGroupKey(" relay-4001"));
        Assert.Throws<ArgumentException>(() => new BroadcastGroupKey(new string('x', BroadcastGroupKey.MaxLength + 1)));
    }

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
