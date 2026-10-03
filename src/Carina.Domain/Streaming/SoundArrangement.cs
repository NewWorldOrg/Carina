using Carina.Domain.Base;
using Carina.Domain.Reservations;

namespace Carina.Domain.Streaming;

public readonly record struct AnnouncedSound(AudioMode Audio, int Sounds)
{
    public bool SaidNothing
        => Audio is AudioMode.Undetermined && Sounds is ProgrammeSnapshot.SoundsUnannounced;
}

public sealed record SoundArrangement
{
    private const int StreamsOfTheirOwn = 2;

    private static readonly SoundArrangement Nothing = new([]);

    public static readonly SoundArrangement TheMainSoundAlone = new([SoundPlacement.WholeStream(0)]);

    private static readonly SoundArrangement TwoStreams = new(
        [SoundPlacement.WholeStream(0), SoundPlacement.WholeStream(1)]);

    private static readonly SoundArrangement TwoChannelsOfOneStream = new(
        [SoundPlacement.OneChannelOf(0, SoundChannel.Left), SoundPlacement.OneChannelOf(0, SoundChannel.Right)]);

    private static readonly SoundArrangement TwoChannelsBesideASecondStream = new(
        [
            SoundPlacement.OneChannelOf(0, SoundChannel.Left),
            SoundPlacement.OneChannelOf(0, SoundChannel.Right),
            SoundPlacement.WholeStream(1),
        ]);

    private readonly IReadOnlyList<SoundPlacement> placed;

    private SoundArrangement(IReadOnlyList<SoundPlacement> placed)
    {
        this.placed = placed;
        Tracks = [.. SoundTracks.InOrder.Take(placed.Count)];
    }

    public IReadOnlyList<SoundTrack> Tracks { get; }

    public bool Holds(SoundTrack track) => Tracks.Contains(track);

    public SoundPlacement Placement(SoundTrack track)
    {
        if (!Holds(track))
        {
            throw new ArgumentOutOfRangeException(
                nameof(track),
                track,
                "A sound this recording does not carry is taken from nowhere.");
        }

        return placed[SoundTracks.Ordinal(track)];
    }

    /// <summary>
    /// Where a live viewer's sound is taken from: the placement this arrangement gives the sound asked for, and
    /// the whole stream at that sound's place in the programme when the arrangement holds no such sound.
    /// </summary>
    public SoundPlacement Reaching(SoundTrack track)
        => Holds(track) ? Placement(track) : SoundPlacement.WholeStream(SoundTracks.Ordinal(track));

    public static SoundArrangement Of(AnnouncedSound announced)
        => announced switch
        {
            { Audio: AudioMode.DualMono, Sounds: < StreamsOfTheirOwn } => TwoChannelsOfOneStream,
            { Audio: AudioMode.DualMono } => TwoChannelsBesideASecondStream,
            { Sounds: >= StreamsOfTheirOwn } => TwoStreams,
            _ => TheMainSoundAlone,
        };

    public static SoundArrangement Of(CarriedSounds read)
    {
        ArgumentNullException.ThrowIfNull(read);

        return read.Tracks.Count switch
        {
            0 => Nothing,
            1 => TheMainSoundAlone,
            _ => TwoStreams,
        };
    }
}
