using System.Text.Json.Serialization;

namespace Carina.Api.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransmitDataBroadcastRequest
{
    public string? Uri { get; init; }

    public string? Text { get; init; }

    public string? Charset { get; init; }
}
