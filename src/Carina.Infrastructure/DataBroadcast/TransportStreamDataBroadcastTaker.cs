using System.Globalization;

using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// Takes the data broadcast out of a recording's file in one pass from its start to its end, reading the sections
/// of the service's data streams the way live viewing reads them, and gathers what changed into the record of it.
/// The service's clock, its wrap followed through, is moved by whole turns of the clock onto the clock the file's
/// captions are told on, and the record is kept within the size a record may take.
/// </summary>
public sealed class TransportStreamDataBroadcastTaker(
    IRecordingClockStart starts,
    CaptionSettings settings,
    TimeProvider clock) : IDataBroadcastTaker
{
    public const int Mouthful = 1 << 20;

    public async Task<DataBroadcastTaking> TakeAsync(string source, ServiceId service, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(service);

        using CancellationTokenSource deadline = new(settings.LongestTranscription, clock);
        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        Gathered gathered;

        try
        {
            gathered = await GatherAsync(source, service, waiting.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return DataBroadcastTaking.Failed(
                DataBroadcastFault.TimedOut,
                string.Create(CultureInfo.InvariantCulture, $"it was still reading after {settings.LongestTranscription}"));
        }
        catch (IOException unreadable)
        {
            return DataBroadcastTaking.Failed(DataBroadcastFault.Unreadable, unreadable.Message);
        }

        if (gathered.Record is not { Modules: > 0 } record)
        {
            return DataBroadcastTaking.Missing();
        }

        RecordingClockStartReading reading = await starts.ReadAsync(source, cancellationToken);

        if (reading.Start is not { } begins)
        {
            return DataBroadcastTaking.Failed(DataBroadcastFault.ClockUnread, reading.Note);
        }

        long startsAt = Ticks(begins);

        return DataBroadcastTaking.Taken(
            record.Shifted(Turns(startsAt - gathered.FirstHeard), startsAt).Within(DataBroadcastRecord.MostBytes));
    }

    /// <summary>
    /// The whole turns of the 33-bit clock nearest to <paramref name="apart"/>, as ticks.
    /// </summary>
    public static long Turns(long apart)
    {
        long turn = (long)LivePts.ComesAroundAt;

        return (long)Math.Round(apart / (double)turn, MidpointRounding.AwayFromZero) * turn;
    }

    private static async Task<Gathered> GatherAsync(string source, ServiceId service, CancellationToken cancellationToken)
    {
        CarouselReader reader = new(service);
        CarouselState state = new();
        DataBroadcastRecordBuilder builder = new();
        byte[] mouthful = new byte[Mouthful];
        long? firstHeard = null;

        await using FileStream reading = File.OpenRead(source);
        int read;

        while ((read = await reading.ReadAsync(mouthful, cancellationToken)) > 0)
        {
            foreach (CarouselSignalRead signal in reader.Read(mouthful.AsSpan(0, read)))
            {
                firstHeard ??= signal.At;
                Take(builder, state.Apply(signal.Signal, signal.At), signal.At);
            }

            firstHeard ??= reader.Now;
        }

        return firstHeard is { } from
            ? new Gathered(builder.Build(from, Math.Max(from, reader.Now ?? from)), from)
            : new Gathered(null, 0);
    }

    private static void Take(DataBroadcastRecordBuilder builder, IReadOnlyList<CarouselDelta> deltas, long at)
    {
        foreach (CarouselDelta delta in deltas)
        {
            builder.Take(delta, at);
        }
    }

    private static long Ticks(TimeSpan at)
        => (long)Math.Round((decimal)at.Ticks * StreamClock.Hertz / TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero);

    private sealed record Gathered(DataBroadcastRecord? Record, long FirstHeard);
}
