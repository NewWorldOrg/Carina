using Carina.Api.Responder;

namespace Carina.Api.Common;

public sealed class UnhandledFailureMiddleware(
    RequestDelegate next,
    ILogger<UnhandledFailureMiddleware> logger)
{
    public const string Message =
        "The request failed and nothing it asked for was written. The app log names the failure.";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (Exception failure) when (!context.Response.HasStarted)
        {
            logger.LogError(
                failure,
                "{Method} {Path} ended without an answer of its own.",
                context.Request.Method,
                context.Request.Path.Value);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            await context.Response.WriteAsJsonAsync(BaseResponder<object>.Error(Message));
        }
    }
}
