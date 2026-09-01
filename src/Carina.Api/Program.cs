using System.Globalization;

using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Events;
using Carina.Api.Extensions;
using Carina.Api.OpenApi;
using Carina.Api.Playback;
using Carina.Api.Responder;
using Carina.Api.Responder.Playback;
using Carina.Api.Services;
using Carina.Domain.Streaming;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.DependencyInjection;
using Carina.Infrastructure.Events;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers(options => options.Filters.Add(new ProducesAttribute("application/json")))
    .AddJsonOptions(options => WireJson.Configure(options.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(options => WireJson.Configure(options.SerializerOptions));
builder.Services.AddApplicationServices();
builder.Services.AddTrustedProxies(builder.Configuration);
builder.Services.AddPublicOrigin(builder.Configuration);
builder.Services.AddAnonymousNetworks(builder.Configuration);
builder.Services.AddCarinaInfrastructure(builder.Configuration);
builder.Services
    .AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<ApiDocumentTransformer>();
    options.AddSchemaTransformer<StringEnumSchemaTransformer>();
    options.AddOperationTransformer<DefaultDenyResponseTransformer>();
    options.AddOperationTransformer<UnhandledFailureResponseTransformer>();
    options.AddOperationTransformer<OperationNamingTransformer>();
    options.AddOperationTransformer<SearchVocabularyTransformer>();
    options.AddOperationTransformer<QueryInputTransformer>();
});

WebApplication app = builder.Build();


app.UseMiddleware<UnhandledFailureMiddleware>();
app.UseForwardedHeaders();
app.UseMiddleware<ForwardedHeadersDiagnosticMiddleware>();
app.UseCookiePolicy(SessionCookiePolicy.Options);
app.UseAuthentication();
app.UseMiddleware<DefaultDenyAuthenticationMiddleware>();
app.UseMiddleware<StateChangingRequestMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().WithEffect(EndpointEffect.Reading);
}

app.MapControllers();
app.MapGet(AppEventStream.Path, (HttpContext context, AppEventHub hub) =>
    AppEventStream.Invoke(context, hub)).ExcludeFromDescription().WithEffect(EndpointEffect.Reading);

app.MapGet(ProgrammeFeedStream.Path, (HttpContext context, ProgrammeFeedService feed) =>
    ProgrammeFeedStream.Invoke(context, feed)).ExcludeFromDescription().WithEffect(EndpointEffect.Reading);

app.MapMethods(VideoDelivery.Path, VideoDelivery.Methods, (HttpContext context, string id, PlaybackService playback) =>
    VideoDelivery.Invoke(context, id, playback)).ExcludeFromDescription().WithEffect(EndpointEffect.Reading).Ticketed();

app.MapGet(
        PlayDelivery.Path,
        (HttpContext context, string id, PlaybackService playback, IOnTheFlyPlayer player) =>
            PlayDelivery.Invoke(context, id, playback, player))
    .WithName(PlaybackSurfaces.PlayingIsCalled)
    .WithTags(PlaybackSurfaces.Tag)
    .WithSummary(PlaybackSurfaces.HowARecordingIsPlayedInABrowser)
    .Produces<BaseResponder<PlaybackPlanResponder>>(StatusCodes.Status200OK, PlayDelivery.Json)
    .Reads(PlaybackSurfaces.WhereThePlayingStarts, PlaybackSurfaces.WhichProfileThePictureIsEncodedIn)
    .WithEffect(EndpointEffect.Reading);

app.MapGet(
        ThumbnailDelivery.Path,
        (HttpContext context, string id, IDrawnThumbnails drawn) =>
            ThumbnailDelivery.Invoke(context, id, drawn))
    .WithName(PlaybackSurfaces.ThePictureIsCalled)
    .WithTags(PlaybackSurfaces.Tag)
    .WithSummary(PlaybackSurfaces.ThePictureDrawnOfARecording)
    .Produces(StatusCodes.Status200OK, contentType: ThumbnailDelivery.MediaType)
    .WithEffect(EndpointEffect.Reading);

app.MapGet(
        ScrubDelivery.Path,
        (HttpContext context, string id, IScrubFrames frames) =>
            ScrubDelivery.Invoke(context, id, frames))
    .WithName(PlaybackSurfaces.TheFrameIsCalled)
    .WithTags(PlaybackSurfaces.Tag)
    .WithSummary(PlaybackSurfaces.AFrameFromWhereTheSliderIs)
    .Produces(StatusCodes.Status200OK, contentType: ScrubDelivery.MediaType)
    .Reads(PlaybackSurfaces.WhereTheFrameIsTakenFrom)
    .WithEffect(EndpointEffect.Reading);

try
{
    app.Run();
}
catch (OptionsValidationException failure)
{
    Console.Error.WriteLine(failure.Message);
    Console.Error.WriteLine("Nothing was served. Fix the settings above and start again.");

    return 78;
}

return 0;

public partial class Program;
