using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Carina.Domain.Auth;

public static class Unguessable
{
    public const int Bytes = 32;

    public const int Length = 43;

    public static string Issue() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Bytes));

    public static string Validated(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);

        if (value.Length != Length || !value.All(IsIssuedCharacter))
        {
            throw new ArgumentException(
                $"An unguessable value is the {Length} base64url characters an issued one has.",
                name);
        }

        return value;
    }

    public static bool IsOne(string? value)
        => value is not null && value.Length == Length && value.All(IsIssuedCharacter);

    public static bool Same(string? held, string? offered)
        => held is not null
           && offered is not null
           && CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(held),
               Encoding.UTF8.GetBytes(offered));

    private static bool IsIssuedCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
