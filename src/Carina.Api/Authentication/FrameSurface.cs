using Microsoft.Net.Http.Headers;

namespace Carina.Api.Authentication;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class OpenToTheFrameAttribute : Attribute;

/// <summary>
/// The surfaces a Hybridcast application's frame reaches. The frame is sandboxed without an origin of its own and
/// sends no cookie, and the ticket in the path is the only credential it carries. So these surfaces, and no others,
/// answer any origin without credentials, answer its preflight for GET and POST with the safe request headers, the
/// content type and the X-Requested-With a script library adds, none of which is sent on to the broadcaster's
/// server, and name no response header beyond the safe ones; a POST reaches them without naming this
/// origin, and a WebSocket is taken from an origin of "null".
/// </summary>
public static class FrameSurfaces
{
    public const string Policy = "hybridcast-frame";

    public static readonly TimeSpan PreflightKept = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<string> Methods { get; } = [HttpMethods.Get, HttpMethods.Post];

    public static IReadOnlyList<string> RequestHeaders { get; } =
        [HeaderNames.Accept, HeaderNames.AcceptLanguage, HeaderNames.ContentLanguage, HeaderNames.ContentType, HeaderNames.XRequestedWith];

    public static IServiceCollection AddFrameSurfaces(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddCors(options => options.AddPolicy(
            Policy,
            policy => policy
                .AllowAnyOrigin()
                .WithMethods([.. Methods])
                .WithHeaders([.. RequestHeaders])
                .SetPreflightMaxAge(PreflightKept)));
    }

    public static TBuilder OpenToTheFrame<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RequireCors(Policy);
        builder.Add(endpoint => endpoint.Metadata.Add(new OpenToTheFrameAttribute()));

        return builder;
    }

    public static bool IsOpenToTheFrame(this Endpoint? endpoint)
        => endpoint?.Metadata.GetMetadata<OpenToTheFrameAttribute>() is not null;
}
