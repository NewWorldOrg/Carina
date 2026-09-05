using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.Channels;
using Carina.Infrastructure.Tests.Fixtures.Encodings;
using Carina.Infrastructure.Tests.Fixtures.Library;
using Carina.Infrastructure.Tests.Fixtures.Programmes;
using Carina.Infrastructure.Tests.Fixtures.Quality;
using Carina.Infrastructure.Tests.Fixtures.Recordings;
using Carina.Infrastructure.Tests.Fixtures.Reservations;
using Carina.Infrastructure.Tests.Fixtures.Rules;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

public sealed class PersistenceBoundaryRuleSelfCheckTests
{
    private static readonly string[] FamilyPrefixes = ["reservation", "channel", "programme", "epg", "recording", "encode"];

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
            modelBuilder.Entity<GuideEntry>(entry =>
                entry.HasOne<ChannelLineup>().WithMany()
                    .HasForeignKey(entity => entity.ChannelLineupId));
            modelBuilder.Entity<RecordingJob>(job =>
                job.HasOne<GuideEntry>().WithMany().HasForeignKey(entity => entity.GuideEntryId));
            modelBuilder.Entity<TapeEntry>(tape =>
            {
                tape.HasOne<ChannelLineup>().WithMany().HasForeignKey(entity => entity.ChannelLineupId);
                tape.HasOne<Booking>().WithMany().HasForeignKey(entity => entity.BookingId);
            });
            modelBuilder.Entity<ShelfItem>();
            modelBuilder.Entity<BurnJob>(burn =>
                burn.HasOne<TapeEntry>().WithMany().HasForeignKey(entity => entity.TapeEntryId));
            modelBuilder.Entity<SignalTrace>(trace =>
                trace.HasOne<TapeEntry>().WithMany().HasForeignKey(entity => entity.TapeEntryId));
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
    public void DetectsAProgrammeCacheEntryThatHoldsAForeignKeyToAChannelDefinition()
    {
        using ViolatingDbContext context = Violating();

        Assert.Contains(
            "guide_entry -> channel_lineup",
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact]
    public void DetectsARecordingThatHoldsAForeignKeyToAChannelDefinition()
    {
        using ViolatingDbContext context = Violating();

        Assert.Contains(
            "tape_entry -> channel_lineup",
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact]
    public void DetectsARecordingThatWouldBeDraggedAwayWithTheReservation()
    {
        using ViolatingDbContext context = Violating();

        Assert.Contains(
            "tape_entry -> booking",
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact(DisplayName = "BR-QD-013: detects a quality table that holds a foreign key into the recording ledger")]
    public void DetectsAQualityTableThatHoldsAForeignKeyIntoTheRecordingLedger()
    {
        using ViolatingDbContext context = Violating();

        Assert.Contains(
            "signal_trace -> tape_entry",
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact(DisplayName = "BR-D-004: detects an encode job that holds a foreign key to the recording ledger")]
    public void DetectsAnEncodeJobThatHoldsAForeignKeyToTheRecordingLedger()
    {
        using ViolatingDbContext context = Violating();

        Assert.Contains(
            "burn_job -> tape_entry",
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact]
    public void LeavesForeignKeysInsideTheReservationAggregateAlone()
    {
        using ViolatingDbContext context = Violating();

        Assert.Equal(
            [
                "booking -> channel_lineup",
                "burn_job -> tape_entry",
                "guide_entry -> channel_lineup",
                "recording_job -> guide_entry",
                "signal_trace -> tape_entry",
                "tape_entry -> booking",
                "tape_entry -> channel_lineup",
            ],
            PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact]
    public void CatchesBreaksThatNoTableNameRevealsAsBelongingToTheirFamily()
    {
        using ViolatingDbContext context = Violating();

        Assert.DoesNotContain(
            new[] { "booking", "burn_job", "guide_entry", "signal_trace", "tape_entry" },
            table => FamilyPrefixes.Any(prefix => table.StartsWith(prefix, StringComparison.Ordinal)));
        Assert.Equal(
            [
                "booking -> channel_lineup",
                "burn_job -> tape_entry",
                "guide_entry -> channel_lineup",
                "recording_job -> guide_entry",
                "signal_trace -> tape_entry",
                "tape_entry -> booking",
                "tape_entry -> channel_lineup",
            ],
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
