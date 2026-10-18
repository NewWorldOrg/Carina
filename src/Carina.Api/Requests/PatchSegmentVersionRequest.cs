using System.Text.Json.Serialization;

namespace Carina.Api.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatchSegmentVersionRequest
{
    public int? Number { get; init; }

    public bool? ByHand { get; init; }
}
