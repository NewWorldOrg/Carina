using Carina.Infrastructure.Events;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.Unit.Infra.Events;

internal static class AppEventListening
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    public static async Task<IReadOnlyList<AppEventName>> Next(AppEventListener listener)
    {
        using var deadline = new CancellationTokenSource(Soon);

        return await listener.TakeAsync(deadline.Token);
    }
}
