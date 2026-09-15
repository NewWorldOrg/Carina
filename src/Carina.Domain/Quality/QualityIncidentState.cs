namespace Carina.Domain.Quality;

public enum QualityIncidentState
{
    Detected = 1,

    Notified = 2,

    Resolved = 3,
}

public enum QualityIncidentOwner
{
    Quality = 1,

    Tuner = 2,

    Guide = 3,

    Reservation = 4,

    Recording = 5,
}
