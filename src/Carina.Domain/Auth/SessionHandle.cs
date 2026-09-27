using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Base;

namespace Carina.Domain.Auth;

/// <summary>
/// Names a session in the store, in the session list and in the request that ends it: the base64url
/// SHA-256 of its session id.
/// </summary>
public sealed class SessionHandle : CommonValueObject<string>
{
    public const int Length = 43;

    public SessionHandle(string value)
        : base(Validated(value))
    {
    }

    public static SessionHandle Of(SessionId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return new SessionHandle(Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(id.Value))));
    }

    private static string Validated(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length != Length)
        {
            throw new ArgumentException(
                $"A session handle is the {Length} characters a derived one has.",
                nameof(value));
        }

        if (!value.All(IsDerivedCharacter))
        {
            throw new ArgumentException(
                "A session handle travels in a URL, so it is base64url and nothing else.",
                nameof(value));
        }

        return value;
    }

    private static bool IsDerivedCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
