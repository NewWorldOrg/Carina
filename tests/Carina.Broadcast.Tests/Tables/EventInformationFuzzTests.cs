using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class EventInformationFuzzTests
{
    private const int SomeService = 1024;

    [Fact]
    public void NoSectionBodyAtAllMakesTheReaderThrowOrRunAway()
    {
        var random = new Random(20260818);

        for (var round = 0; round < 2000; round++)
        {
            var body = new byte[random.Next(0, 200)];
            random.NextBytes(body);

            var read = EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = SomeService,
                Body = body,
            }));

            if (read is not TableRead<EventInformationTable>.Parsed parsed)
            {
                continue;
            }

            foreach (var carried in parsed.Table.Events)
            {
                _ = carried.Described;
                _ = carried.Detailed;
                _ = carried.Genres;
                _ = carried.Components;
                _ = carried.AudioComponents;
                _ = carried.Groupings;
                _ = carried.EndsAt;
            }
        }
    }

    [Fact]
    public void NoRunOfDescriptorBytesAtAllMakesTheReadersThrow()
    {
        var random = new Random(20260819);

        for (var round = 0; round < 2000; round++)
        {
            var descriptors = new byte[random.Next(0, 120)];
            random.NextBytes(descriptors);

            var read = EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = SomeService,
                Body = [.. Header(), .. Event(descriptors.Length), .. descriptors],
            }));

            if (read is not TableRead<EventInformationTable>.Parsed parsed)
            {
                continue;
            }

            foreach (var carried in parsed.Table.Events)
            {
                _ = carried.Described;
                _ = carried.Detailed;
                _ = carried.Genres;
                _ = carried.Components;
                _ = carried.AudioComponents;
                _ = carried.Groupings;
            }
        }
    }

    private static byte[] Header() => [0x7F, 0xE3, 0x7F, 0xE3, 0x00, 0x4E];

    private static byte[] Event(int descriptorsLength)
        =>
        [
            0x00, 0x01,
            0xEF, 0x55, 0x22, 0x57, 0x00,
            0x00, 0x03, 0x00,
            (byte)((descriptorsLength >> 8) & 0x0F), (byte)(descriptorsLength & 0xFF),
        ];
}
