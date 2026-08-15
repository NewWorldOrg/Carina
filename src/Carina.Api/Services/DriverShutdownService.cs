using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Driver;

namespace Carina.Api.Services;

public sealed class DriverShutdownService(IDriverClient driver)
{
    public const string CapabilityMissingTitle = "capabilityMissing";

    public const string RecordingInProgressTitle = "recordingInProgress";

    public static readonly string EndpointMissingTitle =
        DriverProblem.TitleForStatus(StatusCodes.Status404NotFound);

    public async Task<ServiceResult<DriverShutdownView, DriverShutdownFailure>> RequestAsync(
        CancellationToken cancellationToken)
    {
        var call = await driver.RequestShutdownAsync(cancellationToken);

        if (!call.TryGetValue(out var accepted))
        {
            return ServiceResult<DriverShutdownView, DriverShutdownFailure>.Failure(
                Describe(call),
                FailureOf(call));
        }

        return ServiceResult<DriverShutdownView, DriverShutdownFailure>.Success(
            new DriverShutdownView(
                accepted.InstanceId,
                accepted.AcceptedAt,
                accepted.BudgetSeconds));
    }

    private static DriverShutdownFailure FailureOf<T>(DriverCall<T> call)
    {
        if (call.Outcome is DriverCallOutcome.Unreachable)
        {
            return DriverShutdownFailure.DriverUnreachable;
        }

        var title = call.Problem?.Title;

        if (string.Equals(title, EndpointMissingTitle, StringComparison.Ordinal))
        {
            return DriverShutdownFailure.DriverInconsistent;
        }

        return title switch
        {
            CapabilityMissingTitle => DriverShutdownFailure.CapabilityMissing,
            RecordingInProgressTitle => DriverShutdownFailure.RecordingInProgress,
            _ => DriverShutdownFailure.DriverRefused,
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
            return $"The driver says it can be stopped on request but does not answer {DriverEndpoints.Shutdown}; the two halves of this pair are not the same build.";
        }

        return problem.Problems.Count == 0
            ? problem.Title
            : $"{problem.Title}: {string.Join(" ", problem.Problems)}";
    }
}
