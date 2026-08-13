using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Extensions;
using Carina.Api.OpenApi;
using Carina.Infrastructure.DependencyInjection;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers(options => options.Filters.Add(new ProducesAttribute("application/json")))
    .AddJsonOptions(options => WireJson.Configure(options.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(options => WireJson.Configure(options.SerializerOptions));
builder.Services.AddApplicationServices();
builder.Services.AddCarinaInfrastructure(builder.Configuration);
builder.Services.AddAuthentication();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<ApiDocumentTransformer>();
    options.AddSchemaTransformer<StringEnumSchemaTransformer>();
    options.AddOperationTransformer<DefaultDenyResponseTransformer>();
});

var app = builder.Build();

app.UseAuthentication();
app.UseMiddleware<DefaultDenyAuthenticationMiddleware>();

app.MapOpenApi();
app.MapControllers();

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
