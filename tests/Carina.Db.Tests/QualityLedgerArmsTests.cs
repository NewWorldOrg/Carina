using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Puppis.Contracts;

namespace Carina.Db.Tests;

/// <summary>
/// The queries the signal sample pass and the supply watch run in the store, held to the aggregate's own answer for
/// the same rows: the session measurements read as open are the ones that have not ended, a session closed on the very
/// instant it started among them, and the incidents read as unsettled are the ones that have not settled, each standing
/// of an incident and the instants its times share with its detection among them.
/// </summary>
[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualityLedgerArmsTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Started = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Microsecond = TimeSpan.FromTicks(10);

    private static readonly Threshold Applied = Threshold.Provisionally(300, 0, Started);

    [Fact(DisplayName = "BR-QD-005: the session measurements the store reads as open are the ones each says has not ended, closed on its start or a microsecond after among them")]
    public async Task TheMeasurementsReadAsOpenAreTheOnesThatHaveNotEnded()
    {
        await ClearAsync("quality_session_measurement");

        List<QualitySessionMeasurement> laid =
        [
            Measurement("open-1", null),
            Measurement("open-2", null, measured: true),
            Measurement("closed-at-start", Started.AddMinutes(-1)),
            Measurement("closed-a-microsecond-on", Started.Add(Microsecond)),
            Measurement("closed-later", Started.AddHours(1)),
        ];

        await using (CarinaDbContext context = Context())
        {
            context.AddRange(laid);
            await context.SaveChangesAsync();
        }

        IReadOnlyList<QualitySessionMeasurement> stored = await ReadAllAsync<QualitySessionMeasurement>();

        Assert.Contains(stored, measurement => measurement.HasEnded && measurement.EndedAt == measurement.StartedAt);
        Assert.Contains(stored, measurement => !measurement.HasEnded);

        await using CarinaDbContext reading = Context();

        Assert.Equal(
            Sessions(stored.Where(measurement => !measurement.HasEnded)),
            Sessions(await new QualitySessionMeasurementRepository(reading).ListOpenAsync(CancellationToken.None)));
    }

    [Fact(DisplayName = "BR-QS-002: the incidents the store reads as unsettled are the ones each says has not settled, so every incident awaiting its notice is among them")]
    public async Task TheIncidentsReadAsUnsettledAreTheOnesThatHaveNotSettled()
    {
        await ClearAsync("quality_incident");

        List<QualityIncident> laid = [];

        foreach (TimeSpan apart in new[] { TimeSpan.Zero, Microsecond, TimeSpan.FromMinutes(5) })
        {
            laid.Add(Incident());

            QualityIncident notified = Incident();
            notified.Notify(Started.Add(apart));
            laid.Add(notified);

            QualityIncident resolved = Incident();
            resolved.Resolve(Started.Add(apart));
            laid.Add(resolved);

            QualityIncident notifiedThenResolved = Incident();
            notifiedThenResolved.Notify(Started.Add(apart));
            notifiedThenResolved.Resolve(Started.Add(apart + apart));
            laid.Add(notifiedThenResolved);
        }

        await using (CarinaDbContext context = Context())
        {
            context.AddRange(laid);
            await context.SaveChangesAsync();
        }

        IReadOnlyList<QualityIncident> stored = await ReadAllAsync<QualityIncident>();

        Assert.Contains(stored, incident => incident.AwaitsNotice);
        Assert.Contains(stored, incident => !incident.AwaitsNotice && !incident.HasSettled);
        Assert.Contains(stored, incident => incident.HasSettled && incident.ResolvedAt == incident.DetectedAt);

        await using CarinaDbContext reading = Context();
        IReadOnlyList<QualityIncident> unsettled =
            await new QualityIncidentRepository(reading).ListUnsettledAsync(CancellationToken.None);

        Assert.Equal(Ids(stored.Where(incident => !incident.HasSettled)), Ids(unsettled));
        Assert.Subset(Ids(unsettled).ToHashSet(), Ids(stored.Where(incident => incident.AwaitsNotice)).ToHashSet());
    }

    private async Task ClearAsync(string table)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var clearing = new NpgsqlCommand($"TRUNCATE {table} CASCADE", connection);
        await clearing.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<T>> ReadAllAsync<T>()
        where T : class
    {
        await using CarinaDbContext reading = Context();

        return await reading.Set<T>().AsNoTracking().ToListAsync();
    }

    private static QualitySessionMeasurement Measurement(string session, DateTime? closedAt, bool measured = false)
    {
        QualitySessionMeasurement measurement = QualitySessionMeasurement.Open(
            "instance-a",
            SessionId.Parse(session),
            SessionPurpose.Live,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            Started);

        if (measured)
        {
            measurement.Observe(2, 9000, 0, Started.AddMinutes(1));
        }

        if (closedAt is { } at)
        {
            measurement.Close(at);
        }

        return measurement;
    }

    private static QualityIncident Incident()
        => QualityIncident.Detect(
            QualityIncidentId.New(),
            Started,
            QualityThresholdKey.SupplySilence,
            QualitySubject.Of(QualitySubjectKind.Tuner, "adapter0"),
            420,
            Applied,
            silence: SupplySilence.SignalSamples);

    private static string[] Sessions(IEnumerable<QualitySessionMeasurement> measurements)
        => [.. measurements.Select(measurement => measurement.Session.ToString()).Order(StringComparer.Ordinal)];

    private static Guid[] Ids(IEnumerable<QualityIncident> incidents)
        => [.. incidents.Select(incident => incident.Id.Value).Order()];

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);
}
