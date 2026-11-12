using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Notifications;

public sealed class PushEncryptionTests
{
    private const string Rfc8291Plaintext = "When I grow up, I want to be a watermelon";

    private const string Rfc8291SenderPublic = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";

    private const string Rfc8291SenderPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";

    private const string Rfc8291ReceiverPublic = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";

    private const string Rfc8291Salt = "DGv6ra1nlYgDCS1FRnbzlw";

    private const string Rfc8291AuthSecret = "BTBZMqHH6r4Tts7J_aSIgg";

    private const string Rfc8291Message =
        "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

    [Fact(DisplayName = "RFC 8291 section 5: the example's keys, salt and plaintext give the example's message, octet for octet")]
    public void TheExampleOfRfc8291ComesOutOctetForOctet()
    {
        byte[] senderPublic = Base64Url.DecodeFromChars(Rfc8291SenderPublic);

        using ECDiffieHellman sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.DecodeFromChars(Rfc8291SenderPrivate),
            Q = new ECPoint { X = senderPublic[1..33], Y = senderPublic[33..65] },
        });

        byte[] message = PushEncryption.Encrypt(
            Encoding.UTF8.GetBytes(Rfc8291Plaintext),
            Base64Url.DecodeFromChars(Rfc8291ReceiverPublic),
            Base64Url.DecodeFromChars(Rfc8291AuthSecret),
            Base64Url.DecodeFromChars(Rfc8291Salt),
            sender);

        Assert.Equal(Rfc8291Message, Base64Url.EncodeToString(message));
    }

    [Theory(DisplayName = "RFC 8291: the browser the message is for opens it and reads what was written, whatever its length up to the longest")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(41)]
    [InlineData(3000)]
    [InlineData(PushEncryption.LongestContent)]
    public void TheBrowserTheMessageIsForReadsWhatWasWritten(int length)
    {
        using PushReceiver browser = new();
        byte[] written = RandomNumberGenerator.GetBytes(length);

        byte[] message = PushEncryption.Encrypt(written, browser.Key, browser.Secret);

        Assert.Equal(written, browser.Open(message));
    }

    [Fact(DisplayName = "RFC 8188 section 2.1: a message opens with a salt of 16 octets, a record size of 4096, and the sender's key of 65 octets, and is one record")]
    public void AMessageOpensWithItsSaltItsRecordSizeAndTheSendersKey()
    {
        using PushReceiver browser = new();
        byte[] written = RandomNumberGenerator.GetBytes(PushEncryption.LongestContent);

        byte[] message = PushEncryption.Encrypt(written, browser.Key, browser.Secret);

        Assert.Equal(4096u, BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(16, 4)));
        Assert.Equal(65, message[20]);
        Assert.Equal(0x04, message[21]);
        Assert.Equal(16 + 4 + 1 + 65 + written.Length + 1 + 16, message.Length);
        Assert.Equal(4096, message.Length);
    }

    [Fact(DisplayName = "RFC 8291 section 3.1: every message is sealed under a salt and a sender's key of its own")]
    public void EveryMessageHasASaltAndASendersKeyOfItsOwn()
    {
        using PushReceiver browser = new();
        byte[] written = Encoding.UTF8.GetBytes(Rfc8291Plaintext);

        byte[] first = PushEncryption.Encrypt(written, browser.Key, browser.Secret);
        byte[] second = PushEncryption.Encrypt(written, browser.Key, browser.Secret);

        Assert.NotEqual(first[..16], second[..16]);
        Assert.NotEqual(first[21..86], second[21..86]);
        Assert.NotEqual(first[86..], second[86..]);
    }

    [Fact(DisplayName = "RFC 8291: a browser that holds another secret or another key does not open the message")]
    public void AnotherBrowserDoesNotOpenTheMessage()
    {
        using PushReceiver browser = new();
        using PushReceiver another = new();
        byte[] written = Encoding.UTF8.GetBytes(Rfc8291Plaintext);

        byte[] message = PushEncryption.Encrypt(written, browser.Key, browser.Secret);
        byte[] withAnothersSecret = PushEncryption.Encrypt(written, browser.Key, another.Secret);

        Assert.ThrowsAny<CryptographicException>(() => another.Open(message));
        Assert.ThrowsAny<CryptographicException>(() => browser.Open(withAnothersSecret));
    }

    [Theory(DisplayName = "RFC 8291: content too long for one record, a key that is not 65 octets and a secret that is not 16 are refused")]
    [InlineData(PushEncryption.LongestContent + 1, 65, 16)]
    [InlineData(10, 64, 16)]
    [InlineData(10, 66, 16)]
    [InlineData(10, 65, 15)]
    [InlineData(10, 65, 17)]
    public void WhatDoesNotFitTheSchemeIsRefused(int length, int keyLength, int secretLength)
    {
        using PushReceiver browser = new();
        byte[] key = Sized(browser.Key, keyLength);
        byte[] secret = Sized(browser.Secret, secretLength);

        Assert.ThrowsAny<ArgumentException>(() => PushEncryption.Encrypt(new byte[length], key, secret));
    }

    [Fact(DisplayName = "RFC 8291: a key of 65 octets that is not a point of P-256 is refused rather than sealed to")]
    public void AKeyThatIsNotAPointOfTheCurveIsRefused()
    {
        using PushReceiver browser = new();
        byte[] key = [.. browser.Key];
        key[64] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => PushEncryption.Encrypt(new byte[10], key, browser.Secret));
    }

    private static byte[] Sized(byte[] from, int length)
    {
        byte[] sized = new byte[length];
        from.AsSpan(0, Math.Min(from.Length, length)).CopyTo(sized);

        return sized;
    }
}
