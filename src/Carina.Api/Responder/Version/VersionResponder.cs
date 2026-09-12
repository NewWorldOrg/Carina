using Carina.Api.Services;

namespace Carina.Api.Responder.Version;

public sealed record VersionResponder(string Version)
{
    public static VersionResponder Of(VersionView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new VersionResponder(view.Version);
    }
}
