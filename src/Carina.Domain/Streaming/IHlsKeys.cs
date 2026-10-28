using Carina.Domain.Auth;
using Carina.Domain.Recordings;

namespace Carina.Domain.Streaming;

/// <summary>
/// The keys a player opens a playlist, its initialisation and its segments with, each holding a viewer's seat on a live
/// session or a recording's transcoder until nothing has been asked with it for a while.
/// </summary>
public interface IHlsKeys : IHlsKeyRevocation
{
    /// <summary>
    /// Issues a key holding <paramref name="seat"/>, or nothing when as many keys are held as may be, in which case the
    /// seat is the caller's to let go of.
    /// </summary>
    IssuedHlsKey? Issue(ILiveViewing seat, Subject watcher, LiveProfile profile);

    /// <summary>
    /// Issues a key holding a recording's <paramref name="transcode"/>, or nothing when as many keys are held as may
    /// be, in which case the transcode is the caller's to let go of.
    /// </summary>
    IssuedHlsKey? Issue(IOnTheFlyViewing transcode, Subject watcher, RecordingId recording);

    /// <summary>
    /// Lets go of the keys <paramref name="watcher"/> holds to <paramref name="recording"/>, and returns once their
    /// transcoders have been let go of, so that a seek does not wait on the seats of the playlists it leaves behind.
    /// </summary>
    Task<int> LetGoOfAsync(Subject watcher, RecordingId recording);

    /// <summary>
    /// What a key opens, counting the asking as a request made with it, or nothing for a key never issued or let go of.
    /// </summary>
    IHlsRendition? Find(string? offered);

    /// <summary>
    /// Lets go of a key and what it holds at once, for a key whose playlist could not be answered.
    /// </summary>
    void LetGo(string? offered);
}

/// <summary>
/// A key just issued. It says nothing of the key when written out, so that it cannot reach a log by accident.
/// </summary>
public sealed class IssuedHlsKey
{
    public IssuedHlsKey(string key, IHlsRendition rendition)
    {
        ArgumentNullException.ThrowIfNull(rendition);

        Key = Unguessable.Validated(key, nameof(key));
        Rendition = rendition;
    }

    public string Key { get; }

    public IHlsRendition Rendition { get; }
}
