using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Events;
using Carina.Api.Extensions;
using Carina.Api.OpenApi;
using Carina.Api.Services;
using Carina.Infrastructure.DependencyInjection;
using Carina.Infrastructure.Events;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers(options => options.Filters.Add(new ProducesAttribute("application/json")))
    .AddJsonOptions(options => WireJson.Configure(options.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(options => WireJson.Configure(options.SerializerOptions));
builder.Services.AddApplicationServices();
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
});

WebApplication app = builder.Build();


app.UseMiddleware<UnhandledFailureMiddleware>();
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
