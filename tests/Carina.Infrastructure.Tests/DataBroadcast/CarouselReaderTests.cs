using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Infrastructure.DataBroadcast;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class CarouselReaderTests
{
    private const long Second = CarouselBroadcast.Second;

    private static readonly ServiceId Service = new(CarouselBroadcast.ProgramNumber);

    private static readonly CarouselModule Startup = new(
        0x0000,
        1,
        CarouselBroadcast.Resource("startup.bml", EntityWriter.BmlType, Encoding.ASCII.GetBytes("<bml/>")));

    [Fact(DisplayName = "BR-BD-001: a service carrying a data broadcast is read as carried, with its entry and whether it opens by itself")]
    public void AServiceCarryingADataBroadcastIsReadAsCarried()
    {
        CarouselReader reader = new(Service);

        CarouselSignalRead read = Assert.Single(reader.Read(new CarouselBroadcast().Associated().Mapped(autoStart: true).At(Second).Bytes));

        CarouselSignal.Carried carried = Assert.IsType<CarouselSignal.Carried>(read.Signal);
        Assert.Equal(new DataBroadcastEntry(Service, CarouselBroadcast.EntryTag, true), carried.Entry);
        Assert.Equal([CarouselBroadcast.EntryTag], carried.Tags);
        Assert.Equal(Second, read.At);
    }

    [Fact(DisplayName = "BR-BS-002: a service carrying more than one data stream is read as carried with every stream's tag")]
    public void AServiceCarryingMoreThanOneDataStreamIsReadWithEveryTag()
    {
        CarouselReader reader = new(Service);

        CarouselSignalRead read = Assert.Single(reader.Read(new CarouselBroadcast().Associated().Mapped(withAnotherCarousel: true).At(Second).Bytes));

        Assert.Equal([CarouselBroadcast.EntryTag, CarouselBroadcast.OtherTag], Assert.IsType<CarouselSignal.Carried>(read.Signal).Tags);
    }

    [Fact(DisplayName = "BR-BD-004: a reference of the programme time on a stream without a data component descriptor is taken by the state under its tag")]
    public void AReferenceOnAStreamWithoutADataComponentIsTakenByTheStateUnderItsTag()
    {
        CarouselReader reader = new(Service);
        CarouselState state = new();

        IReadOnlyList<CarouselSignalRead> reads = reader.Read(new CarouselBroadcast()
            .Associated()
            .Mapped(eventStreamTag: CarouselBroadcast.EventStreamTag)
            .At(Second)
            .Referred(CarouselBroadcast.EventStreamPid, stc: 2 * Second, npt: 10 * Second)
            .Bytes);

        Assert.Equal([CarouselBroadcast.EntryTag, CarouselBroadcast.EventStreamTag], Assert.IsType<CarouselSignal.Carried>(reads[0].Signal).Tags);
        List<CarouselDelta> deltas = [.. reads.SelectMany(read => state.Apply(read.Signal, read.At))];
        CarouselDelta.NptReferred referred = Assert.Single(deltas.OfType<CarouselDelta.NptReferred>());
        Assert.Equal((CarouselBroadcast.EventStreamTag, 10 * Second), (referred.Reference.Tag, referred.Reference.Npt));
        Assert.Equal([CarouselBroadcast.EventStreamTag], state.NptReferences.Select(reference => reference.Tag));
    }

    [Fact(DisplayName = "BR-BD-004: a service with no data stream is read as carrying none")]
    public void AServiceWithNoDataStreamIsReadAsCarryingNone()
    {
        CarouselReader reader = new(Service);

        CarouselSignalRead read = Assert.Single(reader.Read(new CarouselBroadcast().Associated().Mapped(carrying: false).At(Second).Bytes));

        Assert.IsType<CarouselSignal.NotCarried>(read.Signal);
    }

    [Fact(DisplayName = "BR-BS-002: the download info is read as the modules it lists, and a module put together as its resources")]
    public void TheDownloadInfoAndAModuleAreReadAsTheStateTakesThem()
    {
        CarouselModule logo = new(0x0001, 4, CarouselBroadcast.Resource("logo.png", EntityWriter.PngType, [0x89, 0x50]));
        CarouselReader reader = new(Service);

        IReadOnlyList<CarouselSignalRead> reads = reader.Read(new CarouselBroadcast()
            .Associated()
            .Mapped()
            .At(Second)
            .Listed(1, Startup, logo)
            .Delivered(logo)
            .Bytes);

        CarouselSignal.CatalogUpdated listed = Assert.IsType<CarouselSignal.CatalogUpdated>(reads[1].Signal);
        Assert.Equal(CarouselBroadcast.EntryTag, listed.Tag);
        Assert.Equal(
            [(0, 1, (long)Startup.Body.Length), (1, 4, (long)logo.Body.Length)],
            listed.Modules.Select(module => (module.Id, module.Version, module.Size)));
        CarouselSignal.ModuleCompleted completed = Assert.IsType<CarouselSignal.ModuleCompleted>(reads[2].Signal);
        Assert.Equal((CarouselBroadcast.EntryTag, 1, 4), (completed.Tag, completed.ModuleId, completed.Version));
        CarouselResource resource = Assert.Single(completed.Resources);
        Assert.Equal(("logo.png", "image/X-arib-png", ResourceForm.Binary), (resource.Path, resource.MediaType, resource.Form));
        Assert.Equal([0x89, 0x50], resource.Body.ToArray());
    }

    [Fact(DisplayName = "BR-BD-002: a resource with no name the documents could find it by is left out and counted")]
    public void AResourceWithNoNameIsLeftOutAndCounted()
    {
        CarouselModule nameless = new(0x0002, 1, Encoding.ASCII.GetBytes("Content-Type: image/jpeg\r\n\r\nÿØ"));
        CarouselReader reader = new(Service);

        IReadOnlyList<CarouselSignalRead> reads = reader.Read(new CarouselBroadcast()
            .Associated()
            .Mapped()
            .At(Second)
            .Listed(1, nameless)
            .Delivered(nameless)
            .Bytes);

        Assert.DoesNotContain(reads, read => read.Signal is CarouselSignal.ModuleCompleted);
        Assert.Equal(1, reader.ResourcesLeftOut);
        Assert.Equal(1, reader.RefusedChanges);
    }

    [Fact(DisplayName = "BR-BD-003: an event message is read with the moment it fires on the service's clock")]
    public void AnEventMessageIsReadWithTheMomentItFires()
    {
        CarouselReader reader = new(Service);
        reader.Read(new CarouselBroadcast().Associated().Mapped().At(Second).Bytes);

        CarouselSignalRead read = Assert.Single(reader.Read(new CarouselBroadcast().At(3 * Second).Fired(0, 0x12, 0x0345, 0x06, 0xEE).Bytes));

        EventMessage message = Assert.IsType<CarouselSignal.EventTimed>(read.Signal).Message;
        Assert.Equal((0x12, 0x0345, 0x06, EventTiming.Immediate), (message.Group, message.Id, message.MessageType, message.Timing));
        Assert.Equal(3 * Second, message.FiresAt);
        Assert.Equal([0xEE], message.PrivateData.ToArray());
        Assert.Equal(CarouselBroadcast.EntryTag, message.ComponentTag);
        Assert.Equal(3 * Second, read.At);
    }

    [Fact(DisplayName = "BR-BD-004: an event message on a stream that carries only event messages is read with that stream's tag")]
    public void AnEventOnAStreamOfEventsOnlyIsReadWithThatStreamsTag()
    {
        CarouselReader reader = new(Service);
        reader.Read(new CarouselBroadcast().Associated().Mapped(eventStreamTag: CarouselBroadcast.EventStreamTag).At(Second).Bytes);

        CarouselSignalRead read = Assert.Single(reader.Read(new CarouselBroadcast()
            .At(3 * Second)
            .FiredOn(CarouselBroadcast.EventStreamPid, 0, 0x12, 0x0345, 0x06, 0xEE)
            .Bytes));

        Assert.Equal(CarouselBroadcast.EventStreamTag, Assert.IsType<CarouselSignal.EventTimed>(read.Signal).Message.ComponentTag);
    }

    [Fact(DisplayName = "BR-BD-003: an event message timed by the programme time is read at the moment the reference pairs it with, after the reference")]
    public void AnEventTimedByTheProgrammeTimeIsReadAtTheMomentTheReferencePairsItWith()
    {
        CarouselReader reader = new(Service);
        reader.Read(new CarouselBroadcast().Associated().Mapped().At(Second).Bytes);

        IReadOnlyList<CarouselSignalRead> reads = reader.Read(new CarouselBroadcast()
            .At(3 * Second)
            .Sections(CarouselBroadcast.CarouselPid, new StreamDescriptorWriter
            {
                DataEventId = CarouselBroadcast.DataEventId,
                Descriptors = DescriptorWriter.Loop(
                    StreamDescriptorWriter.NptReference(stc: 4 * Second, npt: 10 * Second),
                    StreamDescriptorWriter.GeneralEvent(0x12, StreamDescriptorWriter.Npt, 12 * Second, 0x06, 0x0345)),
            }.ToSection().ToBytes())
            .Bytes);

        Assert.Equal(2, reads.Count);
        CarouselSignal.NptReferred referred = Assert.IsType<CarouselSignal.NptReferred>(reads[0].Signal);
        Assert.Equal(
            (CarouselBroadcast.EntryTag, 4 * Second, 10 * Second, 1, 1),
            (referred.Reference.Tag, referred.Reference.Stc, referred.Reference.Npt, referred.Reference.ScaleNumerator, referred.Reference.ScaleDenominator));
        EventMessage message = Assert.IsType<CarouselSignal.EventTimed>(reads[1].Signal).Message;
        Assert.Equal(EventTiming.Npt, message.Timing);
        Assert.Equal(6 * Second, message.FiresAt);
    }

    [Fact(DisplayName = "BR-BV-002: a carousel past the limit is read as left out for that reason")]
    public void ACarouselPastTheLimitIsReadAsLeftOut()
    {
        CarouselReader reader = new(Service, new CarouselLimits(mostCarousels: 16, mostModules: 1, largestModule: 1 << 20, largestTotal: 1 << 22, mostParts: 8));

        IReadOnlyList<CarouselSignalRead> reads = reader.Read(new CarouselBroadcast()
            .Associated()
            .Mapped()
            .At(Second)
            .Listed(1, Startup, Startup with { Id = 1 })
            .Bytes);

        CarouselSignal.Dropped dropped = Assert.IsType<CarouselSignal.Dropped>(reads[1].Signal);
        Assert.Equal((CarouselBroadcast.EntryTag, CarouselDropReason.TooManyModules), (dropped.Tag, dropped.Reason));
    }
}
