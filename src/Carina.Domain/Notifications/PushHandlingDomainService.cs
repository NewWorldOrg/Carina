namespace Carina.Domain.Notifications;

/// <summary>
/// Decides how a push service is asked to handle a notice. A notice that a programme asked to be told of begins is
/// urgent and is kept until that programme ends, as the row asked for holds it now; every other notice is kept a
/// day at normal urgency.
/// </summary>
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
