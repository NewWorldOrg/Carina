using Carina.Api.Common;
using Carina.Domain.Notifications;

namespace Carina.Api.Services;

public sealed record NotificationSettingsRead(int NotifyLeadMinutes, bool PublicOriginKnown);

/// <summary>
/// Reads and changes how long before a programme a notice of it is sent, and says whether links can be built.
/// </summary>
public sealed class NotificationSettingsService(
    INotificationSettingsRepository settings,
    NoticeLinkBase links,
    TimeProvider clock)
{
    private static readonly string BadLead
        = $"A notice of a programme is sent {string.Join(", ", NotificationSettings.LeadMinutesOffered)} minutes before it begins.";

    public async Task<ServiceResult<NotificationSettingsRead>> ReadAsync(CancellationToken cancellationToken)
        => ServiceResult<NotificationSettingsRead>.Success(
            Read(NotificationSettings.LeadMinutesOver(await settings.ReadAsync(cancellationToken))));

    public async Task<ServiceResult<NotificationSettingsRead, NotificationFailure>> ChangeAsync(
        int? notifyLeadMinutes,
        CancellationToken cancellationToken)
    {
        if (notifyLeadMinutes is not { } minutes || !NotificationSettings.Offers(minutes))
        {
            return ServiceResult<NotificationSettingsRead, NotificationFailure>.Failure(BadLead, NotificationFailure.Refused);
        }

        await settings.SaveAsync(NotificationSettings.Set(minutes, clock.GetUtcNow().UtcDateTime), cancellationToken);

        return ServiceResult<NotificationSettingsRead, NotificationFailure>.Success(Read(minutes));
    }

    private NotificationSettingsRead Read(int minutes) => new(minutes, links.IsKnown);
}
