using System.Text.Json.Serialization;

namespace Carina.Api.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransmitDataBroadcastRequest
{
    public string? Uri { get; init; }

    public string? Text { get; init; }

    public string? Charset { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartHybridcastRequest
{
    public string? Ait { get; init; }

    public long? OrgId { get; init; }

    public int? AppId { get; init; }

    public string? ReceiverScript { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PutDataBroadcastSettingsRequest
{
    [JsonRequired]
    public string? PostalCode { get; init; }

    [JsonRequired]
    public int? Prefecture { get; init; }
}
