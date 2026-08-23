using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

public sealed class RecordingOwnedColumnRuleTests
{
    [Fact]
    public void TheReservationCannotWriteTheClaimOrTheOutcomeThroughTheChangeTracker()
    {
        using CarinaDbContext context = ReservationModel.Carina();

        Assert.Empty(RecordingOwnedColumnRules.WritableThroughTheChangeTracker(context.Model));
    }

    [Fact]
    public void ThoseColumnsAreOnTheReservationForTheRuleToFind()
    {
        using CarinaDbContext context = ReservationModel.Carina();

        Assert.Equal(
            ["reservation.recording_outcome", "reservation.started_at"],
            RecordingOwnedColumnRules.Found(context.Model));
    }

    [Fact]
    public void TheLedgerKeepsItsOwnOutcomeColumnRatherThanBorrowingTheReservationsOne()
    {
        using CarinaDbContext context = ReservationModel.Carina();

        Assert.DoesNotContain(
            "reservation_outcome.recording_outcome",
            RecordingOwnedColumnRules.WritableThroughTheChangeTracker(context.Model),
            StringComparer.Ordinal);
    }
}
