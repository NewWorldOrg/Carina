using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class EncodeDestinationConfiguration : IEntityTypeConfiguration<EncodeDestination>
{
    public void Configure(EntityTypeBuilder<EncodeDestination> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("encode_destination", table =>
        {
            table.HasCheckConstraint("ck_encode_destination_label", "btrim(label) = label AND length(label) > 0");
            table.HasCheckConstraint(
                "ck_encode_destination_output_root",
                EncodeVocabulary.ASingleName("output_root"));
        });

        builder.HasKey(destination => destination.Id);

        builder.Property(destination => destination.Id)
            .HasConversion(id => id.Value, value => new EncodeDestinationId(value))
            .HasColumnName("id");

        builder.Property(destination => destination.Label)
            .HasConversion(label => label.Value, value => new EncodeLabel(value))
            .HasMaxLength(EncodeLabel.Longest)
            .IsRequired();

        builder.Property(destination => destination.OutputRoot)
            .HasConversion(root => root.Value, value => new OutputRoot(value))
            .HasMaxLength(Carina.Domain.Recordings.OutputRoot.MaxLength)
            .IsRequired();

        builder.Property(destination => destination.DefaultProfileId)
            .HasConversion(id => id.Value, value => new EncodeProfileId(value))
            .HasColumnName("default_profile_id")
            .IsRequired();

        builder.Property(destination => destination.DefinedAt).IsRequired();

        builder.HasOne<EncodeProfile>()
            .WithMany()
            .HasForeignKey(destination => destination.DefaultProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
