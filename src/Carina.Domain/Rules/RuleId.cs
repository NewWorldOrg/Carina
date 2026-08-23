using Carina.Domain.Base;

namespace Carina.Domain.Rules;

public sealed class RuleId : CommonValueObject<Guid>
{
    public RuleId(Guid value)
        : base(Validated(value))
    {
    }

    public static RuleId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A rule id cannot be empty.", nameof(value));
        }

        return value;
    }
}
