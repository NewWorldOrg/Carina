using Carina.Domain.Channels;

namespace Carina.TestSupport;

public sealed class RememberedTuneReports : ITuneFailureReporter
{
    public List<CandidateChannelId> Failures { get; } = [];

    public List<CandidateChannelId> Reached { get; } = [];

    public Task ReportFailureAsync(
        CandidateChannelId candidateChannelId,
        DateTime at,
        CancellationToken cancellationToken)
    {
        Failures.Add(candidateChannelId);

        return Task.CompletedTask;
    }

    public Task ReportReachedAsync(
        CandidateChannelId candidateChannelId,
        DateTime at,
        CancellationToken cancellationToken)
    {
        Reached.Add(candidateChannelId);

        return Task.CompletedTask;
    }
}
