using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Driver;

namespace Carina.Api.Services;

public enum CardReaderFailure
{
    DriverUnreachable = 1,

    CapabilityMissing = 2,

    DriverRefused = 3,
}

public sealed class DriverCardReaderService(ICardReaderStatusReader driver)
{
    public async Task<ServiceResult<CardReaderDto, CardReaderFailure>> ReadAsync(CancellationToken cancellationToken)
    {
        DriverCall<CardReaderDto> call = await driver.GetCardReaderAsync(cancellationToken);

        if (call.TryGetValue(out CardReaderDto? reader))
        {
            return ServiceResult<CardReaderDto, CardReaderFailure>.Success(reader);
        }

        return ServiceResult<CardReaderDto, CardReaderFailure>.Failure(Describe(call), FailureOf(call));
    }

    private static CardReaderFailure FailureOf(DriverCall<CardReaderDto> call)
    {
        if (call.Outcome is DriverCallOutcome.Unreachable)
        {
            return CardReaderFailure.DriverUnreachable;
        }

        return call.Problem?.Title is SessionRefusalTitles.CapabilityMissing
            ? CardReaderFailure.CapabilityMissing
            : CardReaderFailure.DriverRefused;
    }

    private static string Describe(DriverCall<CardReaderDto> call)
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
