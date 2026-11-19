namespace Carina.Api.Authentication;

public static class NeverStored
{
    public const string Directive = "no-store";

    public static void Mark(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers.CacheControl = Directive;
    }
}
