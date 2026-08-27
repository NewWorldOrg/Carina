using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

namespace Carina.Api.Services;

public enum StorageFailure
{
    DriverUnreachable = 1,

    DriverRefused = 2,
}

public sealed class StorageService(
    StorageMonitor storage,
    IRecordingRepository recordings,
    TimeProvider clock)
{
    public async Task<ServiceResult<IReadOnlyList<StorageRootStanding>, StorageFailure>> ReadAsync(
        CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<StorageRootDto>> answer = await storage.ReadAsync(cancellationToken);

        if (!answer.TryGetValue(out IReadOnlyList<StorageRootDto>? declared))
        {
            return ServiceResult<IReadOnlyList<StorageRootStanding>, StorageFailure>.Failure(
                Describe(answer),
                answer.Outcome is DriverCallOutcome.Unreachable
                    ? StorageFailure.DriverUnreachable
                    : StorageFailure.DriverRefused);
        }

        IReadOnlyList<Recording> running = await recordings.ListInFlightAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<StorageRootStanding>, StorageFailure>.Success(
            StorageStanding.Of(
                declared,
                [.. running.Select(Spoken)],
                clock.GetUtcNow().UtcDateTime));
    }

    private static RootDemand Spoken(Recording recording)
        => new(
            recording.OutputRoot,
            RecordingDemand.AtTheHeaviestRate(recording.ExpectedWindowStart, recording.ExpectedWindowEnd));

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
