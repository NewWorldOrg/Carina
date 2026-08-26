using Carina.Domain.Channels;

namespace Carina.TestSupport;

public sealed class HeldReachSettings : IServiceReachSettingsRepository
{
    private static readonly DateTime Configured = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

    private ServiceReachSettings? held;

    public int Saves { get; private set; }

    public Task<ServiceReachSettings> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(held ??= ServiceReachSettings.Default(Configured));

    public Task SaveAsync(ServiceReachSettings settings, CancellationToken cancellationToken)
    {
        held = settings;
        Saves++;

        return Task.CompletedTask;
    }
}
