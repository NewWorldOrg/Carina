using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace Carina.TestSupport;

/// <summary>
/// A browser's end of a push subscription, with keys made for one test: it hands out what a browser would register,
/// and opens a message encrypted to it the way RFC 8291 has the receiver do.
/// </summary>
public sealed class PushReceiver : IDisposable
{
    private const int SaltLength = 16;

    private const int KeyLength = 65;

    private const int TagLength = 16;

    private const byte LastRecord = 0x02;

    private readonly ECDiffieHellman pair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public PushReceiver()
    {
        ECPoint point = pair.ExportParameters(includePrivateParameters: false).Q;

        Key = [0x04, .. point.X!, .. point.Y!];
    }

    public byte[] Key { get; }

    public byte[] Secret { get; } = RandomNumberGenerator.GetBytes(16);

    public string KeyAsWritten => Base64Url.EncodeToString(Key);

    public string SecretAsWritten => Base64Url.EncodeToString(Secret);

    public byte[] Open(byte[] message)
    {
        ArgumentNullException.ThrowIfNull(message);

        ReadOnlySpan<byte> salt = message.AsSpan(0, SaltLength);
        int recordSize = (int)BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(SaltLength, 4));
        int keyLength = message[SaltLength + 4];
        byte[] senderKey = message.AsSpan(SaltLength + 5, keyLength).ToArray();
        ReadOnlySpan<byte> record = message.AsSpan(SaltLength + 5 + keyLength);

        if (keyLength is not KeyLength || message.Length > recordSize)
        {
            throw new CryptographicException("The message is not one record that names the sender's P-256 key.");
        }

        byte[] shared = pair.DeriveRawSecretAgreement(PublicKeyOf(senderKey));
        byte[] keyInfo = [.. "WebPush: info\0"u8, .. Key, .. senderKey];
        byte[] keyingMaterial = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, Secret, keyInfo);
        byte[] pseudoRandomKey = HKDF.Extract(HashAlgorithmName.SHA256, keyingMaterial, salt.ToArray());
        byte[] contentKey = HKDF.Expand(HashAlgorithmName.SHA256, pseudoRandomKey, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        byte[] nonce = HKDF.Expand(HashAlgorithmName.SHA256, pseudoRandomKey, 12, "Content-Encoding: nonce\0"u8.ToArray());
        byte[] padded = new byte[record.Length - TagLength];

        using AesGcm cipher = new(contentKey, TagLength);
        cipher.Decrypt(nonce, record[..^TagLength], record[^TagLength..], padded);

        int delimiter = Array.FindLastIndex(padded, octet => octet is not 0);

        if (delimiter < 0 || padded[delimiter] is not LastRecord)
        {
            throw new CryptographicException("The record does not end as the last record of a message.");
        }

        return padded[..delimiter];
    }

    public void Dispose() => pair.Dispose();

    private static ECDiffieHellmanPublicKey PublicKeyOf(byte[] point)
    {
        using ECDiffieHellman other = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = point[1..33], Y = point[33..65] },
        });

        return other.PublicKey;
    }
}
