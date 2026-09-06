using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;

namespace Carina.Api.Services;

/// <summary>
/// A destination names a root out of the set the storage surface declares (BR-EV-001), so the set
/// is read at the moment of saving and nothing is saved while the driver cannot say what it
/// declares. Of that set, only a root this process holds for writing is accepted.
/// </summary>
public sealed class EncodeDestinationService(
    IEncodeDestinationRepository destinations,
    IEncodeProfileRepository profiles,
    IEncodeJobRepository jobs,
    OutputRootDeclarations declared,
    EncodePlaces places,
    TimeProvider clock)
{
    public async Task<ServiceResult<IReadOnlyList<EncodeDestination>>> ListAsync(CancellationToken cancellationToken)
        => ServiceResult<IReadOnlyList<EncodeDestination>>.Success(await destinations.ListAsync(cancellationToken));

    public async Task<ServiceResult<EncodeDestination, EncodingFailure>> DefineAsync(
        EncodeDestinationDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        ServiceResult<IReadOnlyList<EncodeRefusal>, EncodingFailure> weighed = await WeighedAsync(draft, cancellationToken);

        if (!weighed.IsSuccess)
        {
            return Failure<EncodeDestination>(weighed.ErrorMessage!, weighed.ErrorType);
        }

        if (weighed.Data is { Count: > 0 } refusals)
        {
            return Failure<EncodeDestination>(EncodeRefusals.Describe(refusals), EncodingFailure.Refused);
        }

        EncodeDestination destination = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel(draft.Label!),
            new OutputRoot(draft.OutputRoot!),
            draft.DefaultProfileId!,
            clock.GetUtcNow().UtcDateTime);

        await destinations.AddAsync(destination, cancellationToken);

        return ServiceResult<EncodeDestination, EncodingFailure>.Success(destination);
    }

    public async Task<ServiceResult<EncodeDestination, EncodingFailure>> ReviseAsync(
        EncodeDestinationId id,
        EncodeDestinationDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(draft);

        if (await destinations.FindAsync(id, cancellationToken) is not { } destination)
        {
            return Missing<EncodeDestination>(id);
        }

        if (destination.IsRetired)
        {
            return Failure<EncodeDestination>(EncodeSaying.Retired(destination), EncodingFailure.AlreadyRetired);
        }

        ServiceResult<IReadOnlyList<EncodeRefusal>, EncodingFailure> weighed = await WeighedAsync(draft, cancellationToken);

        if (!weighed.IsSuccess)
        {
            return Failure<EncodeDestination>(weighed.ErrorMessage!, weighed.ErrorType);
        }

        if (weighed.Data is { Count: > 0 } refusals)
        {
            return Failure<EncodeDestination>(EncodeRefusals.Describe(refusals), EncodingFailure.Refused);
        }

        EncodeHold hold = await jobs.HoldOnDestinationAsync(id, cancellationToken);

        if (hold.Unfinished is { } underway)
        {
            return Failure<EncodeDestination>(
                EncodeSaying.Underway("Destination", destination.Id.Wire, underway),
                EncodingFailure.StillInFlight);
        }

        destination.Revise(new EncodeLabel(draft.Label!), new OutputRoot(draft.OutputRoot!), draft.DefaultProfileId!);
        await destinations.SaveAsync(destination, cancellationToken);

        return ServiceResult<EncodeDestination, EncodingFailure>.Success(destination);
    }

    public async Task<ServiceResult<EncodeDefinitionRemoved, EncodingFailure>> RemoveAsync(
        EncodeDestinationId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await destinations.FindAsync(id, cancellationToken) is not { } destination)
        {
            return Missing<EncodeDefinitionRemoved>(id);
        }

        if (destination.IsRetired)
        {
            return Failure<EncodeDefinitionRemoved>(EncodeSaying.Retired(destination), EncodingFailure.AlreadyRetired);
        }

        EncodeHold hold = await jobs.HoldOnDestinationAsync(id, cancellationToken);

        if (hold.Unfinished is { } underway)
        {
            return Failure<EncodeDefinitionRemoved>(
                EncodeSaying.Underway("Destination", destination.Id.Wire, underway),
                EncodingFailure.StillInFlight);
        }

        IReadOnlyList<EncodeDestination> placed = await destinations.ListAsync(cancellationToken);

        if (placed.Count(other => !other.IsRetired) is 1)
        {
            return Failure<EncodeDefinitionRemoved>(
                $"Destination {destination.Id.Wire} is the only one left, and a machine with nowhere to put an artefact encodes nothing; define the one that replaces it first.",
                EncodingFailure.TheLastOne);
        }

        if (!hold.Any)
        {
            await destinations.RemoveAsync(destination, cancellationToken);

            return Gone(new EncodeDefinitionRemoved(destination.Id.Value, EncodeRemoval.Deleted, null));
        }

        destination.Retire(clock.GetUtcNow().UtcDateTime);
        await destinations.SaveAsync(destination, cancellationToken);

        return Gone(new EncodeDefinitionRemoved(destination.Id.Value, EncodeRemoval.Retired, destination.RetiredAt));
    }

    private async Task<ServiceResult<IReadOnlyList<EncodeRefusal>, EncodingFailure>> WeighedAsync(
        EncodeDestinationDraft draft,
        CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<StorageRootDto>> roots = await declared.ReadAsync(cancellationToken);

        if (!roots.TryGetValue(out IReadOnlyList<StorageRootDto>? declaredRoots))
        {
            return ServiceResult<IReadOnlyList<EncodeRefusal>, EncodingFailure>.Failure(
                "The set of output roots cannot be read while the driver does not answer, so no destination is saved: "
                + (roots.Failure ?? roots.Problem?.Title ?? "the driver answered without saying anything."),
                roots.Outcome is DriverCallOutcome.Unreachable ? EncodingFailure.DriverUnreachable : EncodingFailure.DriverRefused);
        }

        IReadOnlyList<EncodeProfile> defined = await profiles.ListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<EncodeRefusal>, EncodingFailure>.Success(EncodeValidation.WhatRefusesTheDestination(
            draft,
            declaredRoots,
            places.Held,
            [.. defined.Where(profile => !profile.IsRetired).Select(profile => profile.Id)]));
    }

    private static ServiceResult<EncodeDefinitionRemoved, EncodingFailure> Gone(EncodeDefinitionRemoved removed)
        => ServiceResult<EncodeDefinitionRemoved, EncodingFailure>.Success(removed);

    private static ServiceResult<T, EncodingFailure> Missing<T>(EncodeDestinationId id)
        => Failure<T>($"No destination {id.Wire} is defined.", EncodingFailure.NoSuchDestination);

    private static ServiceResult<T, EncodingFailure> Failure<T>(string saying, EncodingFailure failure)
        => ServiceResult<T, EncodingFailure>.Failure(saying, failure);
}
