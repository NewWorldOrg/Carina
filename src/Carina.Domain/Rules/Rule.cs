using Carina.Domain.Base;
using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

public sealed class Rule
{
    public const int NameMaxLength = 128;

    private Rule()
    {
    }

    public RuleId Id { get; private set; } = null!;

    public string Name { get; private set; } = string.Empty;

    public RuleQuery Query { get; private set; } = null!;

    public Priority Priority { get; private set; } = null!;

    public bool Enabled { get; private set; }

    public Margin MarginBefore { get; private set; } = null!;

    public Margin MarginAfter { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public static Rule Draft(
        RuleId id,
        string name,
        RuleQuery query,
        Priority priority,
        bool enabled,
        Margin marginBefore,
        Margin marginAfter,
        DateTime at)
        => Rehydrate(id, name, query, priority, enabled, marginBefore, marginAfter, at);

    public static Rule Rehydrate(
        RuleId id,
        string name,
        RuleQuery query,
        Priority priority,
        bool enabled,
        Margin marginBefore,
        Margin marginAfter,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(marginBefore);
        ArgumentNullException.ThrowIfNull(marginAfter);

        return new Rule
        {
            Id = id,
            Name = ValidatedName(name),
            Query = query,
            Priority = priority,
            Enabled = enabled,
            MarginBefore = marginBefore,
            MarginAfter = marginAfter,
            CreatedAt = UtcTimes.Required(createdAt, nameof(createdAt)),
        };
    }

    public void Enable() => Enabled = true;

    public void Disable() => Enabled = false;

    public void Rewrite(string name, RuleQuery query, Priority priority, Margin marginBefore, Margin marginAfter)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(marginBefore);
        ArgumentNullException.ThrowIfNull(marginAfter);

        Name = ValidatedName(name);
        Query = query;
        Priority = priority;
        MarginBefore = marginBefore;
        MarginAfter = marginAfter;
    }

    private static string ValidatedName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"A rule name is at most {NameMaxLength} characters, but this one has {name.Length}.",
                nameof(name));
        }

        return name;
    }
}
