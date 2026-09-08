using Carina.Domain.Rules;

namespace Carina.Domain.Migration;

public sealed class MigrationRuleProposal
{
    private MigrationRuleProposal()
    {
    }

    public MigrationRunId RunId { get; private set; } = null!;

    public long SourceRow { get; private set; }

    public RuleId? RuleId { get; private set; }

    public bool EnabledAtTheSource { get; private set; }

    public static MigrationRuleProposal Rehydrate(
        MigrationRunId runId,
        long sourceRow,
        RuleId? ruleId,
        bool enabledAtTheSource)
    {
        ArgumentNullException.ThrowIfNull(runId);

        return new MigrationRuleProposal
        {
            RunId = runId,
            SourceRow = Migration.SourceRow.Of(sourceRow, nameof(sourceRow)),
            RuleId = ruleId,
            EnabledAtTheSource = enabledAtTheSource,
        };
    }
}
