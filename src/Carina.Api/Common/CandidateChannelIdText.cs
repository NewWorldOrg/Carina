using Carina.Domain.Channels;

namespace Carina.Api.Common;

public static class CandidateChannelIdText
{
    public const string Description = "A candidate channel is named by a UUID, and never by one that is all zeroes.";

    public static CandidateChannelId? Read(Guid id) => id == Guid.Empty ? null : new CandidateChannelId(id);
}
