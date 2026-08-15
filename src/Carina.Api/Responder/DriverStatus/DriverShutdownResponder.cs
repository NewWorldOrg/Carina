using Carina.Api.Services;

namespace Carina.Api.Responder.DriverStatus;

public sealed record DriverShutdownResponder(
    string? InstanceId,
    DateTimeOffset AcceptedAt,
    int BudgetSeconds)
{
    public static DriverShutdownResponder Of(DriverShutdownView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new DriverShutdownResponder(view.InstanceId, view.AcceptedAt, view.BudgetSeconds);
    }
}
