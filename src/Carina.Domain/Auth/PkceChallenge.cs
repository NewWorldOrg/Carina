using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Carina.Domain.Auth;

public sealed class PkceChallenge
{
    public const string Method = "S256";

    public const int ShortestVerifier = 43;

    public const int LongestVerifier = 128;

    private PkceChallenge(string verifier)
    {
        Verifier = verifier;
        Challenge = ChallengeFor(verifier);
    }

    public string Verifier { get; }

    public string Challenge { get; }

    public static PkceChallenge Issue() => new(Unguessable.Issue());

    public static PkceChallenge From(string verifier) => new(Validated(verifier));

    public static string ChallengeFor(string verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);

        return Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    private static string Validated(string verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);

        if (verifier.Length < ShortestVerifier || verifier.Length > LongestVerifier)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verifier),
                verifier.Length,
                $"A code verifier is between {ShortestVerifier} and {LongestVerifier} characters.");
        }

        if (!verifier.All(IsVerifierCharacter))
        {
            throw new ArgumentException(
                "A code verifier travels in a form body, so it is drawn from the unreserved characters only.",
                nameof(verifier));
        }

        return verifier;
    }

    private static bool IsVerifierCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~';
}
