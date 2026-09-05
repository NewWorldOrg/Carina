using Carina.Domain.Encodings;

namespace Carina.Api.Responder.Encoding;

public sealed record EncodeDestinationResponder(
    Guid Id,
    string Label,
    string OutputRoot,
    Guid DefaultProfileId,
    DateTime DefinedAt)
{
    public static EncodeDestinationResponder Of(EncodeDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return new EncodeDestinationResponder(
            destination.Id.Value,
            destination.Label.Value,
            destination.OutputRoot.Value,
            destination.DefaultProfileId.Value,
            destination.DefinedAt);
    }
}

public sealed record EncodeDestinationListResponder(IReadOnlyList<EncodeDestinationResponder> Items)
{
    public static EncodeDestinationListResponder Of(IReadOnlyList<EncodeDestination> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);

        return new EncodeDestinationListResponder([.. destinations.Select(EncodeDestinationResponder.Of)]);
    }
}
