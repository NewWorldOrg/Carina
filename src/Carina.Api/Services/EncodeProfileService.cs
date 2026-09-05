using Carina.Api.Common;
using Carina.Domain.Encodings;

namespace Carina.Api.Services;

public sealed class EncodeProfileService(IEncodeProfileRepository profiles, TimeProvider clock)
{
    public async Task<ServiceResult<IReadOnlyList<EncodeProfile>>> ListAsync(CancellationToken cancellationToken)
        => ServiceResult<IReadOnlyList<EncodeProfile>>.Success(await profiles.ListAsync(cancellationToken));

    public async Task<ServiceResult<EncodeProfile, EncodingFailure>> DefineAsync(
        EncodeProfileDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        IReadOnlyList<EncodeRefusal> refusals = EncodeValidation.WhatRefusesTheProfile(draft);

        if (refusals.Count > 0)
        {
            return ServiceResult<EncodeProfile, EncodingFailure>.Failure(EncodeRefusals.Describe(refusals), EncodingFailure.Refused);
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
}
