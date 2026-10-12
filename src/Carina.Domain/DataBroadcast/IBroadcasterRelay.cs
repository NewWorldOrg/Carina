namespace Carina.Domain.DataBroadcast;

/// <summary>
/// How a relayed exchange with a broadcaster's server ended, numbered the way a data broadcast document reads the
/// first element of what <c>transmitTextDataOverIP</c> returns. A server that answered at all is
/// <see cref="Answered"/>, whatever its status.
/// </summary>
public enum RelayResult
{
    Answered = 1,

    RefusedOrTooLarge = -1,

    Disconnected = -2,

    TimedOut = -3,

    NameNotResolved = -400,

    SecureConnectionFailed = -500,
}

/// <summary>
/// What a text sent to a broadcaster's server came back as: the result, the HTTP status where the server answered,
/// and the body it answered with, read into a string.
/// </summary>
public sealed record RelayedText(RelayResult Result, int? HttpStatus, string? Body)
{
    public static RelayedText Failed(RelayResult result) => new(result, null, null);
}

/// <summary>
/// What a document or resource fetched from a broadcaster's server came back as: the result, the HTTP status where
/// the server answered, and the resource where it answered with a success.
/// </summary>
public sealed record RelayedResource(RelayResult Result, int? HttpStatus, CarouselResource? Resource)
{
    public static RelayedResource Failed(RelayResult result) => new(result, null, null);
}

/// <summary>
/// Talks to a broadcaster's server on behalf of a data broadcast document shown on a live channel.
/// </summary>
public interface IBroadcasterRelay
{
    Task<RelayedText> TransmitAsync(string channel, string uri, string text, string charset, CancellationToken cancellationToken);

    Task<RelayedResource> FetchAsync(string channel, string uri, CancellationToken cancellationToken);
}
