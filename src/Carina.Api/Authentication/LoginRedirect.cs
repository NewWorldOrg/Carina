namespace Carina.Api.Authentication;

public static class LoginRedirect
{
    public const string Path = "/login";

    public const string LoggedOut = "/logged-out";

    public const string ReturnKey = "next";

    public const string Home = "/";

    public const string ErrorKey = "error";

    public const string TheIdentityProviderFailed = "oidc";

    public static string Within(string? target)
    {
        if (string.IsNullOrEmpty(target)
            || target[0] != '/'
            || target.Any(char.IsControl)
            || target.Contains('\\', StringComparison.Ordinal)
            || target.StartsWith("//", StringComparison.Ordinal)
            || LeadsBackToTheLoginScreen(target))
        {
            return Home;
        }

        return target;
    }

    public static string For(string? target)
        => $"{Path}?{ReturnKey}={Uri.EscapeDataString(Within(target))}";

    public static string AfterAFailedSignIn(string? target)
        => $"{For(target)}&{ErrorKey}={TheIdentityProviderFailed}";

    private static bool LeadsBackToTheLoginScreen(string target)
        => target.Equals(Path, StringComparison.OrdinalIgnoreCase)
           || target.StartsWith($"{Path}?", StringComparison.OrdinalIgnoreCase)
           || target.StartsWith($"{Path}/", StringComparison.OrdinalIgnoreCase);
}
