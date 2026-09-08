namespace Carina.Domain.Migration;

[Flags]
public enum SourceRuleFields
{
    Nothing = 0,

    Title = 1,

    Summary = 2,

    ExtendedBody = 4,
}

public static class SourceRuleFieldsRead
{
    public const SourceRuleFields Every = SourceRuleFields.Title | SourceRuleFields.Summary | SourceRuleFields.ExtendedBody;

    public static SourceRuleFields Of(SourceRuleFields fields, string parameterName)
        => (fields & ~Every) is SourceRuleFields.Nothing
            ? fields
            : throw new ArgumentOutOfRangeException(
                parameterName,
                fields,
                "A rule of the source system looks at the fields it can name.");
}
