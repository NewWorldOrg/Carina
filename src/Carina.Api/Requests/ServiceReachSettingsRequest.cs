namespace Carina.Api.Requests;

public sealed record ServiceReachSettingsRequest
{
    public int? HoursOfSilence { get; init; }
}
