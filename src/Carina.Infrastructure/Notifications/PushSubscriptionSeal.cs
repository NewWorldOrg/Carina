using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Notifications;

using Microsoft.AspNetCore.DataProtection;

namespace Carina.Infrastructure.Notifications;

public sealed class PushSubscriptionSeal(IDataProtectionProvider provider)
{
    public const string Purpose = "Carina.Notifications.PushSubscription";

    private const char Between = '\n';

    private const int Parts = 3;

    private readonly IDataProtector protector = provider.CreateProtector(Purpose);

    public byte[] Seal(PushSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return protector.Protect(Encoding.UTF8.GetBytes(string.Join(
            Between,
            subscription.EndpointAsWritten,
            subscription.KeyAsWritten,
            subscription.SecretAsWritten)));
    }

    public PushSubscription? Open(byte[] held)
    {
        ArgumentNullException.ThrowIfNull(held);

        try
        {
            string[] written = Encoding.UTF8.GetString(protector.Unprotect(held)).Split(Between);

            return written.Length is Parts ? PushSubscription.Read(written[0], written[1], written[2]) : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
