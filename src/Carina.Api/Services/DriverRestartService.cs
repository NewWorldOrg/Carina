using Carina.Api.Common;
using Carina.Domain.Driver;

namespace Carina.Api.Services;

public sealed class DriverRestartService(IDriverClient driver)
{
    public const string CapabilityMissingTitle = "capabilityMissing";

    public const string RecordingInProgressTitle = "recordingInProgress";

    public async Task<ServiceResult<DriverRestartView, DriverRestartFailure>> RequestAsync(
        CancellationToken cancellationToken)
    {
        var call = await driver.RequestShutdownAsync(cancellationToken);

        if (!call.TryGetValue(out var accepted))
        {
            return ServiceResult<DriverRestartView, DriverRestartFailure>.Failure(
                Describe(call),
                FailureOf(call));
        }

        return ServiceResult<DriverRestartView, DriverRestartFailure>.Success(
            new DriverRestartView(
                accepted.InstanceId,
                accepted.AcceptedAt,
                accepted.BudgetSeconds));
    }

    private static DriverRestartFailure FailureOf<T>(DriverCall<T> call)
    {
        if (call.Outcome is DriverCallOutcome.Unreachable)
        {
            return DriverRestartFailure.DriverUnreachable;
        }

        return call.Problem?.Title switch
        {
            CapabilityMissingTitle => DriverRestartFailure.CapabilityMissing,
            RecordingInProgressTitle => DriverRestartFailure.RecordingInProgress,
            _ => DriverRestartFailure.DriverRefused,
        };
    }

    private static string Describe<T>(DriverCall<T> call)
    {
        if (call.Failure is { } failure)
        {
            return failure;
        }

        if (call.Problem is not { } problem)
        {
            return "The driver answered without saying anything.";
        }

        return problem.Problems.Count == 0
            ? problem.Title
            : $"{problem.Title}: {string.Join(" ", problem.Problems)}";
    }
}
