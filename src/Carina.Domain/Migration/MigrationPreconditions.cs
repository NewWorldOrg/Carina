using Carina.Domain.Encodings;

namespace Carina.Domain.Migration;

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
