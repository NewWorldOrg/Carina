namespace Carina.Api.Requests;

public sealed record ForgetArchivedServiceRequest
{
    public const string TheWordThatMeansIt = "forget-this-service";

    public int? NetworkId { get; init; }

    public int? ServiceId { get; init; }

    public string? Confirm { get; init; }

    public bool MeansIt => string.Equals(Confirm, TheWordThatMeansIt, StringComparison.Ordinal);
}
