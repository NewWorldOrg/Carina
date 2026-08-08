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

// Liveness only: the single endpoint that stays reachable without authentication.
app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok")));

app.Run();

/// <summary>Payload of the liveness endpoint.</summary>
/// <param name="Status">Constant marker that the process is serving.</param>
public sealed record HealthResponse(string Status);

/// <summary>Entry point, exposed so that feature tests can host the application.</summary>
public partial class Program
{
    internal const string ConnectionStringName = "Carina";
}
