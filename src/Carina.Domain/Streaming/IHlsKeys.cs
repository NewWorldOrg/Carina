using Carina.Domain.Auth;
using Carina.Domain.Recordings;

namespace Carina.Domain.Streaming;

public interface IHlsKeys : IHlsKeyRevocation
{
    IssuedHlsKey? Issue(ILiveViewing seat, Subject watcher, LiveProfile profile);

    IssuedHlsKey? Issue(IOnTheFlyViewing transcode, Subject watcher, RecordingId recording);

    Task<int> LetGoOfAsync(Subject watcher, RecordingId recording);

    IHlsRendition? Find(string? offered);

    void LetGo(string? offered);
}

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
