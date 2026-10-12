using Carina.Domain.DataBroadcast;

namespace Carina.Api.Responder.DataBroadcast;

/// <summary>
/// What a text sent to a broadcaster's server came back as, in the shape <c>transmitTextDataOverIP</c> returns it:
/// <c>result</c> is 1 where the server answered, whatever its status, -1 where the address or a size was refused,
/// -2 where the connection broke, -3 where the time ran out, -400 where the name did not resolve and -500 where the
/// secure connection failed; <c>httpStatus</c> and <c>body</c> are there where the server answered.
/// </summary>
public sealed record DataBroadcastTransmissionResponder(int Result, int? HttpStatus, string? Body)
{
    public static DataBroadcastTransmissionResponder Of(RelayedText relayed)
    {
        ArgumentNullException.ThrowIfNull(relayed);

        return new DataBroadcastTransmissionResponder((int)relayed.Result, relayed.HttpStatus, relayed.Body);
    }
}

/// <summary>
/// Why a document or resource could not be fetched from a broadcaster's server: <c>result</c> numbered as a
/// transmission's is, and <c>httpStatus</c> where the server answered with something other than a success.
/// </summary>
public sealed record DataBroadcastFetchFailureResponder(int Result, int? HttpStatus)
{
    public static DataBroadcastFetchFailureResponder Of(RelayedResource relayed)
    {
        ArgumentNullException.ThrowIfNull(relayed);

        return new DataBroadcastFetchFailureResponder((int)relayed.Result, relayed.HttpStatus);
    }
}
