using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class CollectionEpochConfiguration : IEntityTypeConfiguration<CollectionEpoch>
{
    public void Configure(EntityTypeBuilder<CollectionEpoch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "collection_epoch",
            table => table.HasCheckConstraint(
                "ck_collection_epoch_single_row",
                $"id = {CollectionEpoch.TheOnlyRow} AND generation >= 1"));

        builder.HasKey(epoch => epoch.Id);

        builder.Property(epoch => epoch.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(epoch => epoch.Generation)
            .HasColumnName("generation")
            .IsRequired();

        builder.Property(epoch => epoch.AdvancedAt)
            .HasColumnName("advanced_at")
            .IsRequired();
    }
}
