namespace Carina.Domain.Notifications;

/// <summary>
/// What became of withdrawing a programme asked to be told of.
/// </summary>
public enum ProgrammeWatchWithdrawal
{
    Withdrawn = 1,

    AlreadyWithdrawn = 2,

    Settled = 3,
}
