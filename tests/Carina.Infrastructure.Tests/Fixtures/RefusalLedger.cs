using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Tests.Fixtures.Reservations;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Fixtures.Recordings;

internal sealed class RefusalLedger
{
    public HeldReservations Reservations { get; } = new();

    public HeldOutcomes Outcomes { get; } = new();

    public RememberedTuneReports Tuning { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public RecordingRefusalReporter Reporter
        => new(
            Reservations,
            new ReservationOutcomeDomainService(Outcomes, new NoRecordings()),
            Tuning,
            NullLogger<RecordingRefusalReporter>.Instance);

    public RecordingRetries Retries(
        IAnnouncedProgrammes programmes,
        DiskPrecheckService disks,
        RecordingSettings settings,
        RetryPolicy? policy = null)
        => new(
            Reservations,
            Outcomes,
            Candidates,
            programmes,
            disks,
            settings,
            policy ?? RetryPolicy.Default,
            NullLogger<RecordingRetries>.Instance);

    public RefusalLedger Knowing(params RecordingTick[] due)
    {
        Reservations.Standing([.. due.Select(tick => RecordingTickFixture.Planned(tick))]);

        return this;
    }
}
