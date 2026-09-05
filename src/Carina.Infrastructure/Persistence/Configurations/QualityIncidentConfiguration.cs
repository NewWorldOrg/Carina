using Carina.Domain.Quality;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class QualityIncidentConfiguration : IEntityTypeConfiguration<QualityIncident>
{
    public const string TableName = "quality_incident";

    public const string UnsettledIndexName = "ix_quality_incident_unsettled";

    public void Configure(EntityTypeBuilder<QualityIncident> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_quality_incident_vocabulary",
                $"""
                breached IN ({QualityVocabulary.Of<QualityThresholdKey>()})
                AND owner IN ({QualityVocabulary.Of<QualityIncidentOwner>()})
                AND state IN ({QualityVocabulary.Of<QualityIncidentState>()})
                AND subject_kind IN ({QualityVocabulary.Of<QualitySubjectKind>()})
                """);
            table.HasCheckConstraint(
                "ck_quality_incident_classification",
                $"(owner = '{nameof(QualityIncidentOwner.Quality)}') = (classification IS NULL)");
            table.HasCheckConstraint(
                "ck_quality_incident_applied",
                """
                applied_observations >= 0
                AND (applied_provisional OR applied_observations > 0)
                """);
            table.HasCheckConstraint(
                "ck_quality_incident_lifecycle",
                $"""
                ((acknowledged_at IS NULL) = (acknowledged_by IS NULL))
                AND (acknowledged_at IS NULL OR notified_at IS NOT NULL)
                AND (notified_at IS NULL OR notified_at >= detected_at)
                AND (acknowledged_at IS NULL OR acknowledged_at >= notified_at)
                AND (resolved_at IS NULL OR resolved_at >= detected_at)
                AND ((state = '{nameof(QualityIncidentState.Resolved)}') = (resolved_at IS NOT NULL))
                AND ((state = '{nameof(QualityIncidentState.Acknowledged)}')
                    = (acknowledged_at IS NOT NULL AND resolved_at IS NULL))
                AND ((state = '{nameof(QualityIncidentState.Notified)}')
                    = (notified_at IS NOT NULL AND acknowledged_at IS NULL AND resolved_at IS NULL))
                AND ((state = '{nameof(QualityIncidentState.Detected)}')
                    = (notified_at IS NULL AND resolved_at IS NULL))
                """);
        });

        builder.HasKey(incident => incident.Id);

        builder.Property(incident => incident.Id)
            .HasConversion(id => id.Value, stored => new QualityIncidentId(stored))
            .HasColumnName("id");

        builder.Property(incident => incident.DetectedAt).IsRequired();

        builder.Property(incident => incident.Breached)
            .HasConversion<string>()
            .HasMaxLength(QualityVocabulary.NameLength)
            .IsRequired();

        builder.ComplexProperty(incident => incident.Subject, subject =>
        {
            subject.Property(named => named.Kind)
                .HasConversion<string>()
                .HasColumnName("subject_kind")
                .HasMaxLength(QualityVocabulary.NameLength);

            subject.Property(named => named.Key)
                .HasColumnName("subject_key")
                .HasMaxLength(QualitySubject.KeyMaxLength);
        });

        builder.Property(incident => incident.Observed).IsRequired();

        builder.Property(incident => incident.Owner)
            .HasConversion<string>()
            .HasMaxLength(QualityVocabulary.NameLength)
            .IsRequired();

        builder.Property(incident => incident.Classification)
            .HasMaxLength(QualityIncident.ClassificationMaxLength);

        builder.ComplexProperty(incident => incident.Applied, applied =>
        {
            applied.Property(value => value.Default).HasColumnName("applied_default");
            applied.Property(value => value.Current).HasColumnName("applied_current");
            applied.Property(value => value.Provisional).HasColumnName("applied_provisional");
            applied.Property(value => value.Observations).HasColumnName("applied_observations");
            applied.Property(value => value.UpdatedAt).HasColumnName("applied_updated_at");

            applied.Ignore(value => value.IsAsShipped);
        });

        builder.Property(incident => incident.State)
            .HasConversion<string>()
            .HasMaxLength(QualityVocabulary.NameLength)
            .IsRequired();

        builder.Property(incident => incident.NotifiedAt);
        builder.Property(incident => incident.AcknowledgedAt);

        builder.Property(incident => incident.AcknowledgedBy)
            .HasMaxLength(QualityIncident.AcknowledgedByMaxLength);

        builder.Property(incident => incident.ResolvedAt);

        builder.Ignore(incident => incident.Restated);
        builder.Ignore(incident => incident.HasSettled);

        builder.HasIndex(incident => incident.DetectedAt)
            .HasFilter("resolved_at IS NULL AND acknowledged_at IS NULL")
            .HasDatabaseName(UnsettledIndexName);
    }
}
