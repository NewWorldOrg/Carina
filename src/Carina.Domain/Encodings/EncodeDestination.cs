using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public sealed class EncodeDestination
{
    private EncodeDestination()
    {
    }

    public EncodeDestinationId Id { get; private set; } = null!;

    public EncodeLabel Label { get; private set; } = null!;

    public OutputRoot OutputRoot { get; private set; } = null!;

    public EncodeProfileId DefaultProfileId { get; private set; } = null!;

    public DateTime DefinedAt { get; private set; }

    public DateTime? RetiredAt { get; private set; }

    public bool IsRetired => RetiredAt is not null;

    public static EncodeDestination Define(
        EncodeDestinationId id,
        EncodeLabel label,
        OutputRoot outputRoot,
        EncodeProfileId defaultProfileId,
        DateTime at)
        => Rehydrate(id, label, outputRoot, defaultProfileId, at, null);

    public static EncodeDestination Rehydrate(
        EncodeDestinationId id,
        EncodeLabel label,
        OutputRoot outputRoot,
        EncodeProfileId defaultProfileId,
        DateTime definedAt,
        DateTime? retiredAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(outputRoot);
        ArgumentNullException.ThrowIfNull(defaultProfileId);

        return new EncodeDestination
        {
            Id = id,
            Label = label,
            OutputRoot = outputRoot,
            DefaultProfileId = defaultProfileId,
            DefinedAt = UtcTimes.Required(definedAt, nameof(definedAt)),
            RetiredAt = UtcTimes.Optional(retiredAt, nameof(retiredAt)),
        };
    }

    public void Revise(EncodeLabel label, OutputRoot outputRoot, EncodeProfileId defaultProfileId)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(outputRoot);
        ArgumentNullException.ThrowIfNull(defaultProfileId);
        RefuseWhileRetired();

        Label = label;
        OutputRoot = outputRoot;
        DefaultProfileId = defaultProfileId;
    }

    public void Retire(DateTime at)
    {
        RefuseWhileRetired();

        RetiredAt = UtcTimes.Required(at, nameof(at));
    }

    private void RefuseWhileRetired()
    {
        if (IsRetired)
        {
            throw new InvalidOperationException($"Destination {Id.Wire} was retired at {RetiredAt:O} and is not moved again.");
        }
    }
}
