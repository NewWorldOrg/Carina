using Carina.Domain.Auth;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "auth_session",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_auth_session_method",
                    "method IN ('Local', 'Oidc')");
                table.HasCheckConstraint(
                    "ck_auth_session_times",
                    "last_used_at >= created_at AND (revoked_at IS NULL OR revoked_at >= created_at)");
                table.HasCheckConstraint(
                    "ck_auth_session_device_label",
                    "device_label <> ''");
                table.HasCheckConstraint(
                    "ck_auth_session_display_name",
                    "display_name <> ''");
            });

        builder.HasKey(session => session.Id);

        builder.Property(session => session.Id)
            .HasConversion(id => id.Value, value => new SessionId(value))
            .HasMaxLength(SessionId.Length);

        builder.Property(session => session.Subject)
            .HasConversion(subject => subject.Value, value => new Subject(value))
            .HasMaxLength(Subject.LongestValue)
            .IsRequired();

        builder.Property(session => session.DisplayName)
            .HasMaxLength(AuthSession.LongestDisplayName)
            .IsRequired();

        builder.Property(session => session.Method)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(session => session.CreatedAt).IsRequired();
        builder.Property(session => session.LastUsedAt).IsRequired();

        builder.Property(session => session.DeviceLabel)
            .HasMaxLength(AuthSession.LongestDeviceLabel)
            .IsRequired();

        builder.Property(session => session.RevokedAt);

        builder.HasIndex(session => session.Subject);
        builder.HasIndex(session => session.LastUsedAt);
    }
}
