using Carina.Domain.Auth;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class LocalAccountConfiguration : IEntityTypeConfiguration<LocalAccount>
{
    public void Configure(EntityTypeBuilder<LocalAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "local_account",
            table => table.HasCheckConstraint(
                "ck_local_account_single_row",
                $"id = {LocalAccount.TheOnlyRow} AND username <> '' AND password_changed_at >= created_at"));

        builder.HasKey(account => account.Id);

        builder.Property(account => account.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(account => account.Username)
            .HasMaxLength(LocalAccount.LongestUsername)
            .IsRequired();

        builder.Property(account => account.PasswordHash)
            .HasConversion(hash => hash.Value, value => new PasswordHash(value))
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(account => account.CreatedAt).IsRequired();
        builder.Property(account => account.PasswordChangedAt).IsRequired();
    }
}
