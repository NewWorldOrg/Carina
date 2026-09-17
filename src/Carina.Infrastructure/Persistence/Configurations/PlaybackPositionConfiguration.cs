using Carina.Domain.Auth;
using Carina.Domain.Recordings;
using Carina.Domain.Viewing;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class PlaybackPositionConfiguration : IEntityTypeConfiguration<PlaybackPosition>
{
    public const string TableName = "playback_position";

    public const string PositionCheckName = "ck_playback_position_position";

    public void Configure(EntityTypeBuilder<PlaybackPosition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(PositionCheckName, "position_ms >= 0");
            table.HasCheckConstraint("ck_playback_position_subject", "subject <> ''");
        });

        builder.HasKey(position => new { position.RecordingId, position.Viewer });

        builder.Property(position => position.RecordingId)
            .HasConversion(id => id.Value, value => new RecordingId(value))
            .HasColumnName("recording_id");

        builder.Property(position => position.Viewer)
            .HasConversion(viewer => viewer.Value, value => new Subject(value))
            .HasColumnName("subject")
            .HasMaxLength(Subject.LongestValue);

        builder.Property(position => position.PositionMs).HasColumnName("position_ms").IsRequired();
        builder.Property(position => position.UpdatedAt).IsRequired();

        builder.Ignore(position => position.Position);
    }
}
