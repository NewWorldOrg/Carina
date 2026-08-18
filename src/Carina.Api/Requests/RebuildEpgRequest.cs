namespace Carina.Api.Requests;

public sealed record RebuildEpgRequest
{
    public const string TheWordThatMeansIt = "discard-everything";

    public string? Confirm { get; init; }

    public bool MeansIt => string.Equals(Confirm, TheWordThatMeansIt, StringComparison.Ordinal);
}
