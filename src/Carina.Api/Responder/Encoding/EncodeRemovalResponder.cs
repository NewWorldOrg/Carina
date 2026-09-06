using Carina.Api.Services;

namespace Carina.Api.Responder.Encoding;

public sealed record EncodeRemovalResponder(Guid Id, EncodeRemoval Removal, DateTime? RetiredAt)
{
    public static EncodeRemovalResponder Of(EncodeDefinitionRemoved removed)
    {
        ArgumentNullException.ThrowIfNull(removed);

        return new EncodeRemovalResponder(removed.Id, removed.Removal, removed.RetiredAt);
    }
}
