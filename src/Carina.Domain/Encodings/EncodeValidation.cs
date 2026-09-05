using Carina.Contracts;
using Carina.Domain.Recordings;

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

    OutputRootNotHeld = 10,
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

    /// <summary>
    /// A destination names a root out of the declared set (BR-EV-001), and out of that set one this
    /// process holds for writing: the roots the recordings are read from are declared too, but an
    /// artefact is never placed in one of them, so naming one is refused when it is saved rather
    /// than failing every job afterwards.
    /// </summary>
    public static IReadOnlyList<EncodeRefusal> WhatRefusesTheDestination(
        EncodeDestinationDraft draft,
        IReadOnlyList<StorageRootDto> declared,
        IReadOnlyList<OutputRoot> held,
        IReadOnlyList<EncodeProfileId> profiles)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(profiles);

        List<EncodeRefusal> refusals = [.. WhatRefusesTheLabel(draft.Label)];

        if (!StorageRoots.Declares(declared, draft.OutputRoot))
        {
            refusals.Add(EncodeRefusal.OutputRootNotDeclared);
        }
        else if (!held.Any(root => string.Equals(root.Value, draft.OutputRoot, StringComparison.Ordinal)))
        {
            refusals.Add(EncodeRefusal.OutputRootNotHeld);
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
