namespace Carina.Domain.Notifications;

public sealed class PushHandlingDomainService(IProgrammeWatchRepository watches)
{
    public async Task<PushHandling> OfAsync(Notice notice, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        if (notice.Kind is not NoticeKind.ProgrammeStarting)
        {
            return PushHandling.Usual;
        }

        ProgrammeWatch? watch = await watches.FindAsync(new ProgrammeWatchId(notice.SourceId), cancellationToken);

        return PushHandling.UntilTheEnd(watch?.EndsAt ?? at, at);
    }
}
