namespace Carina.Domain.Integrity;

public sealed class IntegrityReport
{
    private IntegrityReport(IntegrityCheck check, IReadOnlyList<IntegrityFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(findings);

        HashSet<Guid> named = [];

        foreach (IntegrityFinding finding in findings)
        {
            ArgumentNullException.ThrowIfNull(finding);

            if (!finding.CheckId.Equals(check.Id))
            {
                throw new ArgumentException(
                    "A report carries the findings of the check it is about and no others.",
                    nameof(findings));
            }

            if (!named.Add(finding.Id.Value))
            {
                throw new ArgumentException(
                    "A finding is named for the fact it is about, so one sweep cannot bring the same name back twice.",
                    nameof(findings));
            }
        }

        Check = check;
        Findings = [.. findings];
    }

    public IntegrityCheck Check { get; }

    public IReadOnlyList<IntegrityFinding> Findings { get; }

    public static IntegrityReport Of(IntegrityCheck check, IReadOnlyList<IntegrityFinding> findings)
        => new(check, findings);
}
