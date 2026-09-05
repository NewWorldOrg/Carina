using Carina.Domain.Encodings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class EncodeProfileConfiguration : IEntityTypeConfiguration<EncodeProfile>
{
    public void Configure(EntityTypeBuilder<EncodeProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("encode_profile", table =>
        {
            table.HasCheckConstraint("ck_encode_profile_label", "btrim(label) = label AND length(label) > 0");
            table.HasCheckConstraint("ck_encode_profile_codec", $"codec IN ({EncodeVocabulary.Of<EncodeCodec>()})");
            table.HasCheckConstraint(
                "ck_encode_profile_resolution",
                $"resolution IN ({EncodeVocabulary.Of<EncodeResolution>()})");
            table.HasCheckConstraint(
                "ck_encode_profile_deinterlace",
                $"deinterlace IN ({EncodeVocabulary.Of<Deinterlace>()})");
            table.HasCheckConstraint(
                "ck_encode_profile_rate_control",
                $"""
                rate_factor BETWEEN {ConstantRateFactor.Finest} AND {ConstantRateFactor.Coarsest}
                AND quantiser BETWEEN {ConstantQuantiser.Finest} AND {ConstantQuantiser.Coarsest}
                """);
        });

        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Id)
            .HasConversion(id => id.Value, value => new EncodeProfileId(value))
            .HasColumnName("id");

        builder.Property(profile => profile.Label)
            .HasConversion(label => label.Value, value => new EncodeLabel(value))
            .HasMaxLength(EncodeLabel.Longest)
            .IsRequired();

        builder.Property(profile => profile.Codec).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(profile => profile.Resolution).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(profile => profile.Deinterlace).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(profile => profile.SoftwareRateControl)
            .HasConversion(control => control.RateFactor, value => new ConstantRateFactor(value))
            .HasColumnName("rate_factor")
            .IsRequired();

        builder.Property(profile => profile.VaapiRateControl)
            .HasConversion(control => control.Quantiser, value => new ConstantQuantiser(value))
            .HasColumnName("quantiser")
            .IsRequired();

        builder.Property(profile => profile.DefinedAt).IsRequired();
    }
}
