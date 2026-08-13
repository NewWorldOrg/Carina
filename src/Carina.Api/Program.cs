using Carina.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(ConnectionStringName);
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        $"No database connection string: expected configuration key ConnectionStrings:{ConnectionStringName} "
        + $"(environment variable ConnectionStrings__{ConnectionStringName}) to be set, but it was empty.");
}

builder.Services.AddCarinaInfrastructure(connectionString);
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();

app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok")));

app.Run();

public sealed record HealthResponse(string Status);

public partial class Program
{
    internal const string ConnectionStringName = "Carina";
}
