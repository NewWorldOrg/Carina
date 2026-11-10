using Carina.Domain.Programmes;

namespace Carina.Domain.Notifications;

/// <summary>
/// The programmes asked to be told of. What is read is a copy: a row changes only when it is saved.
/// </summary>
public interface IProgrammeWatchRepository
{
    Task<ProgrammeWatch?> FindAsync(ProgrammeWatchId id, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the one row held for a broadcast, whatever state it is in.
    /// </summary>
    Task<ProgrammeWatch?> FindOfAsync(ProgrammeId programme, CancellationToken cancellationToken);

    /// <summary>
    /// Lists every row held, earliest start first.
    /// </summary>
    Task<IReadOnlyList<ProgrammeWatch>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists the rows a look still has to turn at <paramref name="at"/>: those that wait, and those told of whose
    /// broadcast has not ended, earliest start first.
    /// </summary>
    Task<IReadOnlyList<ProgrammeWatch>> ListFollowedAsync(DateTime at, CancellationToken cancellationToken);

    Task AddAsync(ProgrammeWatch watch, CancellationToken cancellationToken);

    Task SaveAsync(ProgrammeWatch watch, CancellationToken cancellationToken);

    Task RemoveAsync(ProgrammeWatch watch, CancellationToken cancellationToken);

    /// <summary>
    /// Clears away the rows that no longer wait and whose broadcast ended before <paramref name="before"/>, and says
    /// how many went.
    /// </summary>
    Task<int> RemoveSettledBeforeAsync(DateTime before, CancellationToken cancellationToken);
}
