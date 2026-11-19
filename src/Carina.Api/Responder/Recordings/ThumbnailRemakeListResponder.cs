using Carina.Api.Services;

namespace Carina.Api.Responder.Recordings;

public sealed record ThumbnailRemakeRowResponder(string Id, ThumbnailRemakeOutcome Outcome, string Message)
{
    public static ThumbnailRemakeRowResponder Of(ThumbnailRemakeRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new ThumbnailRemakeRowResponder(row.Id.Wire, row.Outcome, row.Message);
    }
}

public sealed record ThumbnailRemakeListResponder(IReadOnlyList<ThumbnailRemakeRowResponder> Items)
{
    public static ThumbnailRemakeListResponder Of(IReadOnlyList<ThumbnailRemakeRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return new ThumbnailRemakeListResponder([.. rows.Select(ThumbnailRemakeRowResponder.Of)]);
    }
}
