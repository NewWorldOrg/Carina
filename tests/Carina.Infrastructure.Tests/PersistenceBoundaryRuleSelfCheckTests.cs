using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

internal sealed class ChannelDefinition
{
    public int Id { get; set; }
}

internal sealed class Reservation
{
    public int Id { get; set; }
    public int ChannelDefinitionId { get; set; }
    public int ReservationRuleId { get; set; }
}

internal sealed class ReservationRule
{
    public int Id { get; set; }
}

internal sealed class EpgProgramme
{
    public int Id { get; set; }
}

internal sealed class RecordingJob
{
    public int Id { get; set; }
    public int EpgProgrammeId { get; set; }
}

public sealed class PersistenceBoundaryRuleSelfCheckTests
{
    private sealed class ViolatingDbContext(DbContextOptions<CarinaDbContext> options) : CarinaDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ChannelDefinition>();
            modelBuilder.Entity<ReservationRule>();
            modelBuilder.Entity<Reservation>(reservation =>
            {
                reservation.HasOne<ChannelDefinition>().WithMany()
                    .HasForeignKey(entity => entity.ChannelDefinitionId);
                reservation.HasOne<ReservationRule>().WithMany()
                    .HasForeignKey(entity => entity.ReservationRuleId);
            });
            modelBuilder.Entity<EpgProgramme>();
            modelBuilder.Entity<RecordingJob>(job =>
                job.HasOne<EpgProgramme>().WithMany().HasForeignKey(entity => entity.EpgProgrammeId));
        }
    }

    private static ViolatingDbContext Violating()
    {
        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase("Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");

        return new ViolatingDbContext(builder.Options);
    }

    [Fact]
    public void DetectsAReservationThatHoldsAForeignKeyToAChannelDefinition()
    {
        using var context = Violating();

        Assert.Contains(
            "reservation -> channel_definition",
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact]
    public void DetectsAForeignKeyIntoTheProgrammeCache()
    {
        using var context = Violating();

        Assert.Contains(
            "recording_job -> epg_programme",
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact]
    public void LeavesForeignKeysInsideTheReservationAggregateAlone()
    {
        using var context = Violating();

        Assert.Equal(
            ["recording_job -> epg_programme", "reservation -> channel_definition"],
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }
}
