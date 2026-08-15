namespace Carina.Api.Services;

public enum DriverRestartFailure
{
    DriverUnreachable = 1,

    DriverRefused = 2,

    CapabilityMissing = 3,

    RecordingInProgress = 4,

    DriverInconsistent = 5,
}

public sealed record DriverRestartView(
    string? InstanceId,
    DateTimeOffset AcceptedAt,
    int BudgetSeconds);
