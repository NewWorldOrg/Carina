using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class CarouselSpliceTests
{
    private static readonly CarouselModule Startup = new(
        0x0000,
        1,
        CarouselBroadcast.Resource("startup.bml", EntityWriter.BmlType, Encoding.ASCII.GetBytes("<bml/>")));

    [Fact]
    public void ACarouselPutIntoAStreamWithoutOneIsReadAsItsDataBroadcastBesideWhatTheMapListedBefore()
    {
        CarouselBroadcast written = new CarouselBroadcast().Associated().Mapped(carrying: false);

        for (int second = 1; second <= 20; second++)
        {
            written.At(second * CarouselBroadcast.Second).Mapped(carrying: false);
        }

        byte[] spliced = CarouselSplice.Into(
            written.Bytes,
            CarouselBroadcast.ProgramNumber,
            CarouselBroadcast.MapPid,
            _ =>
            [
                new DiiWriter { Modules = [DiiModule.Of(Startup.Id, Startup.Body.Length, Startup.Version)] }.ToSection().ToBytes(),
                .. DsmCcWriter.Blocks(1, Startup.Id, Startup.Version, Startup.Body, DsmCcWriter.LargestBlock).Select(block => block.ToBytes()),
            ],
            every: 2);

        IReadOnlyList<DataBroadcastRead> reads = new DataBroadcastTap(CarouselBroadcast.ProgramNumber).Push(spliced);

        DataBroadcastService service = Assert.IsType<DataBroadcastRead.ServiceMapped>(reads[0]).Service;
        Assert.Equal(CarouselBroadcast.EntryTag, service.Entry?.ComponentTag);
        Assert.Equal(CarouselSplice.CarouselPid, service.Entry?.Pid);
        Assert.Contains(reads, read => read is DataBroadcastRead.CarouselChanged { Change: CarouselChange.ModuleCompleted });
    }
}
