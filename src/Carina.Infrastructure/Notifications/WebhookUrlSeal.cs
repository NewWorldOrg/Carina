using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Notifications;

using Microsoft.AspNetCore.DataProtection;

namespace Carina.Infrastructure.Notifications;

public sealed class WebhookUrlSeal(IDataProtectionProvider provider)
{
    public const string Purpose = "Carina.Notifications.WebhookUrl";

    private readonly IDataProtector protector = provider.CreateProtector(Purpose);

    public byte[] Seal(WebhookUrl webhook)
    {
        ArgumentNullException.ThrowIfNull(webhook);

        return protector.Protect(Encoding.UTF8.GetBytes(webhook.AddressAsWritten));
    }

    public WebhookUrl? Open(byte[] held)
    {
        ArgumentNullException.ThrowIfNull(held);

        try
        {
            return WebhookUrl.Read(Encoding.UTF8.GetString(protector.Unprotect(held)));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
