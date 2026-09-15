using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class QualityFeature : IAsyncDisposable
{
    public static readonly DateTime Noon = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public QualityFeature()
    {
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IQualityLedgerReader>(Ledger);
                services.AddSingleton<IAppEventPublisher>(Events);
                services.AddSingleton<IQualityThresholdRepository>(Thresholds);
                services.AddSingleton<IQualityThresholdChangeRepository>(Changes);
                services.AddSingleton<IQualitySignalReader>(Signals);
                services.AddSingleton<IQualityIncidentRepository>(Incidents);
                services.AddSingleton<ISupplyStandingBoard>(Board);
                services.AddSingleton<IBroadcastStreamDirectory>(Streams);
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
    }

    public HttpClient Client { get; }

    public MovingClock Clock { get; } = new(Noon);

    public HeldQualityLedger Ledger { get; } = new();

    public SilentEvents Events { get; } = new();

    public HeldQualityThresholds Thresholds { get; } = new();

    public HeldQualityThresholdChanges Changes { get; } = new();

    public HeldQualitySignals Signals { get; } = new();

    public HeldQualityIncidents Incidents { get; } = new();

    public StandingHeld Board { get; } = new();

    public HeldStreams Streams { get; } = new([]);

    public QualityIncident Quiet(
        SupplySilence silence = SupplySilence.SignalSamples,
        string subject = "adapter3.frontend0",
        QualitySubjectKind kind = QualitySubjectKind.Tuner)
    {
        QualityIncident opened = QualityIncident.Detect(
            QualityIncidentId.New(),
            Noon.AddMinutes(-10),
            QualityThresholdKey.SupplySilence,
            QualitySubject.Of(kind, subject),
            300,
            QualityThresholdShapes.AsShipped(QualityThresholdKey.SupplySilence, Noon.AddMinutes(-10)),
            silence: silence);

        opened.Notify(Noon.AddMinutes(-10));
        Incidents.Incidents.Add(opened);

        return opened;
    }

    public QualityIncident Restated()
    {
        QualityIncident elsewhere = QualityIncident.Detect(
            QualityIncidentId.New(),
            Noon.AddMinutes(-20),
            QualityThresholdKey.LockRate,
            QualitySubject.Of(QualitySubjectKind.Tuner, "adapter3.frontend0"),
            0.4,
            QualityThresholdShapes.AsShipped(QualityThresholdKey.LockRate, Noon.AddMinutes(-20)),
            QualityIncidentOwner.Tuner,
            "NoLock");

        elsewhere.Notify(Noon.AddMinutes(-20));
        Incidents.Incidents.Add(elsewhere);

        return elsewhere;
    }

    public SignalFigures Sampled(
        string tuner = "adapter3.frontend0",
        long samples = 360,
        long locked = 360,
        long unreachable = 0,
        int? carrierToNoise = 34_779,
        double? bitErrorRate = 0,
        IReadOnlyList<string>? notRead = null)
    {
        SignalFigures figures = new(
            new TunerDeviceId(tuner),
            samples,
            locked,
            0,
            unreachable,
            carrierToNoise,
            bitErrorRate,
            notRead ?? [],
            Noon.AddMinutes(-10));

        Signals.Figures.Add(figures);

        return figures;
    }

    public QualityLedgerRow Recorded(
        long? dropped = 0,
        long? total = 1_000_000,
        long? scrambled = 0,
        long overflows = 0,
        int service = 1_024,
        string? tuner = "adapter3.frontend0",
        TuneSystem? kind = TuneSystem.IsdbT,
        DateTime? startedAt = null)
    {
        QualityLedgerRow row = QualityLedgerRow.Of(
            RecordingId.New(),
            new NetworkId(32_736),
            new ServiceId(service),
            kind,
            tuner is null ? null : new TunerDeviceId(tuner),
            startedAt ?? Noon.AddHours(-3),
            dropped is { } lost && total is { } carried
                ? DropCounters.Counted(lost, carried)
                : DropCounters.Unmeasured,
            scrambled,
            overflows,
            dropped is null ? null : Noon.AddHours(-2));

        Ledger.Rows.Add(row);

        return row;
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path)
    {
        using HttpResponseMessage response =
            await Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new { });

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PatchAsync(string path, object body)
    {
        using HttpResponseMessage response = await Client.PatchAsJsonAsync(new Uri(path, UriKind.Relative), body);

        return await ReadAsync(response);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{') && !body.StartsWith('['))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }
}

internal sealed class StandingHeld : ISupplyStandingBoard
{
    public SupplyStanding? Latest { get; private set; }

    public void Held(SupplyStanding standing) => Latest = standing;
}
