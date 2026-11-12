using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;

using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Unit.Infra.Notifications;

[Trait("Category", "Unit")]
public sealed class VapidTokenTests
{
    private const string Rfc8292Unsigned =
        "eyJ0eXAiOiJKV1QiLCJhbGciOiJFUzI1NiJ9.eyJhdWQiOiJodHRwczovL3B1c2guZXhhbXBsZS5uZXQiLCJleHAiOjE0NTM1MjM3NjgsInN1YiI6Im1haWx0bzpwdXNoQGV4YW1wbGUuY29tIn0";

    private const string Rfc8292Signature =
        "i3CYb7t4xfxCDquptFOepC9GAu_HLGkMlMuCGSK2rpiUfnK9ojFwDXb1JrErtmysazNjjvW2L9OkSSHzvoD1oA";

    private const string Rfc8292Key =
        "BA1Hxzyi1RUM1b5wjxsn7nGxAszw2u61m164i3MrAIxHF6YK5h4SDYic-dRuU_RCPCfA5aq9ojSwk5Y2EmClBPs";

    private static readonly DateTimeOffset Rfc8292Expiry = DateTimeOffset.FromUnixTimeSeconds(1453523768);

    [Fact(DisplayName = "RFC 8292 section 2.4: the reading these tests check tokens with accepts the RFC's own example token under the RFC's key")]
    public void TheExampleTokenOfRfc8292IsSignedByItsKey()
    {
        VapidReading read = VapidReading.Of($"{Rfc8292Unsigned}.{Rfc8292Signature}", Base64Url.DecodeFromChars(Rfc8292Key));

        Assert.True(read.Signed);
    }

    [Fact(DisplayName = "RFC 8292 section 2.4: the example's audience, expiry and subject give the example's header and claims, character for character")]
    public void TheExampleOfRfc8292ComesOutCharacterForCharacter()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        string token = VapidToken.Write(key, "https://push.example.net", "mailto:push@example.com", Rfc8292Expiry);

        Assert.Equal(Rfc8292Unsigned, token[..token.LastIndexOf('.')]);
    }

    [Fact(DisplayName = "RFC 8292 section 2: the token is a JWT signed with ES256 by the key it is written with, and no other key")]
    public void TheTokenIsSignedWithEs256ByItsKey()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa another = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        string token = VapidToken.Write(key, "https://push.example.net", "https://tv.example.net", Rfc8292Expiry);

        VapidReading read = VapidReading.Of(token, PointOf(key));
        Assert.True(read.Signed);
        Assert.Equal("JWT", read.Header.GetProperty("typ").GetString());
        Assert.Equal("ES256", read.Header.GetProperty("alg").GetString());
        Assert.Equal(64, Base64Url.DecodeFromChars(token.Split('.')[2]).Length);
        Assert.False(VapidReading.Of(token, PointOf(another)).Signed);
    }

    [Fact(DisplayName = "RFC 8292 section 2: the claims name the push service's origin as aud, the expiry in seconds as exp, and the sender as sub")]
    public void TheClaimsNameTheAudienceTheExpiryAndTheSender()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        string token = VapidToken.Write(key, "https://push.example.net", "https://tv.example.net", Rfc8292Expiry);

        JsonElement claims = VapidReading.Of(token, PointOf(key)).Claims;
        Assert.Equal("https://push.example.net", claims.GetProperty("aud").GetString());
        Assert.Equal(1453523768, claims.GetProperty("exp").GetInt64());
        Assert.Equal("https://tv.example.net", claims.GetProperty("sub").GetString());
        Assert.Equal(3, claims.EnumerateObject().Count());
    }

    [Fact(DisplayName = "RFC 8292 section 2.2: a token written without a sender carries no sub at all")]
    public void ATokenWithoutASenderCarriesNoSub()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        string token = VapidToken.Write(key, "https://push.example.net", null, Rfc8292Expiry);

        JsonElement claims = VapidReading.Of(token, PointOf(key)).Claims;
        Assert.False(claims.TryGetProperty("sub", out _));
        Assert.Equal(2, claims.EnumerateObject().Count());
    }

    [Fact(DisplayName = "RFC 8292 section 2: every part of the token is base64url without padding")]
    public void EveryPartIsBase64UrlWithoutPadding()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        string token = VapidToken.Write(key, "https://push.example.net", "https://tv.example.net", Rfc8292Expiry);

        Assert.Equal(3, token.Split('.').Length);
        Assert.All(token.Split('.'), part => Assert.Matches("^[A-Za-z0-9_-]+$", part));
    }

    [Fact(DisplayName = "RFC 8292 section 2: a key that is not on P-256 writes no token")]
    public void AKeyOnAnotherCurveWritesNoToken()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.ThrowsAny<ArgumentException>(
            () => VapidToken.Write(key, "https://push.example.net", null, Rfc8292Expiry));
    }

    private static byte[] PointOf(ECDsa key)
    {
        ECPoint point = key.ExportParameters(includePrivateParameters: false).Q;

        return [0x04, .. point.X!, .. point.Y!];
    }
}
