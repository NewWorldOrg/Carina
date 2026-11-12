using System.Buffers.Text;
using System.Security.Cryptography;

using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Unit.Infra.Notifications;

[Trait("Category", "Unit")]
public sealed class PushServerKeyTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(Path.GetTempPath(), $"carina-push-key-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "the server's key is not made until it is first needed, and is then kept beside the sealing keys")]
    public void TheKeyIsMadeWhenFirstNeededAndKeptBesideTheSealingKeys()
    {
        PushServerKey key = Key();

        Assert.False(File.Exists(KeyFile()));

        string published = key.PublicKey();

        Assert.True(File.Exists(KeyFile()));
        byte[] point = Base64Url.DecodeFromChars(published);
        Assert.Equal(65, point.Length);
        Assert.Equal(0x04, point[0]);
        Assert.Matches("^[A-Za-z0-9_-]{87}$", published);
    }

    [Fact(DisplayName = "the server's key stays the same for as long as its file is kept: asked again, and after a restart")]
    public void TheKeyStaysTheSameAcrossARestart()
    {
        PushServerKey first = Key();
        string published = first.PublicKey();

        Assert.Equal(published, first.PublicKey());
        Assert.Equal(published, Key().PublicKey());
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact(DisplayName = "the server's key is made once even when many ask for it at the same moment")]
    public void TheKeyIsMadeOnceWhenManyAskAtOnce()
    {
        PushServerKey shared = Key();
        string[] published = new string[32];

        Parallel.For(0, published.Length, index =>
        {
            PushServerKey asked = index % 2 is 0 ? shared : Key();
            published[index] = asked.PublicKey();
        });

        Assert.Single(published.Distinct(StringComparer.Ordinal));
        Assert.Equal(published[0], Key().PublicKey());
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact(DisplayName = "the server's key is kept as a PKCS#8 private key on P-256 that only its owner reads")]
    public void TheKeyIsKeptAsAPrivateKeyOnlyItsOwnerReads()
    {
        string published = Key().PublicKey();

        using ECDsa kept = ECDsa.Create();
        kept.ImportFromPem(File.ReadAllText(KeyFile()));
        ECPoint point = kept.ExportParameters(includePrivateParameters: false).Q;

        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", File.ReadAllText(KeyFile()), StringComparison.Ordinal);
        Assert.Equal(published, Base64Url.EncodeToString([0x04, .. point.X!, .. point.Y!]));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyFile()));
        }
    }

    [Fact(DisplayName = "a key file that cannot be read is an error, and is left as it is rather than replaced with a new key")]
    public void AKeyFileThatCannotBeReadIsLeftAsItIs()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(KeyFile(), "not a key");

        Assert.Throws<InvalidOperationException>(() => Key().PublicKey());
        Assert.Equal("not a key", File.ReadAllText(KeyFile()));
    }

    [Fact(DisplayName = "a key file on another curve is an error, and is left as it is")]
    public void AKeyFileOnAnotherCurveIsAnError()
    {
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        string written = other.ExportPkcs8PrivateKeyPem();
        Directory.CreateDirectory(directory);
        File.WriteAllText(KeyFile(), written);

        Assert.Throws<InvalidOperationException>(() => Key().PublicKey());
        Assert.Equal(written, File.ReadAllText(KeyFile()));
    }

    [Fact(DisplayName = "RFC 8292 section 3: the Authorization header uses the vapid scheme with the token as t and the server's public key as k, and that key signed the token")]
    public void TheAuthorizationNamesTheTokenAndTheKeyThatSignedIt()
    {
        PushServerKey key = Key();

        string authorization = key.AuthorizationFor("https://push.example.net", "https://tv.example.net", Now);

        VapidReading read = VapidReading.Of(authorization);
        Assert.Matches("^vapid t=[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+, k=[A-Za-z0-9_-]{87}$", authorization);
        Assert.True(read.Signed);
        Assert.Equal(key.PublicKey(), Base64Url.EncodeToString(read.Key));
        Assert.Equal("https://push.example.net", read.Claims.GetProperty("aud").GetString());
        Assert.Equal("https://tv.example.net", read.Claims.GetProperty("sub").GetString());
    }

    [Fact(DisplayName = "RFC 8292 section 2: the token expires after the request and no more than 24 hours after it")]
    public void TheTokenExpiresWithinADayOfTheRequest()
    {
        string authorization = Key().AuthorizationFor("https://push.example.net", null, Now);

        long expiry = VapidReading.Of(authorization).Claims.GetProperty("exp").GetInt64();
        Assert.InRange(expiry, Now.ToUnixTimeSeconds() + 1, Now.AddHours(24).ToUnixTimeSeconds());
        Assert.Equal(Now.Add(PushServerKey.TokenLifetime).ToUnixTimeSeconds(), expiry);
    }

    [Fact(DisplayName = "the sealing keys kept in the same directory still seal and open with the server's key beside them")]
    public void TheSealingKeysStillWorkWithTheServerKeyBesideThem()
    {
        _ = Key().PublicKey();

        using ServiceProvider services = new ServiceCollection()
            .AddDataProtection()
            .SetApplicationName(SealingKeyOptions.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(directory))
            .Services
            .BuildServiceProvider();
        IDataProtector protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("a test");

        Assert.Equal("kept", protector.Unprotect(protector.Protect("kept")));
        Assert.Contains(Directory.GetFiles(directory), file => file.EndsWith(".xml", StringComparison.Ordinal));
        Assert.True(File.Exists(KeyFile()));
    }

    private PushServerKey Key() => new(Options.Create(new SealingKeyOptions { Directory = directory }));

    private string KeyFile() => Path.Combine(directory, PushServerKey.FileName);
}
