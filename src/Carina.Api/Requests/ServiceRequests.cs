namespace Carina.Api.Requests;

public sealed record SelectedChannelRequest
{
    public Guid? CandidateChannelId { get; init; }
}

public sealed record AddCandidateChannelRequest
{
    public TuningParametersRequest? Tuning { get; init; }
}
