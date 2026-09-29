using Carina.Domain.Auth;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class OidcSettingsConfiguration : IEntityTypeConfiguration<OidcSettings>
{
    public const string SealedSecret = "SealedClientSecret";

    public const string UnsealedSecret = "UnsealedClientSecret";

    public void Configure(EntityTypeBuilder<OidcSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "oidc_config",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_oidc_config_single_row",
                    $"id = {OidcSettings.TheOnlyRow}");
                table.HasCheckConstraint(
                    "ck_oidc_config_whole",
                    "(discovery_url IS NULL AND client_id IS NULL AND client_secret IS NULL AND client_secret_sealed IS NULL)"
                    + " OR (discovery_url IS NOT NULL AND client_id IS NOT NULL"
                    + " AND (client_secret IS NULL) <> (client_secret_sealed IS NULL))");
            });

        builder.HasKey(settings => settings.Id);

        builder.Property(settings => settings.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(settings => settings.DiscoveryUrl)
            .HasMaxLength(OidcSettings.LongestDiscoveryUrl);

        builder.Property(settings => settings.ClientId)
            .HasMaxLength(OidcSettings.LongestClientId);

        builder.Ignore(settings => settings.ClientSecret);

        builder.Property<string?>(SealedSecret)
            .HasColumnName("client_secret_sealed");

        builder.Property<string?>(UnsealedSecret)
            .HasColumnName("client_secret")
            .HasMaxLength(512);

        builder.PrimitiveCollection(settings => settings.AllowedGroups)
            .HasDefaultValueSql("'{}'")
            .IsRequired();

        builder.PrimitiveCollection(settings => settings.AllowedHostedDomains)
            .HasDefaultValueSql("'{}'")
            .IsRequired();

        builder.Property(settings => settings.UpdatedAt).IsRequired();
    }
}
