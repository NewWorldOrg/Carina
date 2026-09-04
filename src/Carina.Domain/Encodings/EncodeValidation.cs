using Carina.Contracts;

namespace Carina.Domain.Encodings;

public enum EncodeRefusal
{
    CodecUnknown = 1,

    ResolutionUnknown = 2,

    DeinterlaceUnknown = 3,

    RateFactorOutOfRange = 4,

    QuantiserOutOfRange = 5,

    LabelMissing = 6,

    LabelTooLong = 7,

    OutputRootNotDeclared = 8,

    DefaultProfileUnknown = 9,
}

public sealed record EncodeProfileDraft(
    string? Label,
    EncodeCodec Codec,
    EncodeResolution Resolution,
    Deinterlace Deinterlace,
    int RateFactor,
    int Quantiser);

public sealed record EncodeDestinationDraft(
    string? Label,
    string? OutputRoot,
    EncodeProfileId? DefaultProfileId);

public static class EncodeValidation
{
    public static IReadOnlyList<EncodeRefusal> WhatRefusesTheProfile(EncodeProfileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        List<EncodeRefusal> refusals = [.. WhatRefusesTheLabel(draft.Label)];

        if (!Enum.IsDefined(draft.Codec))
        {
            refusals.Add(EncodeRefusal.CodecUnknown);
        }

        if (!Enum.IsDefined(draft.Resolution))
        {
            refusals.Add(EncodeRefusal.ResolutionUnknown);
        }

        if (!Enum.IsDefined(draft.Deinterlace))
        {
            refusals.Add(EncodeRefusal.DeinterlaceUnknown);
        }

        if (draft.RateFactor is < ConstantRateFactor.Finest or > ConstantRateFactor.Coarsest)
        {
            refusals.Add(EncodeRefusal.RateFactorOutOfRange);
        }

        if (draft.Quantiser is < ConstantQuantiser.Finest or > ConstantQuantiser.Coarsest)
        {
            refusals.Add(EncodeRefusal.QuantiserOutOfRange);
        }

        return refusals;
    }

    public static IReadOnlyList<EncodeRefusal> WhatRefusesTheDestination(
        EncodeDestinationDraft draft,
        IReadOnlyList<StorageRootDto> declared,
        IReadOnlyList<EncodeProfileId> profiles)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(profiles);

        List<EncodeRefusal> refusals = [.. WhatRefusesTheLabel(draft.Label)];

        if (!StorageRoots.Declares(declared, draft.OutputRoot))
        {
            refusals.Add(EncodeRefusal.OutputRootNotDeclared);
        }

        if (draft.DefaultProfileId is null || !profiles.Contains(draft.DefaultProfileId))
        {
            refusals.Add(EncodeRefusal.DefaultProfileUnknown);
        }

        return refusals;
    }

    private static IReadOnlyList<EncodeRefusal> WhatRefusesTheLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return [EncodeRefusal.LabelMissing];
        }

        return label.Trim().Length > EncodeLabel.Longest ? [EncodeRefusal.LabelTooLong] : [];
    }
}
