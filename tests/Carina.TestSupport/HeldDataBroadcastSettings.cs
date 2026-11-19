using Carina.Domain.DataBroadcast;

namespace Carina.TestSupport;

public sealed class HeldDataBroadcastSettings : IDataBroadcastSettingsRepository
{
    private DataBroadcastSettings? held;

    public int Saves { get; private set; }

    public int Turns { get; private set; }

    public Task TakeTurnAsync(CancellationToken cancellationToken)
    {
        Turns++;

        return Task.CompletedTask;
    }

    public Task<DataBroadcastSettings?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(held);

    public Task SaveAsync(DataBroadcastSettings settings, CancellationToken cancellationToken)
    {
        held = settings;
        Saves++;

        return Task.CompletedTask;
    }
}
