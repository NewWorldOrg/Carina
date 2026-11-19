namespace Carina.Domain.Notifications;

public sealed class NoticeLinkBase
{
    private readonly Uri? origin;

    private NoticeLinkBase(Uri? origin)
    {
        this.origin = origin;
    }

    public static NoticeLinkBase None { get; } = new(null);

    public bool IsKnown => origin is not null;

    public string? Origin => origin?.GetLeftPart(UriPartial.Authority);

    public static NoticeLinkBase At(Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        bool isAnOrigin = origin.IsAbsoluteUri
            && (origin.Scheme == Uri.UriSchemeHttps || origin.Scheme == Uri.UriSchemeHttp)
            && origin.AbsolutePath == "/"
            && origin.Query.Length is 0
            && origin.Fragment.Length is 0
            && origin.UserInfo.Length is 0;

        if (!isAnOrigin)
        {
            throw new ArgumentException(
                "The screens are reached at an http or https origin: a scheme, a host and a port, and nothing after them.",
                nameof(origin));
        }

        return new NoticeLinkBase(origin);
    }

    public string? To(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("A screen is named by a path from the root.", nameof(path));
        }

        return origin is null ? null : new Uri(origin, path).AbsoluteUri;
    }
}
