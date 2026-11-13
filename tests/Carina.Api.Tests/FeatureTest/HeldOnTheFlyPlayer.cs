using Carina.Domain.Channels;
using Carina.Domain.Playback;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class HeldOnTheFlyPlayer : IOnTheFlyPlayer
{
    public byte[] Picture { get; set; } = [.. Enumerable.Range(0, 3_000).Select(index => (byte)(index % 251))];

    public OnTheFlyRefusal? Refuses { get; set; }

    public int Running { get; set; } = 1;

    public int AtOnce { get; set; } = 2;

    public TimeSpan Waited { get; set; } = TimeSpan.FromMilliseconds(138);

    public LiveEncoder Machine { get; set; } = LiveEncoder.Software;

    public List<TimeSpan> AskedFrom { get; } = [];

    public List<string?> AskedFor { get; } = [];

    public List<VideoCodec> AskedIn { get; } = [];

    public List<ServiceId> AskedOf { get; } = [];

    public List<SoundPlacement> AskedWith { get; } = [];

    public List<PlaybackFile> Opened { get; } = [];

    public int Sounds { get; set; } = 1;

    public string? SoundsCannotBeRead { get; set; }

    public int AskedWhatItCarries { get; private set; }

    public HeldViewing? Handed { get; private set; }

    public List<HeldViewing> EveryHanded { get; } = [];

    public TranscoderExit Exit { get; set; } = TranscoderExit.Finished();

    public bool WritesOn { get; set; }

    public int? Seats { get; set; }

    public Task<OnTheFlyStart> StartAsync(
        PlaybackFile file,
        ServiceId service,
        TimeSpan from,
        LiveProfile? profile,
        VideoCodec format,
        SoundPlacement sound,
        CancellationToken cancellationToken)
    {
        AskedFrom.Add(from);
        AskedFor.Add(profile?.Name);
        AskedIn.Add(format);
        AskedOf.Add(service);
        AskedWith.Add(sound);
        Opened.Add(file);

        if (Refuses is { } refusal)
        {
            return Task.FromResult(OnTheFlyStart.Refused(refusal, "the held player was told to refuse."));
        }

        if (Seats is { } seats && EveryHanded.Count(viewing => !viewing.WasLetGo) >= seats)
        {
            return Task.FromResult(OnTheFlyStart.Refused(OnTheFlyRefusal.TooManyAlready, "every seat the held player has is taken."));
        }

        Handed = new HeldViewing(
            new OnTheFlyStanding(
                from,
                Waited,
                profile ?? LiveProfile.Unasked(Machine),
                LiveEncoderChoice.Asked(Machine),
                attributesWereMeasured: true,
                Running,
                AtOnce),
            Picture,
            Exit,
            WritesOn);
        EveryHanded.Add(Handed);

        return Task.FromResult(OnTheFlyStart.Started(Handed));
    }

    public Task<CarriedSounds> SoundsAsync(
        PlaybackFile file,
        ServiceId service,
        CancellationToken cancellationToken)
    {
        AskedWhatItCarries++;

        return Task.FromResult(SoundsCannotBeRead is { } why
            ? CarriedSounds.Unread(why)
            : CarriedSounds.Counted(Sounds));
    }
}
