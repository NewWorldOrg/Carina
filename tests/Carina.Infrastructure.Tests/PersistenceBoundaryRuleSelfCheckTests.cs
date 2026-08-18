using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.Channels;
using Carina.Infrastructure.Tests.Fixtures.Library;
using Carina.Infrastructure.Tests.Fixtures.Programmes;
using Carina.Infrastructure.Tests.Fixtures.Recordings;
using Carina.Infrastructure.Tests.Fixtures.Reservations;
using Carina.Infrastructure.Tests.Fixtures.Rules;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

public sealed class PersistenceBoundaryRuleSelfCheckTests
{
    private static readonly string[] FamilyPrefixes = ["reservation", "channel", "programme", "epg"];

    private sealed class ViolatingDbContext(DbContextOptions<CarinaDbContext> options) : CarinaDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ChannelLineup>();
            modelBuilder.Entity<BookingRule>();
            modelBuilder.Entity<Booking>(booking =>
            {
                booking.HasOne<ChannelLineup>().WithMany()
                    .HasForeignKey(entity => entity.ChannelLineupId);
                booking.HasOne<BookingRule>().WithMany()
                    .HasForeignKey(entity => entity.BookingRuleId);
            });
            modelBuilder.Entity<GuideEntry>();
            modelBuilder.Entity<RecordingJob>(job =>
                job.HasOne<GuideEntry>().WithMany().HasForeignKey(entity => entity.GuideEntryId));
            modelBuilder.Entity<ShelfItem>();
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
        using ViolatingDbContext context = Violating();

        Assert.Contains(
            "booking -> channel_lineup",
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact]
    public void DetectsAForeignKeyIntoTheProgrammeCache()
    {
        using ViolatingDbContext context = Violating();

        Assert.Contains(
            "recording_job -> guide_entry",
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact]
    public void LeavesForeignKeysInsideTheReservationAggregateAlone()
    {
        using ViolatingDbContext context = Violating();

        Assert.Equal(
            ["booking -> channel_lineup", "recording_job -> guide_entry"],
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact]
    public void CatchesBreaksThatNoTableNameRevealsAsBelongingToTheirFamily()
    {
        using ViolatingDbContext context = Violating();

        Assert.DoesNotContain(
            new[] { "booking", "guide_entry", "recording_job" },
            table => FamilyPrefixes.Any(prefix => table.StartsWith(prefix, StringComparison.Ordinal)));
        Assert.Equal(
            ["booking -> channel_lineup", "recording_job -> guide_entry"],
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact]
    public void DetectsAnEntityThatBelongsToNoDeclaredFamily()
    {
        using ViolatingDbContext context = Violating();

        Assert.Equal(
            [$"{typeof(ShelfItem).FullName} (shelf_item)"],
            PersistenceBoundaryRules.UnclassifiedEntityTypes(context.Model));
    }
}
