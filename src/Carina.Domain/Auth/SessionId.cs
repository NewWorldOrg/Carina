using System.Buffers.Text;
using System.Security.Cryptography;

using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed class SessionId : CommonValueObject<string>
{
    public const int Bytes = 32;

    public const int Length = 43;

    public SessionId(string value)
        : base(Validated(value))
    {
    }

    public static SessionId Issue() => new(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Bytes)));

    private static string Validated(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length != Length)
        {
            throw new ArgumentException(
                $"A session id is the {Length} characters an issued one has.",
                nameof(value));
        }

        if (!value.All(IsIssuedCharacter))
        {
            throw new ArgumentException(
                "A session id travels in a cookie and a URL, so it is base64url and nothing else.",
                nameof(value));
        }

        return value;
    }

    private static bool IsIssuedCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
