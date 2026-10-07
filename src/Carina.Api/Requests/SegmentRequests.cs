using System.Text.Json.Serialization;

namespace Carina.Api.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatchSegmentSettingsRequest
{
    public bool? Learning { get; init; }
}
