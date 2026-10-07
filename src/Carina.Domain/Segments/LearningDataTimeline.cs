using System.Runtime.InteropServices;

using Carina.Domain.Encodings;

namespace Carina.Domain.Segments;

/// <summary>
/// Gathers the learning data of one recording from its sound, its frames and its pictures of the
/// corners, handed over in blocks that each carry their time on the recording's own time, and puts
/// every block where its time says: the sound by <see cref="SoundPlacement"/>, the frames by
/// <see cref="FramePlacement"/> and the pictures by <see cref="CornerPlacement"/>. What is skipped
/// is filled, the sound with silence and the frames and pictures with copies of the one before, and
/// the stretches filled are kept as <see cref="LearningDataGaps"/>. The frames are placed at the rate
/// <see cref="FrameRates"/> reads from the length of a frame, or without one from the time between
/// the first two, starting at the first frame. Sound and pictures handed over before the frames can
/// be placed wait for them, the sound no longer than <see cref="LongestWaitForFrames"/>.
/// </summary>
public sealed class LearningDataTimeline
{
    public static readonly TimeSpan LongestWaitForFrames = TimeSpan.FromMinutes(1);

    private static readonly short[] Quiet = new short[SoundReader.SampleRate * SoundReader.Channels];

    private static readonly long MostSoundWaiting =
        (long)LongestWaitForFrames.TotalSeconds * SoundReader.SampleRate * SoundReader.Channels;

    private static readonly int MostPicturesWaiting = (int)LongestWaitForFrames.TotalSeconds + 1;

    private readonly TimeSpan? frameLength;

    private readonly SoundPlacement sound = new();

    private readonly CornerPlacement corners = new();

    private readonly LearningDataGaps gaps = new();

    private readonly List<short> soundWaiting = [];

    private readonly List<(TimeSpan At, byte[] Picture)> picturesWaiting = [];

    private readonly byte[] lastFrame = new byte[FrameLight.Pixels];

    private readonly byte[] lastPicture = new byte[WatermarkFrame.Pixels];

    private (TimeSpan At, byte[] Frame)? firstFrame;

    private FramePlacement? frames;

    private LearningDataCollector? collector;

    private TimeSpan? firstSoundAt;

    private bool pictured;

