using Carina.Domain.Encodings;

namespace Carina.Domain.Migration;

/// <summary>
/// What a run finds before it carries anything: how the new root stands, where what it carries is encoded, and what a
/// link from the source into the new root would meet. A run for real starts only into an empty new root, with one
/// destination and its default profile to queue every recording it carries for encoding, and where a link can be
/// made; a rehearsal is never stopped, and says what a run for real would meet instead.
/// </summary>
public sealed record MigrationPreconditions
{
    public MigrationPreconditions(
        MigrationRootStanding newRoot,
        EncodeUnasked whereEncodesGo,
        MigrationCarryStanding carrying)
    {
        ArgumentNullException.ThrowIfNull(whereEncodesGo);

        NewRoot = Enum.IsDefined(newRoot)
            ? newRoot
            : throw new ArgumentOutOfRangeException(
                nameof(newRoot),
                newRoot,
                "How the new root stands is one of the things a run knows to name.");
        Carrying = Enum.IsDefined(carrying)
            ? carrying
            : throw new ArgumentOutOfRangeException(
                nameof(carrying),
                carrying,
                "What a link into the new root would meet is one of the things a run knows to name.");
        WhereEncodesGo = whereEncodesGo;
    }

    public MigrationRootStanding NewRoot { get; }

    public EncodeUnasked WhereEncodesGo { get; }

    public MigrationCarryStanding Carrying { get; }

    /// <summary>
    /// What stops a run of <paramref name="pass"/> before it carries anything, or none.
    /// </summary>
    public MigrationHindrance? Hindering(MigrationPass pass)
    {
        if (pass is not MigrationPass.ForReal)
        {
            return null;
        }

        if (NewRoot is not MigrationRootStanding.Empty)
        {
            return MigrationHindrance.TheNewRootIsNotEmptyOrNotThere;
        }

        if (!WhereEncodesGo.IsSettled)
        {
            return MigrationHindrance.NothingSettlesWhereEncodesGo;
        }

        return MigrationFindings.WouldStopARunForReal(MigrationFindings.Of(Carrying))
            ? MigrationHindrance.NoLinkCanBeMade
            : null;
    }
}
