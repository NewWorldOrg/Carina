using System.Text.Json;
using System.Text.Json.Serialization;

using Carina.Api.Authentication;
using Carina.Api.Extensions;
using Carina.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddApplicationServices();
builder.Services.AddCarinaInfrastructure(builder.Configuration);
builder.Services.AddAuthentication();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseAuthentication();
app.UseMiddleware<DefaultDenyAuthenticationMiddleware>();

app.MapOpenApi();
app.MapControllers();

app.Run();

public partial class Program;
