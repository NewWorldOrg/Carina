using System.Net.WebSockets;

using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public static class LiveDepartures
{
    public static WebSocketCloseStatus Status(LiveDeparture departure)
        => departure switch
        {
            LiveDeparture.ViewerLeft => WebSocketCloseStatus.NormalClosure,
            LiveDeparture.SourceEnded => WebSocketCloseStatus.NormalClosure,
            LiveDeparture.SourceBroke => WebSocketCloseStatus.InternalServerError,
            LiveDeparture.ViewerStoppedReading => WebSocketCloseStatus.PolicyViolation,
            LiveDeparture.SaidSomethingUnknown => WebSocketCloseStatus.InvalidPayloadData,
            LiveDeparture.SaidMoreThanTheWireTakes => WebSocketCloseStatus.MessageTooBig,
            LiveDeparture.ServerStopping => WebSocketCloseStatus.EndpointUnavailable,
            _ => throw new ArgumentOutOfRangeException(
                nameof(departure),
                departure,
                "A wire ends in one of the ways named here."),
        };

    public static string Because(LiveDeparture departure)
        => departure switch
        {
            LiveDeparture.ViewerLeft => "You said you were leaving.",
            LiveDeparture.SourceEnded => "There is nothing further to send.",
            LiveDeparture.SourceBroke => "What was being sent stopped part way through.",
            LiveDeparture.ViewerStoppedReading => "Frames were not being taken quickly enough to keep sending.",
            LiveDeparture.SaidSomethingUnknown => "That is not a control message this wire understands.",
            LiveDeparture.SaidMoreThanTheWireTakes => "A control message is smaller than that.",
            LiveDeparture.ServerStopping => "The app is shutting down.",
            _ => throw new ArgumentOutOfRangeException(
                nameof(departure),
                departure,
                "A wire ends in one of the ways named here."),
        };
}
