using Carina.Domain.DataBroadcast;

namespace Carina.TestSupport;

/// <summary>
/// The one row of the data broadcast settings, held in memory. Absent until something changes a setting.
/// </summary>
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
