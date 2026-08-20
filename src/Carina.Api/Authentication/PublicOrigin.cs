namespace Carina.Api.Authentication;

public sealed record PublicRedirectUri(string Value, bool Guessed);

public sealed class PublicOrigin
{
    public const string Key = "CARINA_PUBLIC_ORIGIN";

    private readonly string? named;

    private PublicOrigin(string? named) => this.named = named;

    public bool IsGuessed => named is null;

    public static PublicOrigin Named(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return new PublicOrigin(null);
        }

        string entry = setting.Trim();

        if (!Uri.TryCreate(entry, UriKind.Absolute, out Uri? parsed)
            || parsed.Host.Length == 0
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                $"{Key} names the address a browser reaches this installation at, and '{entry}' is not one. "
                + "Write it as https://host with a port only where the port is not the scheme's own.",
                Key);
        }

        if (parsed.AbsolutePath.Length > 1 || parsed.Query.Length > 0 || parsed.Fragment.Length > 0)
        {
            throw new ArgumentException(
                $"{Key} names an origin and nothing after it, and '{entry}' carries more. "
                + $"{OidcHandshake.CallbackPath} is added here.",
                Key);
        }

        return new PublicOrigin(parsed.GetLeftPart(UriPartial.Authority));
    }

    public static string RedirectUriAt(string origin) => $"{origin}{OidcHandshake.CallbackPath}";

    public PublicRedirectUri RedirectUriFor(string arrivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arrivedAt);

        return new PublicRedirectUri(RedirectUriAt(named ?? arrivedAt), IsGuessed);
    }

    public override string ToString() => named ?? "nothing";
}
