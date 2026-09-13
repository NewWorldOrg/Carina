using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Migration;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class HeldMigrationRecord : IMigrationRecordRepository
{
    public MigrationReport? Kept { get; set; }

    public int Rehearsals { get; set; }

    public DateTime? LastRehearsalFinishedAt { get; set; }

    public List<int> PagesAsked { get; } = [];

    public Task SaveAsync(MigrationReport report, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<MigrationRun?> LatestAsync(CancellationToken cancellationToken)
        => Task.FromResult(Kept?.Run);

    public Task<MigrationReport?> ReadAsync(MigrationRunId runId, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<MigrationRecordSummary?> SummariseAsync(CancellationToken cancellationToken)
    {
        if (Kept is not { } report)
        {
            return Task.FromResult<MigrationRecordSummary?>(null);
        }

        Dictionary<MigrationRefusal, int> counted = report.Details
            .GroupBy(detail => detail.Refusal)
            .ToDictionary(group => group.Key, group => group.Count());

        return Task.FromResult<MigrationRecordSummary?>(new MigrationRecordSummary(
            report.Run,
            report.Tallies,
            MigrationRefusalCount.EveryOne(counted),
            report.Losses,
            Rehearsals,
            LastRehearsalFinishedAt));
    }

    public Task<PaginatedList<MigrationDetail>> ListDetailsAsync(
        MigrationRunId runId,
        MigrationDetailQuery query,
        CancellationToken cancellationToken)
    {
        PagesAsked.Add(query.Page);

        MigrationDetail[] found =
        [
            .. (Kept?.Details ?? [])
                .Where(detail => detail.RunId.Equals(runId))
                .OrderBy(detail => detail.Refusal)
                .ThenBy(detail => detail.Population)
                .ThenBy(detail => detail.Subject, StringComparer.Ordinal),
        ];

        return Task.FromResult(new PaginatedList<MigrationDetail>(
            [.. found.Skip((query.Page - 1) * query.PerPage).Take(query.PerPage)],
            found.Length,
            query.Page,
            query.PerPage));
    }
}

internal sealed class MigrationFeature : IAsyncDisposable
{
    public static readonly DateTime Small = new(2026, 8, 10, 3, 12, 0, DateTimeKind.Utc);

    public static readonly MigrationSourceName Source = new("the recording system being replaced");

    private readonly TestingWebApplicationFactory factory = new();

    public MigrationFeature()
    {
        WebApplicationFactory<Program> built = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddScoped<IMigrationRecordRepository>(_ => Records);
            }));

        Client = built.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
    }

    public HttpClient Client { get; }

    public HeldMigrationRecord Records { get; } = new();

    public static MigrationReport Carried(MigrationPass pass, params MigrationVerdict[] verdicts)
    {
        Dictionary<MigrationPopulation, int> offered = MigrationPopulations.Counted
            .ToDictionary(population => population, _ => 0);

        foreach (MigrationVerdict verdict in verdicts)
        {
            offered[verdict.Population]++;
        }

        return MigrationCensus.Taken(
            MigrationRunId.New(),
            Source,
            pass,
            MigrationRoll.Of(offered, verdicts),
            MigrationAftermath.Nothing,
            MigrationRootStanding.Empty,
            EncodeUnaskedStanding.Settled,
            Small,
            Small.AddMinutes(6));
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{') && !body.StartsWith('['))
        {
            return (response.StatusCode, default);
        }

        using JsonDocument document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }
}
