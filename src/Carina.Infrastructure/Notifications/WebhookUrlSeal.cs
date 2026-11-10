using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Notifications;

using Microsoft.AspNetCore.DataProtection;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// Seals the address of a webhook for the database, and opens it again for the moment of sending.
/// </summary>
public sealed class WebhookUrlSeal(IDataProtectionProvider provider)
{
    public const string Purpose = "Carina.Notifications.WebhookUrl";

    private readonly IDataProtector protector = provider.CreateProtector(Purpose);

    public byte[] Seal(WebhookUrl webhook)
    {
        ArgumentNullException.ThrowIfNull(webhook);

        return protector.Protect(Encoding.UTF8.GetBytes(webhook.AddressAsWritten));
    }

    /// <summary>
    /// The address that was sealed, or nothing when the keys that sealed it are gone.
    /// </summary>
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
