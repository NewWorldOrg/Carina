using Carina.Api.Services;

namespace Carina.Api.Responder.Encoding;

/// <summary>
/// One row of the answer to running failed jobs again: the id as it was asked, what became of it, why
/// when nothing was queued, and the job queued in its place when one was.
/// </summary>
public sealed record EncodeRerunResponder(Guid Id, EncodeRerunOutcome Outcome, string Message, EncodeJobResponder? Job)
{
    public static EncodeRerunResponder Of(EncodeRerunRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new EncodeRerunResponder(
            row.Id.Value,
            row.Outcome,
            row.Message,
            row.Job is { } job ? EncodeJobResponder.Of(job) : null);
    }
}

public sealed record EncodeRerunListResponder(IReadOnlyList<EncodeRerunResponder> Items)
{
    public static EncodeRerunListResponder Of(IReadOnlyList<EncodeRerunRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return new EncodeRerunListResponder([.. rows.Select(EncodeRerunResponder.Of)]);
    }
}
