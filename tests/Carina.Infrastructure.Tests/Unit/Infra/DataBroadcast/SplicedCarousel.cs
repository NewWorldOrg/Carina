using System.Text;

using Carina.BroadcastTestSupport;

namespace Carina.Infrastructure.Tests.Unit.Infra.DataBroadcast;

/// <summary>
/// A synthetic broadcast ffmpeg wrote, with a carousel of two modules and an event message put into it, as the
/// material tests of the data broadcast read it.
/// </summary>
internal static class SplicedCarousel
{
    private const int CarouselEvery = 40;

    public static readonly TimeSpan Length = TimeSpan.FromSeconds(4);

    private static readonly CarouselModule Startup = new(
        0x0000,
        1,
        CarouselBroadcast.Resource("startup.bml", EntityWriter.BmlType, Encoding.ASCII.GetBytes("<bml><body><p>CARINA</p></body></bml>")));

    private static readonly CarouselModule Logo = new(
        0x0001,
        1,
        CarouselBroadcast.Resource("logo.png", EntityWriter.PngType, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));

    public static async Task<byte[]> CarryingACarouselAsync(string room, TimeSpan startsAt)
    {
        string written = await (SyntheticBroadcast.AsMeasured() with
        {
            Length = Length,
            StartsAt = startsAt,
            WithCaptions = false,
            WithSuperimpose = false,
        }).WriteAsync(Path.Combine(room, "broadcast.m2ts"));

        byte[][] carousel =
        [
            new DiiWriter
            {
                Modules =
                [
                    DiiModule.Of(Startup.Id, Startup.Body.Length, Startup.Version),
                    DiiModule.Of(Logo.Id, Logo.Body.Length, Logo.Version),
                ],
            }.ToSection().ToBytes(),
            .. DsmCcWriter.Blocks(1, Startup.Id, Startup.Version, Startup.Body, DsmCcWriter.LargestBlock).Select(block => block.ToBytes()),
            .. DsmCcWriter.Blocks(1, Logo.Id, Logo.Version, Logo.Body, DsmCcWriter.LargestBlock).Select(block => block.ToBytes()),
        ];

        return CarouselSplice.Into(
            await File.ReadAllBytesAsync(written),
            SyntheticBroadcast.SomeProgramNumber,
            SyntheticBroadcast.PmtPid,
            round => [.. carousel, Fired(round)],
            CarouselEvery);
    }

    private static byte[] Fired(int round)
        => new StreamDescriptorWriter
        {
            DataEventId = CarouselBroadcast.DataEventId,
            EventMessageGroupId = 1,
            VersionNumber = round % 32,
            Descriptors = StreamDescriptorWriter.GeneralEvent(1, StreamDescriptorWriter.Immediate, 0, 1, round & 0xFFFF),
        }.ToSection().ToBytes();
}
