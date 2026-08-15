namespace Carina.Api.Services;

public enum DriverShutdownFailure
{
    DriverUnreachable = 1,

    DriverRefused = 2,

    CapabilityMissing = 3,

    RecordingInProgress = 4,

    DriverInconsistent = 5,
}

public sealed record DriverShutdownView(
    string? InstanceId,
    DateTimeOffset AcceptedAt,
    int BudgetSeconds);
