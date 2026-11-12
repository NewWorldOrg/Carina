using System.Buffers.Text;
using System.Security.Cryptography;

using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// The server's own key pair for Web Push (VAPID, RFC 8292). It is made once, when first needed, and kept as a file
/// in the directory of the sealing keys, so whoever keeps that directory keeps this key with it: a browser's
/// subscription is bound to this key, and a new key would strand every subscription made under the old one.
/// </summary>
public sealed class PushServerKey(IOptions<SealingKeyOptions> keys)
{
    public const string FileName = "push-server-key.pem";

    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);

    private readonly Lock gate = new();

    private Kept? kept;

    public string PublicKey() => Held().Published;

    public string AuthorizationFor(string audience, string? sender, DateTimeOffset now)
    {
        Kept held = Held();

        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        signer.ImportPkcs8PrivateKey(held.Pkcs8, out _);

        return $"vapid t={VapidToken.Write(signer, audience, sender, now.Add(TokenLifetime))}, k={held.Published}";
    }

    private Kept Held()
    {
        lock (gate)
        {
            return kept ??= ReadOrMake();
        }
    }

    private Kept ReadOrMake()
    {
        string directory = keys.Value.Directory!;
        string file = Path.Combine(directory, FileName);

        if (!File.Exists(file))
        {
            Directory.CreateDirectory(directory);
            WriteUnlessThere(directory, file);
        }

        return Read(file);
    }

    private static void WriteUnlessThere(string directory, string file)
    {
        string draft = Path.Combine(directory, $"{FileName}.{Guid.NewGuid():N}.tmp");

        using ECDsa made = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        try
        {
            using (StreamWriter writer = new(File.Open(draft, Private())))
            {
                writer.Write(made.ExportPkcs8PrivateKeyPem());
            }

            PlaceUnlessThere(draft, file);
        }
        finally
        {
            File.Delete(draft);
        }
    }

    private static void PlaceUnlessThere(string draft, string file)
    {
        try
        {
            File.CreateHardLink(file, draft);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            if (!File.Exists(file))
            {
                File.Move(draft, file, overwrite: false);
            }
        }
    }

    private static FileStreamOptions Private()
    {
        FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private static Kept Read(string file)
    {
        try
        {
            using ECDsa read = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            read.ImportFromPem(File.ReadAllText(file));
            ECParameters parameters = read.ExportParameters(includePrivateParameters: false);

            if (read.KeySize is not 256 || parameters.Q.X is not { Length: 32 } x || parameters.Q.Y is not { Length: 32 } y)
            {
                throw new CryptographicException("The key is not on P-256.");
            }

            return new Kept(read.ExportPkcs8PrivateKey(), Base64Url.EncodeToString([0x04, .. x, .. y]));
        }
        catch (Exception unreadable) when (unreadable is ArgumentException or CryptographicException)
        {
            throw new InvalidOperationException(
                $"The Web Push server key in {FileName} cannot be read. It is left as it is: a new key would strand every subscription made under the old one.",
                unreadable);
        }
    }

    private sealed record Kept(byte[] Pkcs8, string Published);
}
