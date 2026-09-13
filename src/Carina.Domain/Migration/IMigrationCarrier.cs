using Carina.Domain.Recordings;

namespace Carina.Domain.Migration;

public interface IMigrationCarrier
{
    OutputRoot Into { get; }

    Task<MigrationRootStanding> StandingAsync(CancellationToken cancellationToken);

    Task<MigrationCarryStanding> WouldCarryAsync(CancellationToken cancellationToken);

    Task<MigrationCarry> CarryAsync(
        string sourcePath,
        RecordingFileName name,
        CancellationToken cancellationToken);
}
