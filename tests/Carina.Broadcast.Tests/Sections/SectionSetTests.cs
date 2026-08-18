using Carina.Broadcast.Sections;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Sections;

public sealed class SectionSetTests
{
    private const int Pid = 0x0011;
    private const int SomeTableId = 0x42;
    private const int SomeExtension = 0x0100;

    [Fact]
    public void ATableIsIncompleteUntilEverySectionItAnnouncesHasArrived()
    {
        var set = new SectionSet(SomeTableId, SomeExtension);

        Assert.True(set.Add(SectionOf(version: 3, sectionNumber: 0, lastSectionNumber: 2)));
        Assert.True(set.Add(SectionOf(version: 3, sectionNumber: 2, lastSectionNumber: 2)));
        Assert.False(set.IsComplete);
        Assert.False(set.TryComplete(out IReadOnlyList<Section>? incomplete));
        Assert.Empty(incomplete);

        Assert.True(set.Add(SectionOf(version: 3, sectionNumber: 1, lastSectionNumber: 2)));
        Assert.True(set.IsComplete);
        Assert.True(set.TryComplete(out IReadOnlyList<Section>? complete));
        Assert.Equal<int>([0, 1, 2], complete.Select(section => section.SectionNumber).ToArray());
    }

    [Fact]
    public void ARepeatedSectionIsNotCountedTwice()
    {
        var set = new SectionSet(SomeTableId, SomeExtension);

        Assert.True(set.Add(SectionOf(version: 1, sectionNumber: 0, lastSectionNumber: 1)));
        Assert.False(set.Add(SectionOf(version: 1, sectionNumber: 0, lastSectionNumber: 1)));
        Assert.Equal(1, set.HeldCount);
        Assert.False(set.IsComplete);
    }

    [Fact]
    public void ANewVersionThrowsAwayWhatWasGatheredUnderTheOldOne()
    {
        var set = new SectionSet(SomeTableId, SomeExtension);

        set.Add(SectionOf(version: 4, sectionNumber: 0, lastSectionNumber: 1));
        set.Add(SectionOf(version: 4, sectionNumber: 1, lastSectionNumber: 1));
        Assert.True(set.IsComplete);

        Assert.True(set.Add(SectionOf(version: 5, sectionNumber: 1, lastSectionNumber: 1)));
        Assert.False(set.IsComplete);
        Assert.Equal(5, set.VersionNumber);
        Assert.Equal(1, set.HeldCount);
    }

    [Fact]
    public void ATableAnnouncedForLaterUseIsNotGatheredAsIfItWereInForce()
    {
        var set = new SectionSet(SomeTableId, SomeExtension);

        Assert.False(set.Add(SectionOf(version: 6, sectionNumber: 0, lastSectionNumber: 0, isCurrent: false)));
        Assert.Null(set.VersionNumber);
        Assert.False(set.IsComplete);
    }

    [Fact]
    public void ASectionOfAnotherTableOrAnotherNetworkIsNotThisSets()
    {
        var set = new SectionSet(SomeTableId, SomeExtension);

        Assert.False(set.Add(SectionOf(version: 1, sectionNumber: 0, lastSectionNumber: 0, tableId: 0x46)));
        Assert.False(set.Add(SectionOf(version: 1, sectionNumber: 0, lastSectionNumber: 0, extension: 0x0200)));
        Assert.Equal(0, set.HeldCount);
    }

    [Fact]
    public void ASectionNumberedAboveWhatTheTableAnnouncesIsRefused()
    {
        var set = new SectionSet(SomeTableId, SomeExtension);

        Assert.False(set.Add(SectionOf(version: 1, sectionNumber: 3, lastSectionNumber: 1)));
        Assert.Equal(0, set.HeldCount);
        Assert.False(set.IsComplete);
        Assert.False(set.TryComplete(out _));
        Assert.Null(set.VersionNumber);
    }

    [Fact]
    public void ARefusedSectionDoesNotShrinkTheTableItWasRefusedAgainst()
    {
        var set = new SectionSet(SomeTableId, SomeExtension);

        Assert.True(set.Add(SectionOf(version: 1, sectionNumber: 0, lastSectionNumber: 2)));
        Assert.False(set.Add(SectionOf(version: 1, sectionNumber: 5, lastSectionNumber: 0)));

        Assert.False(set.IsComplete);
        Assert.False(set.TryComplete(out IReadOnlyList<Section>? sections));
        Assert.Empty(sections);
        Assert.Equal(1, set.HeldCount);
    }

    [Fact]
    public void ARepeatAnnouncingAShorterTableDoesNotCompleteTheLongerOne()
    {
        var set = new SectionSet(SomeTableId, SomeExtension);

        Assert.True(set.Add(SectionOf(version: 1, sectionNumber: 0, lastSectionNumber: 2)));
        Assert.True(set.Add(SectionOf(version: 1, sectionNumber: 1, lastSectionNumber: 2)));
        Assert.False(set.Add(SectionOf(version: 1, sectionNumber: 0, lastSectionNumber: 1)));

        Assert.False(set.IsComplete);
        Assert.False(set.TryComplete(out _));
    }

    [Fact]
    public void ResettingLeavesNothingOfTheTableBehind()
    {
        var set = new SectionSet(SomeTableId, SomeExtension);

        set.Add(SectionOf(version: 1, sectionNumber: 0, lastSectionNumber: 0));
        set.Reset();

        Assert.Equal(0, set.HeldCount);
        Assert.Null(set.VersionNumber);
        Assert.False(set.IsComplete);
    }

    private static Section SectionOf(
        int version,
        int sectionNumber,
        int lastSectionNumber,
        bool isCurrent = true,
        int tableId = SomeTableId,
        int extension = SomeExtension)
    {
        byte[] bytes = new SectionWriter
        {
            TableId = tableId,
            TableIdExtension = extension,
            VersionNumber = version,
            IsCurrent = isCurrent,
            SectionNumber = sectionNumber,
            LastSectionNumber = lastSectionNumber,
            Body = SectionWriter.Filler(6),
        }.ToBytes();

        IReadOnlyList<SectionRead> read = new SectionAssembler(Pid).Push(new TransportStreamWriter(Pid).Sections(bytes).Packets[0]);

        return Assert.IsType<SectionRead.Assembled>(Assert.Single(read)).Section;
    }
}
