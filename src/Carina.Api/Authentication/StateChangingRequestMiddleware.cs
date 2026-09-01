using Carina.Api.Responder;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Authentication;

public sealed class StateChangingRequestMiddleware(RequestDelegate next)
{
    public const string RequiredContentType = "application/json";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ChangesState(context))
        {
            return next(context);
        }

        if (!RequestOrigin.NamesThisOne(context.Request))
        {
            return RefuseAsync(
                context,
                StatusCodes.Status403Forbidden,
                "A request that changes state is answered only when it names this origin.");
        }

        if (AsksForJson(context.Request) && !CarriesJson(context.Request))
        {
            return RefuseAsync(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                $"A request that changes state and carries a body carries {RequiredContentType}.");
        }

        return next(context);
    }

    private static bool ChangesState(HttpContext context)
        => context.GetEndpoint().DeclaredEffect() is not null && !IsSafe(context.Request.Method);

    private static bool IsSafe(string method)
        => HttpMethods.IsGet(method)
           || HttpMethods.IsHead(method)
           || HttpMethods.IsOptions(method)
           || HttpMethods.IsTrace(method);

    private static bool AsksForJson(HttpRequest request)
        => !HttpMethods.IsDelete(request.Method) || CarriesABody(request);

    private static bool CarriesABody(HttpRequest request)
        => request.ContentLength > 0 || request.Headers.ContainsKey(HeaderNames.ContentType);

    private static bool CarriesJson(HttpRequest request)
        => MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue? media)
           && media.MediaType.Equals(RequiredContentType, StringComparison.OrdinalIgnoreCase);

    private static Task RefuseAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(BaseResponder<string>.Error(message));
    }
}
