using Carina.Domain.Rules;

namespace Carina.Api.Common;

public static class RuleIdText
{
    public const string Description = "A rule is named by a UUID, and never by one that is all zeroes.";

    public static RuleId? Read(Guid id) => id == Guid.Empty ? null : new RuleId(id);
}
