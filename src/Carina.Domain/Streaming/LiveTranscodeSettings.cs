namespace Carina.Domain.Streaming;

public sealed record LiveTranscodeSettings
{
    public string Programme { get; init; } = "ffmpeg";

    public LiveEncoder Prefer { get; init; } = LiveEncoder.Software;

    public TimeSpan LongestProbe { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan StopGrace { get; init; } = TimeSpan.FromSeconds(2);
}
