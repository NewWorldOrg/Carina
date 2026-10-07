using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using Carina.Domain.Base;
using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Segments;
using Carina.Infrastructure.Captions;
using Carina.Infrastructure.Machines;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// Imports the learning data of one recording from a reduced copy of it.
/// </summary>
public interface IReducedCopyImporter
{
    /// <summary>
    /// Reads the copy from its head to its end, keeping the data as it is made and, when the copy carries
    /// captions, whether captions are shown in each second, and settles the record of taking the data out,
    /// which is reading with <see cref="ExtractionVersion.CurrentFromReducedCopy"/>. An import the caller
    /// stops leaves the record where it stood.
    /// </summary>
    Task ImportAsync(ReducedCopy copy, CancellationToken cancellationToken);
}

/// <summary>
/// Imports a reduced copy with one ffmpeg started yielding, which reads the copy's files itself and hands back
/// what <see cref="FfmpegReducedCopyInvocation"/> names. What it hands back is read by a
/// <see cref="MatroskaReader"/> and placed by a <see cref="LearningDataTimeline"/> on the recording's own
/// time, <see cref="ReducedCopy.StartsAt"/> after the copy's, each picture of the corners spread back by
/// <see cref="CornerTiles"/> first, and every chunk is kept as soon as it is whole. The
/// record ends done when the copy was read to its end, partial when what ffmpeg handed back stopped inside a
/// block, and failed when ffmpeg is missing, did not find the copy's picture or sound, or broke off. Captions
/// the copy carries are placed the way the captions taken from a recording are, and kept before the record is
/// settled; captions that cannot be read are left out and said so. Only the tail of what ffmpeg says on its
/// error stream is kept, as the reason for a failure. An import whose record something else has changed
/// stops and leaves it as it is. Nothing in the copy is written.
/// </summary>
public sealed class FfmpegReducedCopyImporter(
    MachineSettings machine,
    LearningRecords records,
    TimeProvider clock,
    ILogger<FfmpegReducedCopyImporter> logger) : IReducedCopyImporter
{
    public const string NoSuchStream = "matches no streams";

    public const int ComplaintKept = 4096;

    public const long LargestCaptions = 256L << 20;

    private static readonly ExtractionVersion Reduced = ExtractionVersion.CurrentFromReducedCopy;

    public async Task ImportAsync(ReducedCopy copy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(copy);

        Handover sink = new(copy.StartsAt);

        try
        {
            await RunAsync(copy, sink, cancellationToken);
        }
        catch (TakenOverException)
        {
            logger.LogWarning(
                "The record of recording {Recording} was changed by something else while its reduced copy was imported, so the import stops and leaves it as it is.",
                copy.Id.Wire);
        }
        catch (Exception broken) when (broken is not OperationCanceledException)
        {
            logger.LogError(broken, "Importing recording {Recording} from its reduced copy broke off.", copy.Id.Wire);

            await FailAsync(copy, new ExtractionFailureDetail(ExtractionFailure.Other, $"the import broke off: {broken.Message}"), cancellationToken);
        }
    }

    private async Task RunAsync(ReducedCopy copy, Handover sink, CancellationToken cancellationToken)
    {
        ProgrammeStart start = AnotherProgramme.Start(machine.Programme, FfmpegReducedCopyInvocation.Arguments(copy), ProgrammePriority.Yielding);

        if (start.Process is not { } running)
        {
            await FailAsync(copy, new ExtractionFailureDetail(ExtractionFailure.FfmpegMissing, start.Complained), cancellationToken);

            return;
        }

        using Process decoder = running;

        logger.LogInformation("Recording {Recording} is imported from its reduced copy in {Directory}.", copy.Id.Wire, copy.Directory);

        await SettleAsync(copy, sink, await DecodedAsync(copy, sink, decoder, cancellationToken), cancellationToken);
    }

    private async Task<Decoded> DecodedAsync(ReducedCopy copy, Handover sink, Process running, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration stopping = cancellationToken.UnsafeRegister(_ => AnotherProgramme.GiveUpOn(running), null);
        Task<string> complaining = TailAsync(running.StandardError);
        MatroskaReader reader = new(sink);

        try
        {
            bool whole = await reader.ReadToEndAsync(running.StandardOutput.BaseStream, token => KeepAsync(copy, sink, token), cancellationToken);

            return await EndedAsync(running, complaining, whole, null, cancellationToken);
        }
        catch (Exception refused) when (refused is InvalidDataException or LearningDataUnplaceableException)
        {
            AnotherProgramme.GiveUpOn(running);

            return await EndedAsync(running, complaining, false, refused, cancellationToken);
        }
        catch
        {
            AnotherProgramme.GiveUpOn(running);
            await ((Task)complaining).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            throw;
        }
    }

    private static async Task<Decoded> EndedAsync(
        Process running,
        Task<string> complaining,
        bool whole,
        Exception? refused,
        CancellationToken cancellationToken)
    {
        await ((Task)complaining).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await running.WaitForExitAsync(CancellationToken.None);

        cancellationToken.ThrowIfCancellationRequested();

        return new Decoded(
            whole,
            refused,
            running.ExitCode,
            ProgrammeNote.Of(complaining.IsCompletedSuccessfully ? complaining.Result : string.Empty, ProgrammeNote.Longest));
    }

    private static async Task<string> TailAsync(StreamReader complaints)
    {
        StringBuilder tail = new();

        while (await complaints.ReadLineAsync(CancellationToken.None) is { } line)
        {
            tail.AppendLine(line);

            if (tail.Length > 2 * ComplaintKept)
            {
                tail.Remove(0, tail.Length - ComplaintKept);
            }
        }

        return tail.Length > ComplaintKept ? tail.ToString(tail.Length - ComplaintKept, ComplaintKept) : tail.ToString();
    }

    private async Task KeepAsync(ReducedCopy copy, Handover sink, CancellationToken cancellationToken)
    {
        if (sink.Ready.Count is 0)
        {
            return;
        }

        LearningDataChunk[] chunks = [.. sink.Ready];
        sink.Ready.Clear();

        await records.KeepAsync(copy.Id, chunks, Reduced, Now(), cancellationToken);
    }

    private async Task SettleAsync(ReducedCopy copy, Handover sink, Decoded decoded, CancellationToken cancellationToken)
    {
        if (Failure(decoded) is { } failure)
        {
            await FailAsync(copy, failure, cancellationToken);

            return;
        }

        LearningDataTimeline? timeline = sink.Timeline;
        IReadOnlyList<LearningDataChunk> rest;

        try
        {
            rest = timeline is null
                ? throw new LearningDataUnplaceableException(ExtractionFailure.StreamMissing, "ffmpeg handed back no tracks.")
                : timeline.Finish();
        }
        catch (LearningDataUnplaceableException unplaced) when (decoded.Whole)
        {
            await FailAsync(copy, new ExtractionFailureDetail(unplaced.Failure, unplaced.Message), cancellationToken);

            return;
        }
        catch (LearningDataUnplaceableException)
        {
            rest = [];
        }

        DateTime at = Now();
        TimeSpan through = timeline?.PlacedThrough ?? TimeSpan.Zero;

        if (rest.Count > 0)
        {
            await records.KeepAsync(copy.Id, rest, Reduced, at, cancellationToken);
        }

        await CaptionAsync(copy, through, at, cancellationToken);
        await ChangeAsync(
            copy,
            record =>
            {
                Placed(record, timeline, through, at);
                Settled(record, decoded.Whole, at);
            },
            cancellationToken);

        Told(copy, decoded.Whole, through);
    }

    private static void Placed(LearningExtraction record, LearningDataTimeline? timeline, TimeSpan through, DateTime at)
    {
        if (timeline?.Sound is { } sound)
        {
            record.Opened(sound, at);
        }

        foreach (LearningDataGap gap in timeline?.RemainingGaps() ?? [])
        {
            record.Missed(gap, at);
        }

        record.Reached(through, at);
    }

    private static void Settled(LearningExtraction record, bool whole, DateTime at)
    {
        if (whole)
        {
            record.Finish(at);

            return;
        }

        record.FinishPartway(at);
    }

    private async Task CaptionAsync(ReducedCopy copy, TimeSpan through, DateTime at, CancellationToken cancellationToken)
    {
        if (copy.Captions is not { } kept)
        {
            return;
        }

        if (await ReadCaptionsAsync(kept, cancellationToken) is not { } captions)
        {
            logger.LogWarning(
                "The captions in the reduced copy of recording {Recording} could not be read, so whether captions are shown is left out.",
                copy.Id.Wire);

            return;
        }

        await records.KeepPartsAsync(
            copy.Id,
            [.. CaptionPresence.Parts(captions, through).OrderByDescending(part => part.Index)],
            Reduced,
            at,
            cancellationToken);
    }

    private static async Task<CaptionRecord?> ReadCaptionsAsync(string kept, CancellationToken cancellationToken)
    {
        try
        {
            return new FileInfo(kept).Length > LargestCaptions
                ? null
                : CaptionRecordFormat.Read(await File.ReadAllBytesAsync(kept, cancellationToken));
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static ExtractionFailureDetail? Failure(Decoded decoded)
    {
        if (decoded.Refused is LearningDataUnplaceableException unplaced)
        {
            return new ExtractionFailureDetail(unplaced.Failure, unplaced.Message);
        }

        if (decoded.Refused is { } refused)
        {
            return new ExtractionFailureDetail(ExtractionFailure.Other, $"ffmpeg handed back what could not be read: {refused.Message}");
        }

        if (decoded.ExitCode is 0)
        {
            return null;
        }

        return decoded.Complained.Contains(NoSuchStream, StringComparison.Ordinal)
            ? new ExtractionFailureDetail(ExtractionFailure.StreamMissing, decoded.Complained)
            : new ExtractionFailureDetail(ExtractionFailure.Other, $"ffmpeg exited {decoded.ExitCode}: {decoded.Complained}");
    }

    private async Task FailAsync(ReducedCopy copy, ExtractionFailureDetail failure, CancellationToken cancellationToken)
    {
        await ChangeAsync(copy, record => record.Fail(failure.Failure, failure.Reason, Now()), cancellationToken);

        logger.LogWarning(
            "The learning data of recording {Recording} could not be imported from its reduced copy in {Directory} ({Failure}): {Reason}",
            copy.Id.Wire,
            copy.Directory,
            failure.Failure,
            failure.Reason);
    }

    private async Task ChangeAsync(ReducedCopy copy, Action<LearningExtraction> settle, CancellationToken cancellationToken)
    {
        ExtractionChange change = await records.ChangeAsync(
            copy.Id,
            record =>
            {
                if (!Owns(record))
                {
                    return false;
                }

                settle(record);

                return true;
            },
            cancellationToken);

        if (change is not ExtractionChange.Written)
        {
            throw new TakenOverException();
        }
    }

    private static bool Owns(LearningExtraction record)
        => record.State is LearningExtractionState.Reading
           && Equals(record.Version, Reduced)
           && record.ReadThrough == TimeSpan.Zero
           && record.Gaps.Count is 0;

    private void Told(ReducedCopy copy, bool whole, TimeSpan through)
    {
        if (whole)
        {
            logger.LogInformation(
                "The learning data of recording {Recording} was imported from its reduced copy in {Directory}, {Through} of it.",
                copy.Id.Wire,
                copy.Directory,
                through);

            return;
        }

        logger.LogWarning(
            "The learning data of recording {Recording} was imported from its reduced copy in {Directory} only partway, {Through} of it, because ffmpeg stopped before the end.",
            copy.Id.Wire,
            copy.Directory,
            through);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private sealed record Decoded(bool Whole, Exception? Refused, int ExitCode, string Complained);

    private sealed class TakenOverException : Exception
    {
    }

    private sealed class Handover(TimeSpan startsAt) : IMatroskaSink
    {
        public const string RawPictures = "V_UNCOMPRESSED";

        public const string RawSound = "A_PCM/INT/LIT";

        private readonly byte[] spread = new byte[WatermarkFrame.Pixels];

        private int frames = -1;

        private int tiles = -1;

        private int sound = -1;

        public LearningDataTimeline? Timeline { get; private set; }

        public List<LearningDataChunk> Ready { get; } = [];

        public void Tracks(IReadOnlyList<MatroskaTrack> tracks)
        {
            MatroskaTrack framed = Picture(tracks, FrameLight.Width, FrameLight.Height);

            frames = framed.Number;
            tiles = Picture(tracks, CornerTiles.Width, CornerTiles.Height).Number;
            sound = Sound(tracks).Number;
            Timeline = new LearningDataTimeline(framed.FrameLength);
        }

        public void Block(int track, TimeSpan at, ReadOnlySpan<byte> payload)
        {
            LearningDataTimeline timeline = Timeline
                ?? throw new LearningDataUnplaceableException(ExtractionFailure.Other, "A block came before the tracks were described.");
            TimeSpan onTheRecording = startsAt + at;

            if (track == frames)
            {
                Ready.AddRange(timeline.See(onTheRecording, payload));
            }
            else if (track == tiles)
            {
                Ready.AddRange(timeline.Glimpse(onTheRecording, Spread(payload)));
            }
            else if (track == sound)
            {
                Ready.AddRange(timeline.Hear(onTheRecording, Samples(payload)));
            }
        }

        private ReadOnlySpan<byte> Spread(ReadOnlySpan<byte> payload)
        {
            if (payload.Length != CornerTiles.Pixels)
            {
                throw new LearningDataUnplaceableException(
                    ExtractionFailure.Other,
                    $"A picture of the tiles of the corners is {CornerTiles.Pixels} bytes, and this one is {payload.Length}.");
            }

            CornerTiles.Spread(payload, spread);

            return spread;
        }

        private static ReadOnlySpan<short> Samples(ReadOnlySpan<byte> payload)
            => BitConverter.IsLittleEndian && payload.Length % sizeof(short) is 0
                ? MemoryMarshal.Cast<byte, short>(payload)
                : throw new LearningDataUnplaceableException(ExtractionFailure.Other, "Sound is read as whole little-endian 16-bit samples.");

        private static MatroskaTrack Picture(IReadOnlyList<MatroskaTrack> tracks, int width, int height)
            => tracks.FirstOrDefault(track => track is { Kind: MatroskaTrackKind.Video, Codec: RawPictures } && track.Width == width && track.Height == height)
               ?? throw new LearningDataUnplaceableException(ExtractionFailure.Other, $"ffmpeg handed back no grey picture {width} by {height}.");

        private static MatroskaTrack Sound(IReadOnlyList<MatroskaTrack> tracks)
            => tracks.FirstOrDefault(track => track is { Kind: MatroskaTrackKind.Audio, Codec: RawSound, Channels: 2, BitDepth: 16 }
                                              && track.SampleRate == SoundReader.SampleRate)
               ?? throw new LearningDataUnplaceableException(ExtractionFailure.Other, "ffmpeg handed back no sound of two 16-bit channels at the rate asked for.");
    }
}
