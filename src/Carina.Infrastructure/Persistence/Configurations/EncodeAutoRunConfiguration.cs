using Carina.Domain.Encodings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class EncodeAutoRunConfiguration : IEntityTypeConfiguration<EncodeAutoRun>
{
    public const string TableName = "encode_auto_run";

    public const string SingleRowCheck = "ck_encode_auto_run_single_row";

    public const string CoresCheck = "ck_encode_auto_run_cores";

    public void Configure(EntityTypeBuilder<EncodeAutoRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(SingleRowCheck, $"id = {EncodeAutoRun.TheOnlyRow}");
            table.HasCheckConstraint(
                CoresCheck,
                $"most_cores >= {EncodeAutoRun.FewestCores} AND most_cores <= {EncodeAutoRun.MostCoresAnyMachineHas}");
        });

        builder.HasKey(autoRun => autoRun.Id);

        builder.Property(autoRun => autoRun.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(autoRun => autoRun.Automatically).IsRequired();

        builder.Property(autoRun => autoRun.MostCores).IsRequired();

        builder.Property(autoRun => autoRun.UpdatedAt).IsRequired();
    }
}
