namespace Carina.Api.Requests;

public sealed record PatchSegmentSettingsRequest
{
    public bool? Learning { get; init; }
}
