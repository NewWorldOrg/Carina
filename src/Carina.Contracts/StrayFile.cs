namespace Carina.Contracts;

public static class StrayFileStamp
{
    public static DateTime Truncated(DateTime moment) =>
        new(moment.Ticks - (moment.Ticks % TimeSpan.TicksPerMicrosecond), DateTimeKind.Utc);
}

public sealed record StrayFileErasureRequest
{
    public string OutputRoot { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public DateTimeOffset LastWrittenAt { get; init; }
}

public sealed record StrayFileErasedDto
{
    public string OutputRoot { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public bool FileRemoved { get; init; }
}
