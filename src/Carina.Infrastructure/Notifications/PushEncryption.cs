using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// The message encryption of Web Push (RFC 8291, the <c>aes128gcm</c> content coding of RFC 8188): what is written
/// is sealed so that only the browser whose key and secret are given reads it. A message is always one record.
/// </summary>
public static class PushEncryption
{
    public const int RecordSize = 4096;

    public const int LongestContent = RecordSize - HeaderLength - 1 - TagLength;

    private const int SaltLength = 16;

    private const int KeyLength = 65;

    private const int SecretLength = 16;

    private const int TagLength = 16;

    private const int HeaderLength = SaltLength + 4 + 1 + KeyLength;

    private const byte LastRecord = 0x02;

    private static readonly HashAlgorithmName Hash = HashAlgorithmName.SHA256;

    public static byte[] Encrypt(byte[] content, byte[] receiverKey, byte[] secret)
    {
        using ECDiffieHellman sender = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        return Encrypt(content, receiverKey, secret, RandomNumberGenerator.GetBytes(SaltLength), sender);
    }

    public static byte[] Encrypt(byte[] content, byte[] receiverKey, byte[] secret, byte[] salt, ECDiffieHellman sender)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(receiverKey);
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(content.Length, LongestContent, nameof(content));
        ArgumentOutOfRangeException.ThrowIfNotEqual(receiverKey.Length, KeyLength, nameof(receiverKey));
        ArgumentOutOfRangeException.ThrowIfNotEqual(secret.Length, SecretLength, nameof(secret));
        ArgumentOutOfRangeException.ThrowIfNotEqual(salt.Length, SaltLength, nameof(salt));

        byte[] senderKey = PointOf(sender);
        byte[] shared = SharedWith(sender, receiverKey);
        byte[] keyInfo = [.. "WebPush: info\0"u8, .. receiverKey, .. senderKey];
        byte[] keyingMaterial = HKDF.DeriveKey(Hash, shared, 32, secret, keyInfo);
        byte[] pseudoRandomKey = HKDF.Extract(Hash, keyingMaterial, salt);
        byte[] contentKey = HKDF.Expand(Hash, pseudoRandomKey, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        byte[] nonce = HKDF.Expand(Hash, pseudoRandomKey, 12, "Content-Encoding: nonce\0"u8.ToArray());

        byte[] padded = [.. content, LastRecord];
        byte[] message = new byte[HeaderLength + padded.Length + TagLength];
        salt.CopyTo(message, 0);
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(SaltLength, 4), RecordSize);
        message[SaltLength + 4] = KeyLength;
        senderKey.CopyTo(message, SaltLength + 5);

        using AesGcm cipher = new(contentKey, TagLength);
        cipher.Encrypt(nonce, padded, message.AsSpan(HeaderLength, padded.Length), message.AsSpan(HeaderLength + padded.Length));

        return message;
    }

    private static byte[] PointOf(ECDiffieHellman pair)
    {
        ECPoint point = pair.ExportParameters(includePrivateParameters: false).Q;

        return [0x04, .. point.X!, .. point.Y!];
    }

    private static byte[] SharedWith(ECDiffieHellman sender, byte[] receiverKey)
    {
        if (receiverKey[0] is not 0x04)
        {
            throw new CryptographicException("The receiver's key is not an uncompressed point.");
        }

        using ECDiffieHellman receiver = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = receiverKey[1..33], Y = receiverKey[33..65] },
        });

        return sender.DeriveRawSecretAgreement(receiver.PublicKey);
    }
}
