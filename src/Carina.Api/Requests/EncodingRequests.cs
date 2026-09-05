using Carina.Domain.Encodings;

namespace Carina.Api.Requests;

public sealed record CreateEncodeProfileRequest
{
    public string? Label { get; init; }

    public EncodeCodec? Codec { get; init; }

    public EncodeResolution? Resolution { get; init; }

    public Deinterlace? Deinterlace { get; init; }

    public int? RateFactor { get; init; }

    public int? Quantiser { get; init; }
}

public sealed record CreateEncodeDestinationRequest
{
    public string? Label { get; init; }

    public string? OutputRoot { get; init; }

    public Guid? DefaultProfileId { get; init; }
}

public sealed record QueueEncodeJobRequest
{
    public string? RecordingId { get; init; }

    public Guid? ProfileId { get; init; }

    public Guid? DestinationId { get; init; }
}
