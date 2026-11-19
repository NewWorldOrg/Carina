namespace Carina.Domain.DataBroadcast;

public enum RelayResult
{
    Answered = 1,

    RefusedOrTooLarge = -1,

    Disconnected = -2,

    TimedOut = -3,

    NameNotResolved = -400,

    SecureConnectionFailed = -500,
}

public sealed record RelayedText(RelayResult Result, int? HttpStatus, string? Body)
{
    public static RelayedText Failed(RelayResult result) => new(result, null, null);
}

public sealed record RelayedResource(RelayResult Result, int? HttpStatus, CarouselResource? Resource)
{
    public static RelayedResource Failed(RelayResult result) => new(result, null, null);
}

public interface IBroadcasterRelay
{
    Task<RelayedText> TransmitAsync(string channel, string uri, string text, string charset, CancellationToken cancellationToken);

    Task<RelayedResource> FetchAsync(string channel, string uri, CancellationToken cancellationToken);
}
