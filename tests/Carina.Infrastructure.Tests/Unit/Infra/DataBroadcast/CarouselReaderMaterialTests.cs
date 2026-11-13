using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;

namespace Carina.Infrastructure.Tests.Unit.Infra.DataBroadcast;

/// <summary>
/// Reads the data broadcast out of a synthetic broadcast ffmpeg wrote, with a carousel put into it, on the clock
/// ffmpeg stamped it with, across that clock coming around.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class CarouselReaderMaterialTests : IDisposable
{
    private const int Mouthful = 64 * 1024;

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private static readonly TimeSpan WhenTheClockComesAround =
        TimeSpan.FromTicks((long)(LivePts.ComesAroundAt * TimeSpan.TicksPerSecond / LivePts.Hertz));

    private readonly string room = Directory.CreateTempSubdirectory("carina-data-broadcast").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact(DisplayName = "BR-BD-004: the carousel of a broadcast ffmpeg wrote is read on the clock ffmpeg stamped, followed through its wrap")]
    public async Task TheCarouselIsReadOnTheClockFfmpegStampedFollowedThroughItsWrap()
    {
        TimeSpan startsAt = WhenTheClockComesAround - TimeSpan.FromSeconds(2);
        byte[] broadcast = await SplicedCarousel.CarryingACarouselAsync(room, startsAt);
        CarouselReader reader = new(Service);
        List<CarouselSignalRead> reads = [];

        for (int at = 0; at < broadcast.Length; at += Mouthful)
        {
            reads.AddRange(reader.Read(broadcast.AsSpan(at, Math.Min(Mouthful, broadcast.Length - at))));
        }

        long from = Ticks(startsAt - TimeSpan.FromSeconds(2));
        long until = Ticks(startsAt + SplicedCarousel.Length + TimeSpan.FromSeconds(2));

        Assert.IsType<CarouselSignal.Carried>(reads[0].Signal);
        Assert.Contains(reads, read => read.Signal is CarouselSignal.CatalogUpdated);
        Assert.Equal(
            [0, 1],
            reads.Select(read => read.Signal).OfType<CarouselSignal.ModuleCompleted>().Select(module => module.ModuleId).Distinct().Order());
        Assert.All(reads, read => Assert.InRange(read.At, from, until));
        Assert.Equal(reads.Select(read => read.At).Order(), reads.Select(read => read.At));
        EventMessage[] fired = [.. reads.Select(read => read.Signal).OfType<CarouselSignal.EventTimed>().Select(timed => timed.Message)];
        Assert.Contains(fired, message => message.FiresAt > (long)LivePts.ComesAroundAt);
        Assert.Contains(fired, message => message.FiresAt < (long)LivePts.ComesAroundAt);
        Assert.Equal(fired.Select(message => message.FiresAt).Order(), fired.Select(message => message.FiresAt));
        Assert.Equal(0, reader.RefusedChanges);
    }

    private static long Ticks(TimeSpan at) => (long)(at.Ticks * (Int128)StreamClock.Hertz / TimeSpan.TicksPerSecond);
}
