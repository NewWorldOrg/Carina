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
            LiveDeparture.SourceWentQuiet => WebSocketCloseStatus.InternalServerError,
            _ => throw new ArgumentOutOfRangeException(
                nameof(departure),
                departure,
                "A wire ends in one of the ways named here."),
        };

    /// <summary>
    /// What a viewer is told the supply ended for when the supply itself named no reason.
    /// </summary>
    /// <remarks>
    /// The four ways that are not the supply ending name nothing: a viewer that left is not owed an
    /// explanation of its own leaving, a viewer that has stopped reading cannot be sent a frame at
    /// all, and a viewer that said something the wire does not take is told so in the close itself
    /// while the supply behind it goes on running.
    /// </remarks>
    public static LiveSupplyEnd? Ending(LiveDeparture departure)
        => departure switch
        {
            LiveDeparture.SourceWentQuiet => LiveSupplyEnd.WentQuiet,
            LiveDeparture.SourceEnded => LiveSupplyEnd.DriverLost,
            LiveDeparture.SourceBroke => LiveSupplyEnd.DriverLost,
            LiveDeparture.ViewerLeft => null,
            LiveDeparture.ViewerStoppedReading => null,
            LiveDeparture.SaidSomethingUnknown => null,
            LiveDeparture.SaidMoreThanTheWireTakes => null,
            LiveDeparture.ServerStopping => null,
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
            LiveDeparture.SourceWentQuiet => "Nothing has been sent for long enough to take what was supplying it as gone.",
            _ => throw new ArgumentOutOfRangeException(
                nameof(departure),
                departure,
                "A wire ends in one of the ways named here."),
        };
}
