using System.Security.Cryptography;

using Carina.Domain.Auth;

using Microsoft.AspNetCore.DataProtection;

namespace Carina.Infrastructure.Auth;

public sealed class ClientSecretSeal(IDataProtectionProvider provider)
{
    public const string Purpose = "Carina.Auth.OidcClientSecret";

    private readonly IDataProtector protector = provider.CreateProtector(Purpose);

    public string Seal(ClientSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return protector.Protect(secret.Value);
    }

    public ClientSecret? Open(string held)
    {
        ArgumentNullException.ThrowIfNull(held);

        try
        {
            return new ClientSecret(protector.Unprotect(held));
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
