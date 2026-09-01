namespace Carina.Domain.Streaming;

public sealed record StreamAttributeSettings
{
    public string Programme { get; init; } = "ffprobe";

    public TimeSpan LongestRead { get; init; } = TimeSpan.FromSeconds(10);
}
