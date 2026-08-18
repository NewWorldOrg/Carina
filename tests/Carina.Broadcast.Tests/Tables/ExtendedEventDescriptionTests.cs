using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class ExtendedEventDescriptionTests
{
    private const int SomeService = 1024;

    [Fact]
    public void AnItemContinuedWithoutAHeadingBelongsToTheOneBeforeIt()
    {
        var detailed = Detailed(
            Descriptor(0, 0, Item("A", "one"), Item(string.Empty, "two")));

        var only = Assert.Single(detailed.Items);

        Assert.Equal("A", only.Heading);
        Assert.Equal("onetwo", only.Text);
    }

    [Fact]
    public void AnItemContinuedInTheNextDescriptorIsStillTheSameItem()
    {
        var detailed = Detailed(
            Descriptor(0, 1, Item("A", "one")),
            Descriptor(1, 1, Item(string.Empty, "two")));

        var only = Assert.Single(detailed.Items);

        Assert.Equal("A", only.Heading);
        Assert.Equal("onetwo", only.Text);
    }

    [Fact]
    public void DescriptorsAreReadInTheOrderTheyNumberThemselvesNotTheOrderTheyArrive()
    {
        var detailed = Detailed(
            Descriptor(1, 1, Item(string.Empty, "two")),
            Descriptor(0, 1, Item("A", "one")));

        var only = Assert.Single(detailed.Items);

        Assert.Equal("onetwo", only.Text);
    }

    [Fact]
    public void AWordSplitBetweenDescriptorsIsPutBackTogetherBeforeItIsRead()
    {
        var detailed = Detailed(
            Descriptor(0, 1, Item("A", [0x1B, 0x24, 0x42, 0x46])),
            Descriptor(1, 1, Item(string.Empty, [0x7C])));

        Assert.Equal("日", Assert.Single(detailed.Items).Text);
    }

    [Fact]
    public void TheTrailingTextOfEveryDescriptorIsGatheredInOrder()
    {
        var detailed = Detailed(
            Descriptor(0, 1, [], "one"),
            Descriptor(1, 1, [], "two"));

        Assert.Equal("onetwo", detailed.Text);
    }

    [Fact]
    public void AnEventWithoutTheseDescriptorsHasNoLongDescription()
    {
        Assert.False(ExtendedEventDescription.TryRead(Read().Descriptors, out _));
    }

    [Fact]
    public void ADescriptorClaimingMoreItemsThanItCarriesIsRefused()
    {
        var descriptor = new byte[] { DescriptorTags.ExtendedEvent, 0x06, 0x00, 0x6A, 0x70, 0x6E, 0x40, 0x00 };

        Assert.False(ExtendedEventDescription.TryRead(Read(descriptor).Descriptors, out _));
    }

    [Fact]
    public void AnItemClaimingMoreTextThanItCarriesIsRefused()
    {
        var descriptor = new byte[]
        {
            DescriptorTags.ExtendedEvent, 0x09, 0x00, 0x6A, 0x70, 0x6E, 0x04, 0x01, 0x41, 0x40, 0x00,
        };

        Assert.False(ExtendedEventDescription.TryRead(Read(descriptor).Descriptors, out _));
    }

    private static ExtendedEventDescription Detailed(params byte[][] descriptors)
    {
        var carried = Read([.. descriptors.SelectMany(descriptor => descriptor)]);

        Assert.True(ExtendedEventDescription.TryRead(carried.Descriptors, out var detailed));

        return detailed;
    }

    private static DescribedEvent Read(params byte[] descriptors)
    {
        var table = Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = SomeService,
                Body = [.. Header(), .. Event(descriptors.Length), .. descriptors],
            }))).Table;

        return Assert.Single(table.Events);
    }

    private static byte[] Descriptor(int number, int last, params byte[][] items)
        => Descriptor(number, last, [.. items.SelectMany(item => item)], string.Empty);

    private static byte[] Descriptor(int number, int last, byte[] items, string text)
    {
        var written = Ascii(text);
        var body = new List<byte>
        {
            (byte)((number << 4) | last),
            0x6A,
            0x70,
            0x6E,
            (byte)items.Length,
        };

        body.AddRange(items);
        body.Add((byte)written.Length);
        body.AddRange(written);

        return [DescriptorTags.ExtendedEvent, (byte)body.Count, .. body];
    }

    private static byte[] Item(string heading, string text) => Item(heading, Ascii(text));

    private static byte[] Item(string heading, byte[] text)
    {
        var written = Ascii(heading);

        return [(byte)written.Length, .. written, (byte)text.Length, .. text];
    }

    private static byte[] Ascii(string text)
        => text.Length == 0 ? [] : [0x1B, 0x28, 0x4A, .. text.Select(letter => (byte)letter)];

    private static byte[] Header() => [0x7F, 0xE3, 0x7F, 0xE3, 0x00, 0x4E];

    private static byte[] Event(int descriptorsLength = 0)
        =>
        [
            0x00, 0x01,
            0xEF, 0x55, 0x22, 0x57, 0x00,
            0x00, 0x03, 0x00,
            (byte)((descriptorsLength >> 8) & 0x0F), (byte)(descriptorsLength & 0xFF),
        ];
}
