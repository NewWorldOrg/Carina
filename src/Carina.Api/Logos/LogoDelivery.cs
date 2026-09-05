using System.Globalization;

using Carina.Domain.Channels;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Logos;

public static class LogoDelivery
{
    public const string Path = "/api/services/{networkId:int}-{serviceId:int}/logo";

    public const string MediaType = "image/png";

    public const string HeldForADay = "private, max-age=86400";

    public static string Of(NetworkId networkId, ServiceId serviceId)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"/api/services/{networkId.Value}-{serviceId.Value}/logo");
    }

    public static async Task Invoke(
        HttpContext context,
        int networkId,
        int serviceId,
        IStationLogoRepository logos)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logos);

        if (networkId is < NetworkId.MinValue or > NetworkId.MaxValue
            || serviceId is < ServiceId.MinValue or > ServiceId.MaxValue)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        StationLogo? logo = await logos.OfServiceAsync(
            new NetworkId(networkId),
            new ServiceId(serviceId),
            context.RequestAborted);

        if (logo is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        string tag = Tag(logo);

        context.Response.Headers[HeaderNames.CacheControl] = HeldForADay;
        context.Response.Headers[HeaderNames.ETag] = tag;
        context.Response.Headers[HeaderNames.LastModified] =
            logo.CollectedAt.ToString("R", CultureInfo.InvariantCulture);

        if (context.Request.Headers.IfNoneMatch.Contains(tag))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;

            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = MediaType;
        context.Response.ContentLength = logo.Picture.Length;

        await context.Response.Body.WriteAsync(logo.Picture, context.RequestAborted);
    }

    private static string Tag(StationLogo logo)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"\"{logo.LogoId.Value:x}-{logo.LogoVersion:x}-{logo.Width:x}x{logo.Height:x}-{logo.CollectedAt.Ticks:x}\"");
}
