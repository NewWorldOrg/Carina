using Carina.Api.Services;

namespace Carina.Api.Responder.Notifications;

public sealed record PushKeyResponder(string PublicKey)
{
    public static PushKeyResponder Of(PushKeyHeld held)
    {
        ArgumentNullException.ThrowIfNull(held);

        return new PushKeyResponder(held.PublicKey);
    }
}
