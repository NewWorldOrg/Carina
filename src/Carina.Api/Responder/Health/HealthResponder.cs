using Carina.Api.Services;

namespace Carina.Api.Responder.Health;

public sealed record HealthResponder(string Status, IReadOnlyList<string> Degraded)
{
    public static HealthResponder Of(HealthView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new HealthResponder(view.Status, view.Degraded);
    }
}
