using System.Buffers.Text;
using System.Security.Cryptography;

namespace Carina.Domain.Auth;

public static class FirstCredentials
{
    public const string Username = "carina";

    public const int PasswordBytes = 24;

    public static string MakePassword() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(PasswordBytes));
}
