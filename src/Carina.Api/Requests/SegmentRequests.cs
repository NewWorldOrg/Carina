using System.Text.Json.Serialization;

namespace Carina.Api.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatchSegmentSettingsRequest
{
    public bool? Learning { get; init; }

    public double? SkipThreshold { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PutRecordingSegmentsRequest
{
    public IReadOnlyList<AnsweredSegmentRequest?>? Segments { get; init; }

    public bool? Confirmed { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnsweredSegmentRequest
{
    public required double StartsAtSec { get; init; }

    public required double EndsAtSec { get; init; }

    public string? Kind { get; init; }
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
