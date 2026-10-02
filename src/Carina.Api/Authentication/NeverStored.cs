namespace Carina.Api.Authentication;

/// <summary>The mark on an answer that no cache may keep.</summary>
public static class NeverStored
{
    public const string Directive = "no-store";

    public static void Mark(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers.CacheControl = Directive;
    }
}
