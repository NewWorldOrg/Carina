namespace Carina.Domain.Channels;

public interface ITuneFailureReporter
{
    Task ReportFailureAsync(CandidateChannelId candidateChannelId, DateTime at, CancellationToken cancellationToken);

    Task ReportReachedAsync(CandidateChannelId candidateChannelId, DateTime at, CancellationToken cancellationToken);
}
