namespace Carina.Domain.Encodings;

public enum EncodeDefinitionFate
{
    Deleted = 1,

    Retired = 2,
}

public enum EncodeDefinitionRefusal
{
    AlreadyRetired = 1,

    StillInFlight = 2,

    StillTheDefault = 3,

    TheLastOne = 4,
}

public sealed record EncodeDefinitionRemoval
{
    private EncodeDefinitionRemoval(
        EncodeDefinitionFate? fate,
        EncodeDefinitionRefusal? refusal,
        EncodeJob? underway,
        EncodeDestination? namedBy)
    {
        Fate = fate;
        Refusal = refusal;
        Underway = underway;
        NamedBy = namedBy;
    }

    public EncodeDefinitionFate? Fate { get; }

    public EncodeDefinitionRefusal? Refusal { get; }

    public EncodeJob? Underway { get; }

    public EncodeDestination? NamedBy { get; }

    public static EncodeDefinitionRemoval Gone(EncodeDefinitionFate fate) => new(fate, null, null, null);

    public static EncodeDefinitionRemoval Refused(EncodeDefinitionRefusal refusal) => new(null, refusal, null, null);

    public static EncodeDefinitionRemoval StillInFlight(EncodeJob underway)
    {
        ArgumentNullException.ThrowIfNull(underway);

        return new EncodeDefinitionRemoval(null, EncodeDefinitionRefusal.StillInFlight, underway, null);
    }

    public static EncodeDefinitionRemoval StillTheDefault(EncodeDestination namedBy)
    {
        ArgumentNullException.ThrowIfNull(namedBy);

        return new EncodeDefinitionRemoval(null, EncodeDefinitionRefusal.StillTheDefault, null, namedBy);
    }
}

public sealed class EncodeDefinitionDomainService(
    IEncodeJobRepository jobs,
    IEncodeDestinationRepository destinations)
{
    public async Task<EncodeJob?> UnderwayWithAsync(EncodeProfileId profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return (await jobs.HoldOnProfileAsync(profile, cancellationToken)).Unfinished;
    }

    public async Task<EncodeJob?> UnderwayWithAsync(EncodeDestinationId destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return (await jobs.HoldOnDestinationAsync(destination, cancellationToken)).Unfinished;
    }

    public async Task<EncodeDefinitionRemoval> RemoveAsync(EncodeProfile profile, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return await RemovedAsync(
            profile.IsRetired,
            () => jobs.HoldOnProfileAsync(profile.Id, cancellationToken),
            async () => await StillTheDefaultAsync(profile.Id, cancellationToken),
            profile.Retire,
            at);
    }

    public async Task<EncodeDefinitionRemoval> RemoveAsync(
        EncodeDestination destination,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return await RemovedAsync(
            destination.IsRetired,
            () => jobs.HoldOnDestinationAsync(destination.Id, cancellationToken),
            async () => await TheLastOneAsync(cancellationToken),
            destination.Retire,
            at);
    }

    private static async Task<EncodeDefinitionRemoval> RemovedAsync(
        bool retired,
        Func<Task<EncodeHold>> hold,
        Func<Task<EncodeDefinitionRemoval?>> stillWanted,
        Action<DateTime> retire,
        DateTime at)
    {
        if (retired)
        {
            return EncodeDefinitionRemoval.Refused(EncodeDefinitionRefusal.AlreadyRetired);
        }

        EncodeHold held = await hold();

        if (held.Unfinished is { } underway)
        {
            return EncodeDefinitionRemoval.StillInFlight(underway);
        }

        if (await stillWanted() is { } refused)
        {
            return refused;
        }

        if (!held.Any)
        {
            return EncodeDefinitionRemoval.Gone(EncodeDefinitionFate.Deleted);
        }

        retire(at);

        return EncodeDefinitionRemoval.Gone(EncodeDefinitionFate.Retired);
    }

    private async Task<EncodeDefinitionRemoval?> StillTheDefaultAsync(EncodeProfileId profile, CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeDestination> placed = await destinations.ListAsync(cancellationToken);

        return placed.FirstOrDefault(destination => !destination.IsRetired && destination.DefaultProfileId.Equals(profile)) is { } naming
            ? EncodeDefinitionRemoval.StillTheDefault(naming)
            : null;
    }

    private async Task<EncodeDefinitionRemoval?> TheLastOneAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeDestination> placed = await destinations.ListAsync(cancellationToken);

        return placed.Count(destination => !destination.IsRetired) is 1
            ? EncodeDefinitionRemoval.Refused(EncodeDefinitionRefusal.TheLastOne)
            : null;
    }
}
