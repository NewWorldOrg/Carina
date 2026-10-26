using Carina.Domain.Auth;

namespace Carina.Domain.Streaming;

/// <summary>
/// The keys a player opens a live channel's playlist, initialisation and segments with, each holding a viewer's seat
/// on a live session until nothing has been asked with it for a while.
/// </summary>
public interface ILiveHlsKeys
{
    /// <summary>
    /// Issues a key holding <paramref name="seat"/>, or nothing when as many keys are held as may be, in which case the
    /// seat is the caller's to let go of.
    /// </summary>
    IssuedLiveHlsKey? Issue(ILiveViewing seat, Subject watcher, LiveProfile profile);

    /// <summary>
    /// What a key opens, counting the asking as a request made with it, or nothing for a key never issued or let go of.
    /// </summary>
    ILiveHlsRendition? Find(string? offered);

    int RevokeEverythingOf(Subject watcher);
}

/// <summary>
/// A key just issued. It says nothing of the key when written out, so that it cannot reach a log by accident.
/// </summary>
public sealed class IssuedLiveHlsKey
{
    public IssuedLiveHlsKey(string key, ILiveHlsRendition rendition)
    {
        ArgumentNullException.ThrowIfNull(rendition);

        Key = Unguessable.Validated(key, nameof(key));
        Rendition = rendition;
    }

    public string Key { get; }

    public ILiveHlsRendition Rendition { get; }
}
