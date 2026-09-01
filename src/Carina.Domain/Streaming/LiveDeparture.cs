namespace Carina.Domain.Streaming;

public enum LiveDeparture
{
    ViewerLeft = 1,

    SourceEnded = 2,

    SourceBroke = 3,

    ViewerStoppedReading = 4,

    SaidSomethingUnknown = 5,

    SaidMoreThanTheWireTakes = 6,

    ServerStopping = 7,
}
