using System.Buffers.Text;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
public sealed class PushSubscriptionTests
{
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/made-up-registration";

    private const string Key = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";

    private const string Secret = "BTBZMqHH6r4Tts7J_aSIgg";

    [Theory(DisplayName = "BR-NT-008: an https endpoint of one of the four push services on port 443 is taken")]
    [InlineData(Endpoint)]
    [InlineData("https://fcm.googleapis.com:443/fcm/send/made-up-registration")]
    [InlineData("https://FCM.GOOGLEAPIS.COM/fcm/send/made-up-registration")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/made-up-registration")]
    [InlineData("https://web.push.apple.com/made-up-registration")]
    [InlineData("https://made-up.notify.windows.com/w/?token=made-up-registration")]
    [InlineData("https://a.b.notify.windows.com/w/?token=made-up-registration")]
    public void AnEndpointOfAKnownPushServiceIsTaken(string endpoint)
    {
        Assert.NotNull(PushSubscription.Read(endpoint, Key, Secret));
    }

    [Theory(DisplayName = "BR-NT-008: any other endpoint is not taken")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fcm.googleapis.com/fcm/send/made-up")]
    [InlineData("/fcm/send/made-up")]
    [InlineData("http://fcm.googleapis.com/fcm/send/made-up")]
    [InlineData("wss://fcm.googleapis.com/fcm/send/made-up")]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/made-up")]
    [InlineData("https://fcm.googleapis.com:80/fcm/send/made-up")]
    [InlineData("https://someone@fcm.googleapis.com/fcm/send/made-up")]
    [InlineData("https://someone:secret@web.push.apple.com/made-up")]
    [InlineData("https://fcm.googleapis.com.evil.example/fcm/send/made-up")]
    [InlineData("https://evil.example/fcm.googleapis.com/fcm/send/made-up")]
    [InlineData("https://evil-fcm.googleapis.com/fcm/send/made-up")]
    [InlineData("https://fcm.googleapis.com./fcm/send/made-up")]
    [InlineData("https://googleapis.com/fcm/send/made-up")]
    [InlineData("https://push.services.mozilla.com/wpush/v2/made-up")]
    [InlineData("https://push.apple.com/made-up")]
    [InlineData("https://notify.windows.com/w/?token=made-up")]
    [InlineData("https://evilnotify.windows.com/w/?token=made-up")]
    [InlineData("https://made-up.notify.windows.com.evil.example/w/?token=made-up")]
    [InlineData("https://made-up.notify.windows.com./w/?token=made-up")]
    [InlineData("https://push.example.net/push/made-up")]
    [InlineData("https://[::ffff:10.20.30.40]/fcm/send/made-up")]
    [InlineData("https://10.20.30.40/fcm/send/made-up")]
    [InlineData("https://192.168.0.10/fcm/send/made-up")]
    [InlineData("https://127.0.0.1/fcm/send/made-up")]
    [InlineData("https://localhost/fcm/send/made-up")]
    [InlineData("https://fcm.googleapis.com\\@evil.example/fcm/send/made-up")]
    [InlineData("https://fcm.googleapis.com/fcm/send/made up")]
    [InlineData("https://fcm.googleapis.com/fcm/send/made-up\n")]
    [InlineData("https://fcm.googleapis.com/fcm/send/作り物")]
    [InlineData("https://fcm.googleapis.com/fcm/send/made-up#part")]
    public void AnyOtherEndpointIsNotTaken(string? endpoint)
    {
        Assert.Null(PushSubscription.Read(endpoint, Key, Secret));
    }

    [Fact(DisplayName = "BR-NT-008: an endpoint longer than the ledger keeps is not taken")]
    public void AnEndpointTooLongIsNotTaken()
    {
        string head = "https://fcm.googleapis.com/fcm/send/";

        Assert.NotNull(PushSubscription.Read(head + new string('a', PushSubscription.LongestEndpoint - head.Length), Key, Secret));
        Assert.Null(PushSubscription.Read(head + new string('a', PushSubscription.LongestEndpoint - head.Length + 1), Key, Secret));
    }

    [Theory(DisplayName = "BR-NT-008: a key that is not an uncompressed point of 65 bytes in base64url is not taken")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw")]
    [InlineData("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4A")]
    [InlineData("BCVxsr7N/eNgVRqvHtD0zTZsEc6+VV+JvLexhqUzORcxaOzi6+AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4")]
    [InlineData("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4=")]
    [InlineData("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw 4")]
    [InlineData("AiVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcx")]
    public void AKeyOfAnotherShapeIsNotTaken(string? key)
    {
        Assert.Null(PushSubscription.Read(Endpoint, key, Secret));
    }

    [Fact(DisplayName = "BR-NT-008: a key of 65 bytes that is no point of P-256 is not taken")]
    public void AKeyThatIsNoPointOfTheCurveIsNotTaken()
    {
        byte[] point = Base64Url.DecodeFromChars(Key);
        byte[] moved = [.. point];
        moved[^1] ^= 0x01;
        byte[] compressedMark = [.. point];
        compressedMark[0] = 0x02;
        byte[] beyondTheField = [0x04, .. Enumerable.Repeat((byte)0xFF, 64)];
        byte[] atInfinity = [0x04, .. new byte[64]];

        Assert.NotNull(PushSubscription.Read(Endpoint, Base64Url.EncodeToString(point), Secret));
        Assert.Null(PushSubscription.Read(Endpoint, Base64Url.EncodeToString(moved), Secret));
        Assert.Null(PushSubscription.Read(Endpoint, Base64Url.EncodeToString(compressedMark), Secret));
        Assert.Null(PushSubscription.Read(Endpoint, Base64Url.EncodeToString(beyondTheField), Secret));
        Assert.Null(PushSubscription.Read(Endpoint, Base64Url.EncodeToString(atInfinity), Secret));
    }

    [Fact(DisplayName = "BR-NT-008: a key a browser has just made is taken")]
    public void AKeyJustMadeIsTaken()
    {
        using ECDiffieHellman receiver = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        ECParameters made = receiver.ExportParameters(includePrivateParameters: false);
        byte[] point = [0x04, .. made.Q.X!, .. made.Q.Y!];

        Assert.NotNull(PushSubscription.Read(Endpoint, Base64Url.EncodeToString(point), Secret));
    }

    [Theory(DisplayName = "BR-NT-008: a secret that is not 16 bytes in base64url is not taken")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BTBZMqHH6r4Tts7J_aSI")]
    [InlineData("BTBZMqHH6r4Tts7J_aSIggAA")]
    [InlineData("BTBZMqHH6r4Tts7J/aSIgg")]
    [InlineData("BTBZMqHH6r4Tts7J_aSIgg==")]
    [InlineData("BTBZMqHH6r4Tts7J_aSIg g")]
    public void ASecretOfAnotherShapeIsNotTaken(string? secret)
    {
        Assert.Null(PushSubscription.Read(Endpoint, Key, secret));
    }

    [Fact(DisplayName = "BR-ND-015: the fingerprint of a subscription is the SHA-256 of its endpoint as written, in lowercase hex")]
    public void TheFingerprintIsTheHashOfTheEndpoint()
    {
        PushSubscription subscription = PushSubscription.Read(Endpoint, Key, Secret)!;
        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Endpoint)));

        Assert.Equal(expected, subscription.Fingerprint);
        Assert.Equal(NotificationDestination.EndpointHashLength, subscription.Fingerprint.Length);
        Assert.Equal(expected[^NotificationDestination.UrlTailLength..], subscription.Tail);
    }

    [Fact(DisplayName = "BR-ND-015: two subscriptions of one endpoint have one fingerprint, whatever their keys")]
    public void TwoSubscriptionsOfOneEndpointHaveOneFingerprint()
    {
        using ECDiffieHellman receiver = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        ECParameters made = receiver.ExportParameters(includePrivateParameters: false);
        byte[] point = [0x04, .. made.Q.X!, .. made.Q.Y!];
        PushSubscription first = PushSubscription.Read(Endpoint, Key, Secret)!;
        PushSubscription again = PushSubscription.Read(Endpoint, Base64Url.EncodeToString(point), Secret)!;
        PushSubscription elsewhere = PushSubscription.Read(Endpoint + "-2", Key, Secret)!;

        Assert.Equal(first.Fingerprint, again.Fingerprint);
        Assert.NotEqual(first.Fingerprint, elsewhere.Fingerprint);
    }

    [Theory(DisplayName = "the audience of a subscription is the origin of its push service")]
    [InlineData(Endpoint, "https://fcm.googleapis.com")]
    [InlineData("https://FCM.GOOGLEAPIS.COM:443/fcm/send/made-up-registration", "https://fcm.googleapis.com")]
    [InlineData("https://made-up.notify.windows.com/w/?token=made-up-registration", "https://made-up.notify.windows.com")]
    public void TheAudienceIsTheOriginOfThePushService(string endpoint, string audience)
    {
        Assert.Equal(audience, PushSubscription.Read(endpoint, Key, Secret)!.Audience);
    }

    [Fact(DisplayName = "BR-NA-003: a subscription written out says nothing of itself")]
    public void ASubscriptionWrittenOutSaysNothingOfItself()
    {
        PushSubscription subscription = PushSubscription.Read(Endpoint, Key, Secret)!;

        Assert.Equal("(push subscription)", subscription.ToString());
        Assert.Equal("(push subscription)", $"{subscription}");
    }

    [Fact(DisplayName = "BR-NA-003: the endpoint and the keys are read in the clear through three members only")]
    public void TheEndpointAndTheKeysAreReadThroughThreeMembersOnly()
    {
        string[] telling =
        [
            .. typeof(PushSubscription).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal),
        ];
        PushSubscription subscription = PushSubscription.Read(Endpoint, Key, Secret)!;

        Assert.Equal(
            [
                nameof(PushSubscription.Audience),
                nameof(PushSubscription.EndpointAsWritten),
                nameof(PushSubscription.Fingerprint),
                nameof(PushSubscription.KeyAsWritten),
                nameof(PushSubscription.SecretAsWritten),
                nameof(PushSubscription.Tail),
            ],
            telling);
        Assert.Equal(Endpoint, subscription.EndpointAsWritten);
        Assert.Equal(Key, subscription.KeyAsWritten);
        Assert.Equal(Secret, subscription.SecretAsWritten);
    }

    [Fact(DisplayName = "BR-NA-003: a subscription is not compared by what it holds, so it is no key to look one up by")]
    public void TwoSubscriptionsAreNotEqualByWhatTheyHold()
    {
        Assert.False(typeof(PushSubscription).IsAssignableTo(typeof(IEquatable<PushSubscription>)));
    }
}
