using Carina.Domain.Encodings;

namespace Carina.Api.Common;

public static class EncodingIdText
{
    public const string Description = "A profile, a destination or a job is named by a UUID, and never by one that is all zeroes.";

    public static EncodeProfileId? Profile(Guid? id) => id is { } some && some != Guid.Empty ? new EncodeProfileId(some) : null;

    public static EncodeDestinationId? Destination(Guid? id) => id is { } some && some != Guid.Empty ? new EncodeDestinationId(some) : null;

    public static EncodeJobId? Job(Guid id) => id == Guid.Empty ? null : new EncodeJobId(id);
}
