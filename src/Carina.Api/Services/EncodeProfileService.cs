using Carina.Api.Common;
using Carina.Domain.Encodings;

namespace Carina.Api.Services;

public enum EncodeRemoval
{
    Deleted = 1,

    Retired = 2,
}

public sealed record EncodeDefinitionRemoved(Guid Id, EncodeRemoval Removal, DateTime? RetiredAt);

public sealed class EncodeProfileService(
    IEncodeProfileRepository profiles,
    IEncodeDestinationRepository destinations,
    IEncodeJobRepository jobs,
    TimeProvider clock)
{
    public async Task<ServiceResult<IReadOnlyList<EncodeProfile>>> ListAsync(CancellationToken cancellationToken)
        => ServiceResult<IReadOnlyList<EncodeProfile>>.Success(await profiles.ListAsync(cancellationToken));

    public async Task<ServiceResult<EncodeProfile, EncodingFailure>> DefineAsync(
        EncodeProfileDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (EncodeValidation.WhatRefusesTheProfile(draft) is { Count: > 0 } refusals)
        {
            return Refused(refusals);
        }

        EncodeProfile defined = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel(draft.Label!),
            draft.Codec,
            draft.Resolution,
            draft.Deinterlace,
            new ConstantRateFactor(draft.RateFactor),
            new ConstantQuantiser(draft.Quantiser),
            clock.GetUtcNow().UtcDateTime);

        await profiles.AddAsync(defined, cancellationToken);

        return ServiceResult<EncodeProfile, EncodingFailure>.Success(defined);
    }

    public async Task<ServiceResult<EncodeProfile, EncodingFailure>> ReviseAsync(
        EncodeProfileId id,
        EncodeProfileDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(draft);

        if (await profiles.FindAsync(id, cancellationToken) is not { } profile)
        {
            return Missing<EncodeProfile>(id);
        }

        if (profile.IsRetired)
        {
            return Failure<EncodeProfile>(EncodeSaying.Retired(profile), EncodingFailure.AlreadyRetired);
        }

        if (EncodeValidation.WhatRefusesTheProfile(draft) is { Count: > 0 } refusals)
        {
            return Refused(refusals);
        }

        EncodeHold hold = await jobs.HoldOnProfileAsync(id, cancellationToken);

        if (hold.Unfinished is { } underway)
        {
            return Failure<EncodeProfile>(EncodeSaying.Underway("Profile", profile.Id.Wire, underway), EncodingFailure.StillInFlight);
        }

        profile.Revise(
            new EncodeLabel(draft.Label!),
            draft.Codec,
            draft.Resolution,
            draft.Deinterlace,
            new ConstantRateFactor(draft.RateFactor),
            new ConstantQuantiser(draft.Quantiser));

        await profiles.SaveAsync(profile, cancellationToken);

        return ServiceResult<EncodeProfile, EncodingFailure>.Success(profile);
    }

    public async Task<ServiceResult<EncodeDefinitionRemoved, EncodingFailure>> RemoveAsync(
        EncodeProfileId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await profiles.FindAsync(id, cancellationToken) is not { } profile)
        {
            return Missing<EncodeDefinitionRemoved>(id);
        }

        if (profile.IsRetired)
        {
            return Failure<EncodeDefinitionRemoved>(EncodeSaying.Retired(profile), EncodingFailure.AlreadyRetired);
        }

        EncodeHold hold = await jobs.HoldOnProfileAsync(id, cancellationToken);

        if (hold.Unfinished is { } underway)
        {
            return Failure<EncodeDefinitionRemoved>(
                EncodeSaying.Underway("Profile", profile.Id.Wire, underway),
                EncodingFailure.StillInFlight);
        }

        IReadOnlyList<EncodeDestination> placed = await destinations.ListAsync(cancellationToken);

        if (placed.FirstOrDefault(destination => !destination.IsRetired && destination.DefaultProfileId.Equals(id)) is { } naming)
        {
            return Failure<EncodeDefinitionRemoved>(
                $"Profile {profile.Id.Wire} is what destination {naming.Id.Wire} encodes with unless another is asked for; point that destination at another profile first.",
                EncodingFailure.StillTheDefault);
        }

        if (!hold.Any)
        {
            await profiles.RemoveAsync(profile, cancellationToken);

            return Gone(new EncodeDefinitionRemoved(profile.Id.Value, EncodeRemoval.Deleted, null));
        }

        profile.Retire(clock.GetUtcNow().UtcDateTime);
        await profiles.SaveAsync(profile, cancellationToken);

        return Gone(new EncodeDefinitionRemoved(profile.Id.Value, EncodeRemoval.Retired, profile.RetiredAt));
    }

    private static ServiceResult<EncodeDefinitionRemoved, EncodingFailure> Gone(EncodeDefinitionRemoved removed)
        => ServiceResult<EncodeDefinitionRemoved, EncodingFailure>.Success(removed);

    private static ServiceResult<EncodeProfile, EncodingFailure> Refused(IReadOnlyList<EncodeRefusal> refusals)
        => Failure<EncodeProfile>(EncodeRefusals.Describe(refusals), EncodingFailure.Refused);

    private static ServiceResult<T, EncodingFailure> Missing<T>(EncodeProfileId id)
        => Failure<T>($"No profile {id.Wire} is defined.", EncodingFailure.NoSuchProfile);

    private static ServiceResult<T, EncodingFailure> Failure<T>(string saying, EncodingFailure failure)
        => ServiceResult<T, EncodingFailure>.Failure(saying, failure);
}
