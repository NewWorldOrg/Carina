using Carina.Domain.Reservations;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Tests.Reservations;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Recordings;

internal sealed class RefusalLedger
{
    public HeldReservations Reservations { get; } = new();

    public HeldOutcomes Outcomes { get; } = new();

    public RememberedTuneReports Tuning { get; } = new();

    public RecordingRefusalReporter Reporter
        => new(Reservations, Outcomes, Tuning, NullLogger<RecordingRefusalReporter>.Instance);

    public RefusalLedger Knowing(params RecordingTick[] due)
    {
        Reservations.Standing([.. due.Select(tick => RecordingTickFixture.Planned(tick))]);

        return this;
    }
}
