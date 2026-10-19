using System.Text.Json.Serialization;

namespace Carina.Api.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatchSegmentSettingsRequest
{
    public bool? Learning { get; init; }

    public double? SkipThreshold { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddSkipEventRequest
{
    public string? Event { get; init; }

    public string? Kind { get; init; }

    public double? FromSec { get; init; }

    public double? ToSec { get; init; }

    public int? ModelVersion { get; init; }
}
