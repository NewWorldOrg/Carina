using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Unit.Infra.Persistence;

[Trait("Category", "Unit")]
public sealed class ReservationConfigurationTests
{
    [Fact]
    public void TheReservationCannotWriteTheClaimOrTheOutcomeThroughTheChangeTracker()
    {
        using CarinaDbContext context = CarinaModel.Context();

        Assert.Empty(RecordingOwnedColumnRules.WritableThroughTheChangeTracker(context.Model));
    }

    [Fact]
    public void ThoseColumnsAreOnTheReservationForTheRuleToFind()
    {
        using CarinaDbContext context = CarinaModel.Context();

        Assert.Equal(
            ["reservation.recording_outcome", "reservation.started_at"],
            RecordingOwnedColumnRules.Found(context.Model));
    }

    [Fact]
    public void TheLedgerKeepsItsOwnOutcomeColumnRatherThanBorrowingTheReservationsOne()
    {
        using CarinaDbContext context = CarinaModel.Context();

        Assert.DoesNotContain(
            "reservation_outcome.recording_outcome",
            RecordingOwnedColumnRules.WritableThroughTheChangeTracker(context.Model),
            StringComparer.Ordinal);
    }
}
