using Carina.Contracts;
using Carina.Domain.Driver;

namespace Carina.Infrastructure.Recordings;

public sealed record StorageMonitorSettings
{
    public static readonly StorageMonitorSettings Default = new(TimeSpan.FromMinutes(1));

    public StorageMonitorSettings(TimeSpan restBetweenReads)
    {
        if (restBetweenReads <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(restBetweenReads),
                restBetweenReads,
                "Asking a root whether it takes a file makes the driver write one, so an answer is held for a while "
                + "rather than asked for again.");
        }

        RestBetweenReads = restBetweenReads;
    }

    public TimeSpan RestBetweenReads { get; }
}

public sealed class StorageMonitor(
    IDriverClient client,
    TimeProvider timeProvider,
    StorageMonitorSettings settings)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    private DriverCall<IReadOnlyList<StorageRootDto>>? held;

    private DateTimeOffset heldAt;

    public async Task<DriverCall<IReadOnlyList<StorageRootDto>>> ReadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            DateTimeOffset now = timeProvider.GetUtcNow();

            if (held is { } answer && now - heldAt < settings.RestBetweenReads)
            {
                return answer;
            }

            DriverCall<IReadOnlyList<StorageRootDto>> fresh = await client.GetStorageAsync(cancellationToken);

            held = fresh;
            heldAt = now;

            return fresh;
        }
        finally
        {
            gate.Release();
        }
    }
}
