using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Channels;
using Carina.TestSupport;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Tuners;

internal static class TunerHealthFeature
{
    public static readonly Uri Health = new("/api/tuners/health", UriKind.Relative);

    public static readonly string[] Everything =
    [
        DriverCapabilities.TunerLedger,
        DriverCapabilities.DeviceDetection,
    ];

    public static TunerConfigEntry Entry(string deviceId, TunerKind kind)
        => new() { DeviceId = deviceId, Kind = kind };

    public static void Stocked(FakeDriver driver, IReadOnlyList<TunerConfigEntry> ledger)
    {
        driver.Ledger = new TunerLedgerDto
        {
            Tuners = ledger,
            SavedHash = "saved",
            LoadedHash = "saved",
        };

        driver.Tuners =
        [
            .. ledger.Select(entry => new TunerSnapshot(entry.DeviceId, entry.Kind, TunerState.Idle)),
        ];
    }

    public static async Task<DriverFeature> StartAsync(TunerKind kind)
        => await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a", capabilities: Everything),
            driver => Stocked(driver, [Entry("adapter0", kind)]));

    public static void Quiet(DriverFeature feature, TimeSpan ago)
    {
        DateTime lastSeenAt = DateTime.UtcNow - ago;

        feature.Candidates.Candidates.Add(CandidateChannel.Rehydrate(
            CandidateChannelId.New(),
            new NetworkId(4),
            new ServiceId(101),
            TuningParameters.Bs(15, new TransportStreamId(16_400)),
            observedStreamId: null,
            isSelected: false,
            selectionSource: null,
            selectedAt: null,
            selectionMeasurement: null,
            lastMeasurement: null,
            needsRevalidation: false,
            rotationState: RotationState.NeedsAttention,
            consecutiveFailures: 5,
            nextAttemptAt: null,
            needsAttentionSince: lastSeenAt,
            discoveredAt: lastSeenAt,
            lastSeenAt: lastSeenAt));
    }

    public static async Task<JsonElement> SystemsAsync(DriverFeature feature)
    {
        (HttpStatusCode status, JsonElement body) = await DriverFeature.ReadAsync(await feature.Client.GetAsync(Health));

        Assert.Equal(HttpStatusCode.OK, status);

        return body.GetProperty("data").GetProperty("systems").Clone();
    }

    public static string LevelOf(JsonElement systems, string system)
        => Named(systems, system).GetProperty("level").GetString()!;

    public static JsonElement Named(JsonElement systems, string system)
        => systems.EnumerateArray()
            .Single(entry => entry.GetProperty("system").GetString() == system);
}
