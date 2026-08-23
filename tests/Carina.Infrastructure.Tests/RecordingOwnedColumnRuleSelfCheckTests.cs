using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Carina.Infrastructure.Tests;

public sealed class RecordingOwnedColumnRuleSelfCheckTests
{
    private sealed class ClaimWritingDbContext(DbContextOptions<CarinaDbContext> options) : CarinaDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Reservation>()
                .Property(reservation => reservation.StartedAt)
                .Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Save);
        }
    }

    private sealed class OutcomeWritingDbContext(DbContextOptions<CarinaDbContext> options) : CarinaDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Reservation>()
                .Property(reservation => reservation.RecordingOutcome)
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Save);
        }
    }

    [Fact]
    public void DetectsAReservationThatCanWriteTheClaimOnInsert()
    {
        using var context = new ClaimWritingDbContext(ReservationModel.Options());

        Assert.Equal(
            ["reservation.started_at"],
            RecordingOwnedColumnRules.WritableThroughTheChangeTracker(context.Model));
    }

    [Fact]
    public void DetectsAReservationThatCanWriteTheOutcomeOnUpdate()
    {
        using var context = new OutcomeWritingDbContext(ReservationModel.Options());

        Assert.Equal(
            ["reservation.recording_outcome"],
            RecordingOwnedColumnRules.WritableThroughTheChangeTracker(context.Model));
    }

    [Fact]
    public void FindsBothColumnsInEitherModelSoAnEmptyAnswerIsNeverAnEmptySearch()
    {
        using var context = new ClaimWritingDbContext(ReservationModel.Options());

        Assert.Equal(
            ["reservation.recording_outcome", "reservation.started_at"],
            RecordingOwnedColumnRules.Found(context.Model));
    }
}
