using Carina.Api.Playback;
using Carina.Api.Requests;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public sealed record LiveSessionAsking(
    NetworkId Network,
    ServiceId Service,
    LiveProfile? Profile,
    SoundTrack Sound,
    VideoCodec Format)
{
    public static readonly string WhatASessionIsAskedWith =
        "A session is asked for by a networkId and a serviceId, each a whole number in the range a broadcast carries. "
        + "profile is left out for what this machine encodes at, or is one of "
        + $"{string.Join(", ", LiveProfile.All.Select(profile => profile.Name))}. "
        + $"sound is left out for the main sound, or is one of {string.Join(", ", SoundTracks.Names)}. "
        + $"format is left out for {VideoCodecs.H264IsCalled}, or is one of {string.Join(", ", VideoCodecs.Names)}.";

    public static LiveSessionAsking? Read(LiveSessionRequest? request)
    {
        if (request is not { NetworkId: { } network, ServiceId: { } service }
            || network is < NetworkId.MinValue or > NetworkId.MaxValue
            || service is < ServiceId.MinValue or > ServiceId.MaxValue)
        {
            return null;
        }

        AskedProfile profile = AskedProfile.Read(request.Profile);
        AskedSound sound = AskedSound.Read(request.Sound);
        AskedFormat format = AskedFormat.Read(request.Format);

        if (profile.Answer is ProfileAnswer.NotOneOfThese
            || sound.Answer is SoundAnswer.NotOneOfThese
            || format.Answer is FormatAnswer.NotOneOfThese)
        {
            return null;
        }

        return new LiveSessionAsking(
            new NetworkId(network),
            new ServiceId(service),
            profile.Named,
            sound.Track,
            format.Format);
    }
}
