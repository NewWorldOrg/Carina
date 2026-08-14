using Carina.Domain.Channels;

namespace Carina.Infrastructure.Scanning;

public sealed record ScanSettings
{
    public static readonly ScanSettings Default = new();

    public TimeSpan AttemptPatience { get; init; } = TimeSpan.FromSeconds(20);

    public RotationBackoff BusyWait { get; init; } =
        new(TimeSpan.FromSeconds(2), 2, TimeSpan.FromSeconds(30), 5);

    public RotationBackoff Rotation { get; init; } = RotationBackoff.Default;

    public int ReadBufferSize { get; init; } = 188 * 348;

    public bool AttemptsAreBounded => AttemptPatience > TimeSpan.Zero
        && AttemptPatience != Timeout.InfiniteTimeSpan;
}
