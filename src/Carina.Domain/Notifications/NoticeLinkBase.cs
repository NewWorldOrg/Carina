namespace Carina.Domain.Notifications;

/// <summary>
/// The address the screens are reached at from outside, which the links of a notice are built on. With none, a notice
/// carries no link.
/// </summary>
public sealed class NoticeLinkBase
{
    private readonly Uri? origin;

    private NoticeLinkBase(Uri? origin)
    {
        this.origin = origin;
    }

    public static NoticeLinkBase None { get; } = new(null);

    public bool IsKnown => origin is not null;

    /// <summary>
    /// The origin as it is written to another service: the scheme, the host and the port, with nothing after them.
    /// </summary>
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

    /// <summary>
    /// The address of the screen at <paramref name="path"/>, or nothing when the origin is not known.
    /// </summary>
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
