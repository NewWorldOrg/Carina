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

        DriverCall<IReadOnlyList<StorageRootDto>> roots = await declared.ReadAsync(cancellationToken);

        if (!roots.TryGetValue(out IReadOnlyList<StorageRootDto>? declaredRoots))
        {
            return ServiceResult<EncodeDestination, EncodingFailure>.Failure(
                "The set of output roots cannot be read while the driver does not answer, so no destination is saved: "
                + (roots.Failure ?? roots.Problem?.Title ?? "the driver answered without saying anything."),
                roots.Outcome is DriverCallOutcome.Unreachable ? EncodingFailure.DriverUnreachable : EncodingFailure.DriverRefused);
        }

        IReadOnlyList<EncodeProfile> defined = await profiles.ListAsync(cancellationToken);
        IReadOnlyList<EncodeRefusal> refusals = EncodeValidation.WhatRefusesTheDestination(
            draft,
            declaredRoots,
            places.Held,
            [.. defined.Select(profile => profile.Id)]);

        if (refusals.Count > 0)
        {
            return ServiceResult<EncodeDestination, EncodingFailure>.Failure(EncodeRefusals.Describe(refusals), EncodingFailure.Refused);
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
}
