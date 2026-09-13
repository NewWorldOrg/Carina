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

    private readonly IReadOnlyList<SoundPlacement> placed;

    private SoundArrangement(IReadOnlyList<SoundPlacement> placed)
    {
        this.placed = placed;
        Tracks = SoundTracks.OutOf(placed.Count);
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

    public static SoundArrangement Of(AnnouncedSound announced)
        => announced switch
        {
            { Audio: AudioMode.DualMono, Sounds: < StreamsOfTheirOwn } => TwoChannelsOfOneStream,
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
