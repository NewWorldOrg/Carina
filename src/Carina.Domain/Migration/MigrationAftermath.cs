namespace Carina.Domain.Migration;

public sealed record MigrationAftermath
{
    public MigrationAftermath(
        IReadOnlyList<MigrationChannelProposal> channelProposals,
        IReadOnlyList<MigrationRuleProposal> ruleProposals,
        int rulesRead,
        int rowsPastRestoring,
        int rulesNarrowedByDay)
    {
        ArgumentNullException.ThrowIfNull(channelProposals);
        ArgumentNullException.ThrowIfNull(ruleProposals);

        foreach (MigrationChannelProposal proposal in channelProposals)
        {
            ArgumentNullException.ThrowIfNull(proposal, nameof(channelProposals));
        }

        foreach (MigrationRuleProposal proposal in ruleProposals)
        {
            ArgumentNullException.ThrowIfNull(proposal, nameof(ruleProposals));
        }

        ChannelProposals = [.. channelProposals];
        RuleProposals = [.. ruleProposals];
        RulesRead = Counted(rulesRead, nameof(rulesRead));
        RowsPastRestoring = Counted(rowsPastRestoring, nameof(rowsPastRestoring));
        RulesNarrowedByDay = Counted(rulesNarrowedByDay, nameof(rulesNarrowedByDay));
    }

    public IReadOnlyList<MigrationChannelProposal> ChannelProposals { get; }

    public IReadOnlyList<MigrationRuleProposal> RuleProposals { get; }

    public int RulesRead { get; }

    public int RowsPastRestoring { get; }

    public int RulesNarrowedByDay { get; }

    public static MigrationAftermath Nothing { get; } = new([], [], 0, 0, 0);

    private static int Counted(int value, string parameterName)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "A run counts nothing negative.");
}
