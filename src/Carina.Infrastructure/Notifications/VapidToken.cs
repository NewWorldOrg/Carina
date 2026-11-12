using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// The token a server shows a push service to say who is sending (VAPID, RFC 8292): a JWT signed with ES256 that
/// names the push service's origin, when the token stops being good, and optionally the sender.
/// </summary>
public static class VapidToken
{
    private const int KeySizeInBits = 256;

    private static readonly string Header = Base64Url.EncodeToString("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"u8);

    public static string Write(ECDsa key, string audience, string? subject, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrEmpty(audience);

        if (key.KeySize is not KeySizeInBits)
        {
            throw new ArgumentException("A VAPID token is signed with a key on P-256.", nameof(key));
        }

        string unsigned = $"{Header}.{Base64Url.EncodeToString(Claims(audience, subject, expiresAt))}";
        byte[] signature = key.SignData(
            Encoding.ASCII.GetBytes(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{unsigned}.{Base64Url.EncodeToString(signature)}";
    }

    private static ReadOnlySpan<byte> Claims(string audience, string? subject, DateTimeOffset expiresAt)
    {
        ArrayBufferWriter<byte> written = new();

        using (Utf8JsonWriter json = new(written))
        {
            json.WriteStartObject();
            json.WriteString("aud", audience);
            json.WriteNumber("exp", expiresAt.ToUnixTimeSeconds());

            if (subject is not null)
            {
                json.WriteString("sub", subject);
            }

            json.WriteEndObject();
        }

        return written.WrittenSpan;
    }
}
