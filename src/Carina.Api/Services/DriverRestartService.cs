using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Driver;

namespace Carina.Api.Services;

public sealed class DriverRestartService(IDriverClient driver)
{
    public const string CapabilityMissingTitle = "capabilityMissing";

    public const string RecordingInProgressTitle = "recordingInProgress";

    public static readonly string EndpointMissingTitle =
        DriverProblem.TitleForStatus(StatusCodes.Status404NotFound);

    public async Task<ServiceResult<DriverRestartView, DriverRestartFailure>> RequestAsync(
        CancellationToken cancellationToken)
    {
        DriverCall<DriverRestartDto> call = await driver.RequestRestartAsync(cancellationToken);

        if (!call.TryGetValue(out DriverRestartDto? accepted))
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

        string? title = call.Problem?.Title;

        if (string.Equals(title, EndpointMissingTitle, StringComparison.Ordinal))
        {
            return DriverRestartFailure.DriverInconsistent;
        }

        return title switch
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

        if (string.Equals(problem.Title, EndpointMissingTitle, StringComparison.Ordinal))
        {
            return $"The driver says it can be restarted on request but does not answer {DriverEndpoints.Restart}; the two halves of this pair are not the same build.";
        }

        return problem.Problems.Count == 0
            ? problem.Title
            : $"{problem.Title}: {string.Join(" ", problem.Problems)}";
    }
}
