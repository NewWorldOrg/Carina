using Carina.Api.Services;

namespace Carina.Api.Responder.DriverStatus;

public sealed record DriverRestartResponder(
    string? InstanceId,
    DateTimeOffset AcceptedAt,
    int BudgetSeconds)
{
    public static DriverRestartResponder Of(DriverRestartView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new DriverRestartResponder(view.InstanceId, view.AcceptedAt, view.BudgetSeconds);
    }
}
