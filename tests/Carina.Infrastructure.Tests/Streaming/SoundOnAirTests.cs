using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class SoundOnAirTests
{
    private static readonly DateTime Noon = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    private static readonly NetworkId Network = new(32736);

    private static readonly ServiceId Service = new(1024);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HeldProgrammes programmes = new();

    [Fact(DisplayName = "the sound on air is what the programme being broadcast now announced")]
    public async Task TheSoundOnAirIsWhatTheProgrammeBeingBroadcastNowAnnounced()
    {
        programmes.Programmes.Add(Announced(6, Noon.AddHours(-2), Noon.AddMinutes(-30), AudioMode.Surround, 1));
        programmes.Programmes.Add(Announced(7, Noon.AddMinutes(-30), Noon.AddMinutes(30), AudioMode.DualMono, 1));
        programmes.Programmes.Add(Announced(8, Noon.AddMinutes(30), Noon.AddHours(2), AudioMode.Stereo, 2));

        Assert.Equal(new AnnouncedSound(AudioMode.DualMono, 1), await OnAirAsync());
    }

    [Fact(DisplayName = "a programme ends the moment the next one starts, and one that starts now is on air")]
    public async Task AProgrammeEndsTheMomentTheNextOneStarts()
    {
        programmes.Programmes.Add(Announced(7, Noon.AddHours(-1), Noon, AudioMode.DualMono, 1));
        programmes.Programmes.Add(Announced(8, Noon, Noon.AddHours(1), AudioMode.Stereo, 2));

        Assert.Equal(new AnnouncedSound(AudioMode.Stereo, 2), await OnAirAsync());
    }

    [Fact(DisplayName = "a programme with no end yet is on air until a later one starts")]
    public async Task AProgrammeWithNoEndYetIsOnAirUntilALaterOneStarts()
    {
        programmes.Programmes.Add(Announced(7, Noon.AddHours(-3), null, AudioMode.DualMono, 1));

        Assert.Equal(new AnnouncedSound(AudioMode.DualMono, 1), await OnAirAsync());

        programmes.Programmes.Add(Announced(8, Noon.AddMinutes(-5), Noon.AddHours(1), AudioMode.Stereo, 1));

        Assert.Equal(new AnnouncedSound(AudioMode.Stereo, 1), await OnAirAsync());
    }

    [Fact(DisplayName = "a channel with nothing on air that the guide knows of has announced nothing, whatever another channel or a placeholder says")]
    public async Task AChannelWithNothingOnAirHasAnnouncedNothing()
    {
        programmes.Programmes.Add(Announced(6, Noon.AddHours(-2), Noon.AddHours(-1), AudioMode.DualMono, 1));
        programmes.Programmes.Add(Announced(7, Noon.AddHours(1), Noon.AddHours(2), AudioMode.DualMono, 1));
        programmes.Programmes.Add(Announced(8, Noon.AddMinutes(-30), Noon.AddMinutes(30), AudioMode.DualMono, 1, service: 1032));
        programmes.Programmes.Add(Announced(9, Noon.AddMinutes(-30), Noon.AddMinutes(30), AudioMode.DualMono, 1, shadow: true));

        AnnouncedSound announced = await OnAirAsync();

        Assert.True(announced.SaidNothing);
        Assert.Equal(SoundPlacement.WholeStream(0), SoundArrangement.Of(announced).Reaching(SoundTrack.Main));
    }

    private Task<AnnouncedSound> OnAirAsync()
        => new SoundOnAir(programmes, new HandTurnedClock(Noon)).AnnouncedAsync(Network, Service, Cancel);

    private static Programme Announced(
        int eventId,
        DateTime startsAt,
        DateTime? endsAt,
        AudioMode audio,
        int sounds,
        int service = 1024,
        bool shadow = false)
        => Programme.Rehydrate(
            new ProgrammeId(Network, new ServiceId(service), new EventId(eventId)),
            new TransportStreamId(32736),
            startsAt,
            endsAt,
            "a programme",
            string.Empty,
            shadow,
            startsAt,
            audio: audio,
            sounds: sounds);
}
