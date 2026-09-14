using System.IO.Pipelines;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

internal sealed class LiveHandedOverReading : ILiveHandedOver
{
    internal const int HeldForTheReader = 4 * 1024 * 1024;

    private readonly LiveReception reading;

    private readonly Stream written;

    private readonly LiveSeat seat;

    private bool letGo;

    internal LiveHandedOverReading(LiveReception reading)
    {
        Pipe holding = new(new PipeOptions(
            pauseWriterThreshold: HeldForTheReader,
            resumeWriterThreshold: HeldForTheReader / 2));

        this.reading = reading;
        written = holding.Writer.AsStream();
        seat = reading.Take(written);
        Bytes = holding.Reader.AsStream();
    }

    public Stream Bytes { get; }

    public async ValueTask DisposeAsync()
    {
        if (letGo)
        {
            return;
        }

        letGo = true;
        reading.Drop(seat);

        await written.DisposeAsync();
        await Bytes.DisposeAsync();

        reading.Detach();
    }
}
