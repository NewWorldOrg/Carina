using Carina.Api.Services;
using Carina.Domain.Notifications;

namespace Carina.Api.Responder.Notifications;

public sealed record NoticeTrialResponder(bool Delivered, NoticeFailure? Failure)
{
    public static NoticeTrialResponder Of(NoticeTrialOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new NoticeTrialResponder(outcome.Delivered, outcome.Failure);
    }
}
