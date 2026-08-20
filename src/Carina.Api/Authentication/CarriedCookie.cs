namespace Carina.Api.Authentication;

public static class CarriedCookie
{
    public static string? FirstUsable(
        HttpRequest request,
        IReadOnlyList<string> namesInDescendingTrust,
        Func<string?, bool> usable)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(namesInDescendingTrust);
        ArgumentNullException.ThrowIfNull(usable);

        foreach (string name in namesInDescendingTrust)
        {
            if (request.Cookies.TryGetValue(name, out string? carried) && usable(carried))
            {
                return carried;
            }
        }

        return null;
    }
}
