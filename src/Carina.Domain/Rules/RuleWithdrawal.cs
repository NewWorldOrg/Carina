namespace Carina.Domain.Rules;

/// <summary>
/// When a rule takes back a reservation it made: while it still stands and no longer takes the programme, when it
/// is switched off, or when it is deleted.
/// </summary>
public enum RuleWithdrawal
{
    WhileTheRuleStands = 1,

    WhenTheRuleIsSwitchedOff = 2,

    WhenTheRuleIsDeleted = 3,
}
