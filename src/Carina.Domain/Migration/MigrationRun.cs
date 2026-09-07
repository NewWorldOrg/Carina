using Carina.Domain.Base;

namespace Carina.Domain.Migration;

public sealed class MigrationRun
{
    private MigrationRun()
    {
    }

    public MigrationRunId Id { get; private set; } = null!;

    public MigrationSourceName Source { get; private set; } = null!;

    public MigrationPass Pass { get; private set; }

    public DateTime StartedAt { get; private set; }

    public DateTime FinishedAt { get; private set; }

    public static MigrationRun Rehydrate(
        MigrationRunId id,
        MigrationSourceName source,
        MigrationPass pass,
        DateTime startedAt,
        DateTime finishedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(source);

        if (!Enum.IsDefined(pass))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pass),
                pass,
                "A run is either a rehearsal or the real thing.");
        }

        DateTime began = UtcTimes.Required(startedAt, nameof(startedAt));
        DateTime ended = UtcTimes.Required(finishedAt, nameof(finishedAt));

        if (ended < began)
        {
            throw new ArgumentException("A run finishes after it starts.", nameof(finishedAt));
        }

        return new MigrationRun
        {
            Id = id,
            Source = source,
            Pass = pass,
            StartedAt = began,
            FinishedAt = ended,
        };
    }
}
