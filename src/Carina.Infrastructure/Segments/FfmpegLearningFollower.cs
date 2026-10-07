using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Segments;
using Carina.Infrastructure.Machines;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// Follows a recording with one ffmpeg started yielding: the file is read once, from its head, and
/// handed to ffmpeg on its standard input; at the end of a file still being written the follow waits
/// <see cref="LearningFollowSettings.WhileCaughtUp"/> and reads on, and once the recording has ended
/// and the file is read to its end the standard input is closed. What ffmpeg hands back
/// (<see cref="FfmpegLearningInvocation"/>) is read by a <see cref="MatroskaReader"/> and placed by a
/// <see cref="LearningDataTimeline"/>, every chunk is kept as soon as it is whole, and the record goes
/// as far as the chunk with the gaps no later block can reach. The record ends done when the
/// recording was read to its end, partial when the recording went or its file could no longer be
/// read, and failed when ffmpeg is missing, did not find the programme's picture or sound, or broke
/// off. Only the tail of what ffmpeg says on its error stream is kept, as the reason for a failure.
/// A follow whose record something else has changed stops and leaves it as it is.
/// </summary>
public sealed class FfmpegLearningFollower(
    MachineSettings machine,
    LearningFollowSettings settings,
    LearningRecords records,
    TimeProvider clock,
    ILogger<FfmpegLearningFollower> logger) : ILearningFollower
{
    public const string NoSuchStream = "matches no streams";

    public const int ComplaintKept = 4096;

    private enum FeedEnd
    {
        ReadToTheEnd = 1,

        Gone = 2,

        Unreadable = 3,

        Abandoned = 4,
    }

    public async Task FollowAsync(FollowedRecording recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        Follow follow = new(recording);

        try
        {
            await RunAsync(follow, cancellationToken);
        }
        catch (TakenOverException)
        {
            logger.LogWarning(
                "The record of recording {Recording} was changed by something else while it was followed, so the follow stops and leaves it as it is.",
                recording.Id.Wire);
        }
        catch (Exception broken) when (broken is not OperationCanceledException)
        {
            logger.LogError(broken, "Following recording {Recording} for its learning data broke off.", recording.Id.Wire);

            await FailAsync(follow, new ExtractionFailureDetail(ExtractionFailure.Other, $"the follow broke off: {broken.Message}"), cancellationToken);
        }
    }

    private async Task RunAsync(Follow follow, CancellationToken cancellationToken)
    {
        FileStream? opened = Opened(follow.Recording.Source);

        if (opened is null)
        {
            await SettleAsync(follow, new Decoded(false, null, FeedEnd.Unreadable, 0, string.Empty), cancellationToken);

            return;
        }

        await using FileStream file = opened;
        ProgrammeStart start = AnotherProgramme.StartFed(
            machine.Programme,
            FfmpegLearningInvocation.Arguments(follow.Recording.Service),
            ProgrammePriority.Yielding);

        if (start.Process is not { } running)
        {
            await FailAsync(follow, new ExtractionFailureDetail(ExtractionFailure.FfmpegMissing, start.Complained), cancellationToken);

            return;
        }

        using Process decoder = running;

        logger.LogInformation("Recording {Recording} is followed for its learning data.", follow.Recording.Id.Wire);

        await SettleAsync(follow, await DecodedAsync(follow, file, decoder, cancellationToken), cancellationToken);
    }

    private static FileStream? Opened(string source)
    {
        try
        {
            return File.OpenRead(source);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<Decoded> DecodedAsync(Follow follow, FileStream file, Process running, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration stopping = cancellationToken.UnsafeRegister(_ => AnotherProgramme.GiveUpOn(running), null);
        using CancellationTokenSource feedingStops = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<string> complaining = TailAsync(running.StandardError);
        Task<FeedEnd> feeding = FeedAsync(follow.Recording, file, running.StandardInput.BaseStream, feedingStops.Token);
        MatroskaReader reader = new(follow.Sink);

        try
        {
            bool whole = await reader.ReadToEndAsync(running.StandardOutput.BaseStream, token => KeepAsync(follow, token), cancellationToken);

            return await EndedAsync(running, feeding, feedingStops, complaining, whole, null, cancellationToken);
        }
        catch (Exception refused) when (refused is InvalidDataException or LearningDataUnplaceableException)
        {
            AnotherProgramme.GiveUpOn(running);

            return await EndedAsync(running, feeding, feedingStops, complaining, false, refused, cancellationToken);
        }
        catch
        {
            AnotherProgramme.GiveUpOn(running);
            await feedingStops.CancelAsync();
            await Task.WhenAll(feeding, complaining).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            throw;
        }
    }

    private static async Task<Decoded> EndedAsync(
        Process running,
        Task<FeedEnd> feeding,
        CancellationTokenSource feedingStops,
        Task<string> complaining,
        bool whole,
        Exception? refused,
        CancellationToken cancellationToken)
    {
        await feedingStops.CancelAsync();
        await Task.WhenAll(feeding, complaining).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await running.WaitForExitAsync(CancellationToken.None);

        cancellationToken.ThrowIfCancellationRequested();

        return new Decoded(
            whole,
            refused,
            feeding.IsCompletedSuccessfully ? feeding.Result : FeedEnd.Abandoned,
            running.ExitCode,
            ProgrammeNote.Of(complaining.IsCompletedSuccessfully ? complaining.Result : string.Empty, ProgrammeNote.Longest));
    }

    private async Task<FeedEnd> FeedAsync(FollowedRecording recording, FileStream file, Stream input, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[settings.ReadBytes];
        bool lastLook = false;

        try
        {
            while (!recording.HasGone)
            {
                int read = await ReadAsync(file, buffer, cancellationToken);

                if (read < 0)
                {
                    return FeedEnd.Unreadable;
                }

                if (read > 0 && !await WrittenAsync(input, buffer.AsMemory(0, read), cancellationToken))
                {
                    return FeedEnd.Abandoned;
                }

                if (read is 0 && lastLook)
                {
                    return FeedEnd.ReadToTheEnd;
                }

                lastLook = lastLook || (read is 0 && recording.HasEnded);

                if (read is 0 && !lastLook)
                {
                    await Task.Delay(settings.WhileCaughtUp, clock, cancellationToken);
                }
            }

            return FeedEnd.Gone;
        }
        finally
        {
            Closed(input);
        }
    }

    private static async Task<int> ReadAsync(FileStream file, byte[] buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await file.ReadAsync(buffer, cancellationToken);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private static async Task<bool> WrittenAsync(Stream input, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        try
        {
            await input.WriteAsync(bytes, cancellationToken);
            await input.FlushAsync(cancellationToken);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void Closed(Stream input)
    {
        try
        {
            input.Dispose();
        }
        catch (IOException)
        {
            return;
        }
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

    private async Task KeepAsync(Follow follow, CancellationToken cancellationToken)
    {
        if (follow.Sink.Ready.Count is 0 || follow.Sink.Timeline is not { } timeline)
        {
            return;
        }

        LearningDataChunk[] chunks = [.. follow.Sink.Ready];
        follow.Sink.Ready.Clear();

        await records.KeepAsync(follow.Recording.Id, chunks, follow.Recording.Version, Now(), cancellationToken);
        await ProgressAsync(follow, LearningData.ChunkStarts(chunks[^1].Index + 1), timeline.SettledGaps(), null, cancellationToken);
    }

    private async Task SettleAsync(Follow follow, Decoded decoded, CancellationToken cancellationToken)
    {
        if (Failure(decoded) is { } failure)
        {
            await FailAsync(follow, failure, cancellationToken);

            return;
        }

        bool whole = decoded.Whole && decoded.Fed is FeedEnd.ReadToTheEnd;
        LearningDataTimeline? timeline = follow.Sink.Timeline;
        IReadOnlyList<LearningDataChunk> rest;

        try
        {
            rest = timeline is null
                ? throw new LearningDataUnplaceableException(ExtractionFailure.StreamMissing, "ffmpeg handed back no tracks.")
                : timeline.Finish();
        }
        catch (LearningDataUnplaceableException unplaced) when (whole)
        {
            await FailAsync(follow, new ExtractionFailureDetail(unplaced.Failure, unplaced.Message), cancellationToken);

            return;
        }
        catch (LearningDataUnplaceableException)
        {
            rest = [];
        }

        if (rest.Count > 0)
        {
            await records.KeepAsync(follow.Recording.Id, rest, follow.Recording.Version, Now(), cancellationToken);
        }

        await ProgressAsync(
            follow,
            timeline?.PlacedThrough ?? TimeSpan.Zero,
            timeline?.RemainingGaps() ?? [],
            whole ? (record, at) => record.Finish(at) : (record, at) => record.FinishPartway(at),
            cancellationToken);

        Told(follow, decoded, whole);
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

    private async Task FailAsync(Follow follow, ExtractionFailureDetail failure, CancellationToken cancellationToken)
    {
        await ProgressAsync(follow, TimeSpan.Zero, [], (record, at) => record.Fail(failure.Failure, failure.Reason, at), cancellationToken);

        logger.LogWarning(
            "The learning data of recording {Recording} could not be taken while it was recorded ({Failure}): {Reason}",
            follow.Recording.Id.Wire,
            failure.Failure,
            failure.Reason);
    }

    private async Task ProgressAsync(
        Follow follow,
        TimeSpan through,
        IReadOnlyList<LearningDataGap> gaps,
        Action<LearningExtraction, DateTime>? settle,
        CancellationToken cancellationToken)
    {
        ExtractionSound? sound = follow.Sink.Timeline?.Sound;
        DateTime at = Now();

        ExtractionChange change = await records.ChangeAsync(
            follow.Recording.Id,
            record => follow.Progress(record, sound, gaps, through, settle, at),
            cancellationToken);

        if (change is not ExtractionChange.Written)
        {
            throw new TakenOverException();
        }

        follow.Wrote(through, gaps.Count);
    }

    private void Told(Follow follow, Decoded decoded, bool whole)
    {
        if (whole)
        {
            logger.LogInformation(
                "The learning data of recording {Recording} was taken to its end, {Through} of it.",
                follow.Recording.Id.Wire,
                follow.Through);

            return;
        }

        logger.LogWarning(
            "The learning data of recording {Recording} was taken only partway, {Through} of it, because {Why}.",
            follow.Recording.Id.Wire,
            follow.Through,
            decoded.Fed switch
            {
                FeedEnd.Gone => "the recording went",
                FeedEnd.Unreadable => "its file could no longer be read",
                _ => "ffmpeg stopped before the end",
            });
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private sealed record Decoded(bool Whole, Exception? Refused, FeedEnd Fed, int ExitCode, string Complained);

    private sealed class TakenOverException : Exception
    {
    }

    private sealed class Follow(FollowedRecording recording)
    {
        private int gaps;

        public FollowedRecording Recording { get; } = recording;

        public Handover Sink { get; } = new();

        public TimeSpan Through { get; private set; }

        public bool Progress(
            LearningExtraction record,
            ExtractionSound? sound,
            IReadOnlyList<LearningDataGap> found,
            TimeSpan reached,
            Action<LearningExtraction, DateTime>? settle,
            DateTime at)
        {
            if (!Owns(record))
            {
                return false;
            }

            if (record.Sound is null && sound is not null)
            {
                record.Opened(sound, at);
            }

            foreach (LearningDataGap gap in found)
            {
                record.Missed(gap, at);
            }

            if (reached > record.ReadThrough)
            {
                record.Reached(reached, at);
            }

            settle?.Invoke(record, at);

            return true;
        }

        public void Wrote(TimeSpan reached, int found)
        {
            Through = reached > Through ? reached : Through;
            gaps += found;
        }

        private bool Owns(LearningExtraction record)
            => record.State is LearningExtractionState.Following
               && Equals(record.Version, Recording.Version)
               && record.ReadThrough == Through
               && record.Gaps.Count == gaps;
    }

    private sealed class Handover : IMatroskaSink
    {
        public const string RawPictures = "V_UNCOMPRESSED";

        public const string RawSound = "A_PCM/INT/LIT";

        private int frames = -1;

        private int pictures = -1;

        private int sound = -1;

        public LearningDataTimeline? Timeline { get; private set; }

        public List<LearningDataChunk> Ready { get; } = [];

        public void Tracks(IReadOnlyList<MatroskaTrack> tracks)
        {
            MatroskaTrack framed = Picture(tracks, FrameLight.Width, FrameLight.Height);

            frames = framed.Number;
            pictures = Picture(tracks, WatermarkFrame.Width, WatermarkFrame.Height).Number;
            sound = Sound(tracks).Number;
            Timeline = new LearningDataTimeline(framed.FrameLength);
        }

        public void Block(int track, TimeSpan at, ReadOnlySpan<byte> payload)
        {
            LearningDataTimeline timeline = Timeline
                ?? throw new LearningDataUnplaceableException(ExtractionFailure.Other, "A block came before the tracks were described.");

            if (track == frames)
            {
                Ready.AddRange(timeline.See(at, payload));
            }
            else if (track == pictures)
            {
                Ready.AddRange(timeline.Glimpse(at, payload));
            }
            else if (track == sound)
            {
                Ready.AddRange(timeline.Hear(at, Samples(payload)));
            }
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
