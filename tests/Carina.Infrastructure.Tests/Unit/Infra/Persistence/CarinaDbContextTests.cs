using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Scans;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using static Carina.Infrastructure.Tests.Fixtures.Persistence.CarinaModel;

namespace Carina.Infrastructure.Tests.Unit.Infra.Persistence;

internal sealed class ConventionProbe
{
    public int Id { get; set; }
    public DateTime RecordedAt { get; set; }
}

[Trait("Category", "Unit")]
public sealed class CarinaDbContextTests
{
    private static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NamesTablesAndColumnsInSnakeCase()
    {
        using ProbeDbContext context = Probe();

        IEntityType entity = context.Model.FindEntityType(typeof(ConventionProbe))!;

        Assert.Equal("convention_probe", entity.GetTableName());
        Assert.Equal("recorded_at", entity.FindProperty(nameof(ConventionProbe.RecordedAt))!.GetColumnName());
    }

    [Fact]
    public void RoutesEveryDateTimeThroughTheUtcConverter()
    {
        using ProbeDbContext context = Probe();

        IProperty property = context.Model.FindEntityType(typeof(ConventionProbe))!
            .FindProperty(nameof(ConventionProbe.RecordedAt))!;

        Assert.IsType<UtcDateTimeConverter>(property.GetValueConverter());
    }

    [Fact]
    public void NoValueObjectOfThisDomainIsAnEntityInItsOwnRight()
    {
        using CarinaDbContext context = Context();

        Assert.DoesNotContain(
            Schema(context).GetEntityTypes(),
            entity => entity.FindOwnership() is not null);
    }

    [Fact]
    public void EveryTableOfThisDomainIsNamedInSnakeCase()
    {
        using CarinaDbContext context = Context();

        Assert.Equal(
            [
                "archived_programme",
                "auth_session",
                "broadcast_service",
                "candidate_channel",
                "collection_epoch",
                "data_broadcast_settings",
                "encode_auto_run",
                "encode_chapter",
                "encode_destination",
                "encode_job",
                "encode_profile",
                "encode_scratch_file",
                "integrity_check",
                "integrity_finding",
                "local_account",
                "logo_visit",
                "migration_channel_proposal",
                "migration_detail",
                "migration_loss",
                "migration_rule_proposal",
                "migration_run",
                "migration_standing",
                "migration_tally",
                "notification_delivery",
                "notification_destination",
                "notification_event",
                "notification_rule",
                "notification_settings",
                "notification_watch",
                "oidc_config",
                "playback_position",
                "programme",
                "quality_incident",
                "quality_session_measurement",
                "quality_signal_rollup",
                "quality_signal_sample",
                "quality_threshold",
                "quality_threshold_change",
                "recording",
                "reservation",
                "reservation_outcome",
                "rule",
                "satellite_transport_stream",
                "scan_run",
                "scan_run_attempt",
                "segment_catalog_entry",
                "segment_catalog_occurrence",
                "segment_detection",
                "segment_extraction",
                "segment_learning_data",
                "segment_learning_round",
                "segment_model_version",
                "segment_seek_event",
                "segment_settings",
                "segment_skip_event",
                "service_reach_config",
                "station_logo",
                "stream_visit",
                "stream_visit_tally",
            ],
            Schema(context).GetEntityTypes()
                .Where(entity => entity.FindOwnership() is null)
                .Select(entity => entity.GetTableName())
                .OfType<string>()
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheOnlyShapeReadFromAQueryRatherThanATableIsTheSearchAcrossBothLayers()
    {
        using CarinaDbContext context = Context();

        Assert.Equal(
            [nameof(ProgrammeMatch)],
            Schema(context).GetEntityTypes()
                .Where(entity => entity.GetTableName() is null)
                .Select(entity => entity.ClrType.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryTimeOfThisDomainGoesThroughTheUtcConverter()
    {
        using CarinaDbContext context = Context();

        IProperty[] times = Schema(context).GetEntityTypes()
            .SelectMany(EveryProperty)
            .Where(property => property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
            .ToArray();

        Assert.NotEmpty(times);
        Assert.All(times, property => Assert.IsType<UtcDateTimeConverter>(property.GetValueConverter()));
    }

    [Fact]
    public void TwoCandidatesMayBeTunedByTheVerySameParameters()
    {
        using CarinaDbContext context = Context();
        var carrying = TuningParameters.Terrestrial(27);

        context.Attach(Candidate(1, carrying));

        context.Add(Candidate(2, carrying));

        Assert.Equal(2, context.ChangeTracker.Entries<CandidateChannel>().Count());
    }

    [Fact]
    public void TwoCandidatesMayCarryTheVerySameReading()
    {
        using CarinaDbContext context = Context();
        var measured = SignalMeasurement.WithLock(At, 21_000);
        CandidateChannel first = Candidate(1, TuningParameters.Terrestrial(27));
        CandidateChannel second = Candidate(2, TuningParameters.Terrestrial(27));
        first.RecordTuningSuccess(measured, At);
        second.RecordTuningSuccess(measured, At);
        context.Attach(first);

        context.Add(second);

        Assert.Equal(2, context.ChangeTracker.Entries<CandidateChannel>().Count());
    }

    [Fact]
    public void TwoAttemptsMayBeTunedByTheVerySameParametersAndCarryTheVerySameReading()
    {
        using CarinaDbContext context = Context();
        var carrying = TuningParameters.Terrestrial(27);
        var measured = SignalMeasurement.WithLock(At, 21_000);
        context.Attach(Attempt(carrying, measured));

        context.Add(Attempt(carrying, measured));

        Assert.Equal(2, context.ChangeTracker.Entries<ScanRunAttempt>().Count());
    }

    [Fact]
    public void OneReadingMayBeKeptByBothAnAttemptAndACandidateAtOnce()
    {
        using CarinaDbContext context = Context();
        var measured = SignalMeasurement.WithLock(At, 21_000);
        CandidateChannel candidate = Candidate(1, TuningParameters.Terrestrial(27));
        candidate.RecordTuningSuccess(measured, At);
        context.Attach(Attempt(TuningParameters.Terrestrial(27), measured));

        context.Add(candidate);

        Assert.Single(context.ChangeTracker.Entries<CandidateChannel>());
    }

    private static ProbeDbContext Probe() => new(Options());

    private static CandidateChannel Candidate(int service, TuningParameters carrying)
        => CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(1),
            new ServiceId(service),
            carrying,
            At);

    private static ScanRunAttempt Attempt(TuningParameters carrying, SignalMeasurement measured)
        => ScanRunAttempt.Rehydrate(
            ScanRunAttemptId.New(),
            ScanRunId.New(),
            carrying,
            ScanAttemptOutcome.Succeeded,
            measured,
            null,
            null,
            At,
            At);

    private sealed class ProbeDbContext(DbContextOptions<CarinaDbContext> options) : CarinaDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ConventionProbe>();
        }
    }
}
