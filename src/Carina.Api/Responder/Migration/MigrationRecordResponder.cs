using Carina.Api.Services;

using Carina.Domain.Base;
using Carina.Domain.Migration;

namespace Carina.Api.Responder.Migration;

public sealed record MigrationRunResponder(
    Guid Id,
    string Source,
    MigrationPass Pass,
    DateTime StartedAt,
    DateTime FinishedAt,
    int Rehearsals,
    DateTime? LastRehearsalFinishedAt)
{
    public static MigrationRunResponder Of(MigrationRecordSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new MigrationRunResponder(
            summary.Run.Id.Value,
            summary.Run.Source.Value,
            summary.Run.Pass,
            summary.Run.StartedAt,
            summary.Run.FinishedAt,
            summary.Rehearsals,
            summary.LastRehearsalFinishedAt);
    }
}

public sealed record MigrationPopulationResponder(
    MigrationPopulation Population,
    int Offered,
    int Carried,
    int NotCarried,
    int Unclassified)
{
    public static MigrationPopulationResponder Of(MigrationTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);

        return new MigrationPopulationResponder(
            tally.Population,
            tally.Offered,
            tally.Carried,
            tally.NotCarried,
            tally.Unclassified);
    }
}

public sealed record MigrationRefusalResponder(MigrationRefusal Refusal, int Count)
{
    public static MigrationRefusalResponder Of(MigrationRefusalCount counted)
    {
        ArgumentNullException.ThrowIfNull(counted);

        return new MigrationRefusalResponder(counted.Refusal, counted.Count);
    }
}

public sealed record MigrationLossResponder(MigrationLossSubject Subject, int Affected)
{
    public static MigrationLossResponder Of(MigrationLoss loss)
    {
        ArgumentNullException.ThrowIfNull(loss);

        return new MigrationLossResponder(loss.Subject, loss.Affected);
    }
}

public sealed record MigrationDetailResponder(
    Guid Id,
    MigrationPopulation Population,
    MigrationRefusal Refusal,
    string Subject,
    string Note,
    long? Claimed,
    long? Observed)
{
    public static MigrationDetailResponder Of(MigrationDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new MigrationDetailResponder(
            detail.Id.Value,
            detail.Population,
            detail.Refusal,
            detail.Subject,
            detail.Note,
            detail.Claimed,
            detail.Observed);
    }
}

public sealed record MigrationRecordResponder(
    MigrationRunResponder? Run,
    IReadOnlyList<MigrationPopulationResponder> Populations,
    int Unclassified,
    IReadOnlyList<MigrationRefusalResponder> Refusals,
    IReadOnlyList<MigrationLossResponder> Losses,
    IReadOnlyList<MigrationDetailResponder> Items,
    int Total,
    int CurrentPage,
    int LastPage,
    int PerPage)
{
    public static MigrationRecordResponder Of(MigrationRecordRead read)
    {
        ArgumentNullException.ThrowIfNull(read);

        PaginatedList<MigrationDetail> details = read.Details;
        MigrationRecordSummary? summary = read.Summary;

        return new MigrationRecordResponder(
            summary is null ? null : MigrationRunResponder.Of(summary),
            summary is null ? [] : [.. summary.Tallies.Select(MigrationPopulationResponder.Of)],
            summary?.Unclassified ?? 0,
            summary is null ? [] : [.. summary.Refusals.Select(MigrationRefusalResponder.Of)],
            summary is null ? [] : [.. summary.Losses.Select(MigrationLossResponder.Of)],
            [.. details.Items.Select(MigrationDetailResponder.Of)],
            details.Total,
            details.CurrentPage,
            details.LastPage,
            details.PerPage);
    }
}
