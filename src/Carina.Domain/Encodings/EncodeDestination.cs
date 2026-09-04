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

    public static EncodeDestination Define(
        EncodeDestinationId id,
        EncodeLabel label,
        OutputRoot outputRoot,
        EncodeProfileId defaultProfileId,
        DateTime at)
        => Rehydrate(id, label, outputRoot, defaultProfileId, at);

    public static EncodeDestination Rehydrate(
        EncodeDestinationId id,
        EncodeLabel label,
        OutputRoot outputRoot,
        EncodeProfileId defaultProfileId,
        DateTime definedAt)
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
        };
    }
}