    public LearningDataTimeline(TimeSpan? frameLength)
    {
        if (frameLength is { } length)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero, nameof(frameLength));
        }

        this.frameLength = frameLength;
    }

    /// <summary>
    /// The sound taken, the first of the programme's, and how far its first block lies from the
    /// first frame; null until both have been handed over.
    /// </summary>
    public ExtractionSound? Sound
        => firstSoundAt is { } heard && frames is { } placed
            ? new ExtractionSound(0, heard - placed.Clock.FirstFrameAt)
            : null;

    public TimeSpan PlacedThrough
    {
        get
        {
            TimeSpan seen = frames?.PlacedThrough ?? TimeSpan.Zero;

            return seen > sound.PlacedThrough ? seen : sound.PlacedThrough;
        }
    }

    private TimeSpan Settled
    {
        get
        {
            TimeSpan seen = frames?.PlacedThrough ?? TimeSpan.Zero;

            return seen < sound.PlacedThrough ? seen : sound.PlacedThrough;
        }
    }

    /// <summary>
    /// Takes out the gaps found so far that nothing handed over later can reach back into.
    /// </summary>
    public IReadOnlyList<LearningDataGap> SettledGaps() => gaps.TakeThrough(Settled);

    /// <summary>
    /// Takes out every gap found and not yet taken out.
    /// </summary>
    public IReadOnlyList<LearningDataGap> RemainingGaps() => gaps.TakeAll();

    public IReadOnlyList<LearningDataChunk> Hear(TimeSpan at, ReadOnlySpan<short> interleaved)
    {
        if (interleaved.Length % SoundReader.Channels is not 0)
        {
            throw new LearningDataUnplaceableException(
                ExtractionFailure.Other,
                $"Sound is handed over in pairs of samples, and this block holds {interleaved.Length} samples.");
        }

        firstSoundAt ??= at;

        SoundPlace place = sound.Place(at, interleaved.Length / SoundReader.Channels);
        List<LearningDataChunk> chunks = [];

        gaps.Add(place.Gap);

        for (long left = place.Silence; left > 0; left -= Quiet.Length / SoundReader.Channels)
        {
            int pairs = (int)Math.Min(left, Quiet.Length / SoundReader.Channels);

            Heard(Quiet.AsSpan(0, pairs * SoundReader.Channels), chunks);
        }

        Heard(interleaved[(place.Dropped * SoundReader.Channels)..], chunks);

        return chunks;
    }

    public IReadOnlyList<LearningDataChunk> See(TimeSpan at, ReadOnlySpan<byte> frame)
    {
        Sized(frame, FrameLight.Pixels, "frame");

        if (collector is not null && frames is not null)
        {
            return Placed(collector, frames, at, frame);
        }

        if (frameLength is { } length)
        {
            return Begun(ClockOf(length, at), at, frame);
        }

        if (firstFrame is not { } first)
        {
            firstFrame = (at, frame.ToArray());

            return [];
        }

        if (at <= first.At)
        {
            return [];
        }

        List<LearningDataChunk> chunks = Begun(ClockOf(at - first.At, first.At), first.At, first.Frame);

        if (collector is not null && frames is not null)
        {
            chunks.AddRange(Placed(collector, frames, at, frame));
        }

        return chunks;
    }

    public IReadOnlyList<LearningDataChunk> Glimpse(TimeSpan at, ReadOnlySpan<byte> picture)
    {
        Sized(picture, WatermarkFrame.Pixels, "picture of the corners");

        if (collector is not null)
        {
            return Placed(collector, at, picture);
        }

        if (picturesWaiting.Count >= MostPicturesWaiting)
        {
            throw new LearningDataUnplaceableException(
                ExtractionFailure.StreamMissing,
                $"No frame came with the first {MostPicturesWaiting} pictures of the corners.");
        }

        picturesWaiting.Add((at, picture.ToArray()));

        return [];
    }

    public IReadOnlyList<LearningDataChunk> Finish()
    {
        if (collector is null)
        {
            throw new LearningDataUnplaceableException(ExtractionFailure.StreamMissing, "No frames were handed over to place.");
        }

        if (firstSoundAt is null)
        {
            throw new LearningDataUnplaceableException(ExtractionFailure.StreamMissing, "No sound was handed over to place.");
        }

        return collector.Finish();
    }

    private static FrameClock ClockOf(TimeSpan length, TimeSpan firstAt)
        => length <= FrameRates.Longest
            ? FrameRates.Clock(length, NoEarlierThanZero(firstAt))
            : throw new LearningDataUnplaceableException(
                ExtractionFailure.TimingMismatch,
                $"A frame lasting {length} is too long to read a frame rate from.");

    private static TimeSpan NoEarlierThanZero(TimeSpan at) => at < TimeSpan.Zero ? TimeSpan.Zero : at;

    private static void Sized(ReadOnlySpan<byte> handed, int size, string what)
    {
        if (handed.Length != size)
        {
            throw new LearningDataUnplaceableException(
                ExtractionFailure.Other,
                $"A {what} is {size} bytes, and this one is {handed.Length}.");
        }
    }

    private void Heard(ReadOnlySpan<short> placed, List<LearningDataChunk> chunks)
    {
        if (collector is not null)
        {
            chunks.AddRange(collector.Hear(placed));

            return;
        }

        if (soundWaiting.Count + placed.Length > MostSoundWaiting)
        {
            throw new LearningDataUnplaceableException(
                ExtractionFailure.StreamMissing,
                $"No frame came with the first {LongestWaitForFrames} of sound.");
        }

        soundWaiting.AddRange(placed);
    }

    private List<LearningDataChunk> Begun(FrameClock clock, TimeSpan at, ReadOnlySpan<byte> frame)
    {
        LearningDataCollector begun = new(clock);
        FramePlacement placement = new(clock);
        List<LearningDataChunk> chunks = [.. begun.Hear(CollectionsMarshal.AsSpan(soundWaiting))];

        collector = begun;
        frames = placement;
        soundWaiting.Clear();
        soundWaiting.TrimExcess();

        foreach ((TimeSpan pictureAt, byte[] picture) in picturesWaiting)
        {
            chunks.AddRange(Placed(begun, pictureAt, picture));
        }

        picturesWaiting.Clear();
        chunks.AddRange(Placed(begun, placement, at, frame));

        return chunks;
    }

    private List<LearningDataChunk> Placed(LearningDataCollector gathering, FramePlacement placement, TimeSpan at, ReadOnlySpan<byte> frame)
    {
        FramePlace place = placement.Place(at);

        gaps.Add(place.Gap);

        if (!place.Kept)
        {
            return [];
        }

        List<LearningDataChunk> chunks = [];

        for (long copy = 0; copy < place.Repeats; copy++)
        {
            chunks.AddRange(gathering.See(lastFrame));
        }

        chunks.AddRange(gathering.See(frame));
        frame.CopyTo(lastFrame);

        return chunks;
    }

    private List<LearningDataChunk> Placed(LearningDataCollector gathering, TimeSpan at, ReadOnlySpan<byte> picture)
    {
        CornerPlace place = corners.Place(at);

        if (!place.Kept)
        {
            return [];
        }

        List<LearningDataChunk> chunks = [];
        ReadOnlySpan<byte> before = pictured ? lastPicture : picture;

        for (long copy = 0; copy < place.Repeats; copy++)
        {
            chunks.AddRange(gathering.Glimpse(before));
        }

        chunks.AddRange(gathering.Glimpse(picture));
        picture.CopyTo(lastPicture);
        pictured = true;

        return chunks;
    }
}
