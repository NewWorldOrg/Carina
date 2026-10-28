using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Auth;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// Holds the keys players open the segments of a live channel or a recording with, by a digest of each so that the key
/// itself is held nowhere, and lets go of a key and what it holds once nothing has been asked with it for a while.
/// </summary>
public sealed class HlsKeys(HlsSettings settings, TimeProvider clock) : IHlsKeys, IAsyncDisposable
{
    private readonly Lock gate = new();

    private readonly Dictionary<string, HlsRendition> held = new(StringComparer.Ordinal);

    private bool disposed;

    public int Count
    {
        get
        {
            lock (gate)
            {
                return held.Count;
            }
        }
    }

    public IssuedHlsKey? Issue(ILiveViewing seat, Subject watcher, LiveProfile profile)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(watcher);
        ArgumentNullException.ThrowIfNull(profile);

        return Issue(watcher, holding => new LiveHlsRendition(holding, seat, profile, settings, clock, Forget));
    }

    public IssuedHlsKey? Issue(IOnTheFlyViewing transcode, Subject watcher)
    {
        ArgumentNullException.ThrowIfNull(transcode);
        ArgumentNullException.ThrowIfNull(watcher);

        return Issue(watcher, holding => new RecordingHlsRendition(holding, transcode, settings, clock, Forget));
    }

    public IHlsRendition? Find(string? offered)
    {
        if (!Unguessable.IsOne(offered))
        {
            return null;
        }

        HlsRendition? found;

        lock (gate)
        {
            if (!held.TryGetValue(DigestOf(offered!), out found))
            {
                return null;
            }

            if (found.HasLapsed(clock.GetUtcNow()))
            {
                held.Remove(found.Digest);
            }
            else
            {
                found.Asked();

                return found;
            }
        }

        found.Close();

        return null;
    }

    public void LetGo(string? offered)
    {
        if (!Unguessable.IsOne(offered))
        {
            return;
        }

        HlsRendition? found;

        lock (gate)
        {
            if (!held.Remove(DigestOf(offered!), out found))
            {
                return;
            }
        }

        found.Close();
    }

    public int RevokeEverythingOf(Subject watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);

        List<HlsRendition> revoked;

        lock (gate)
        {
            revoked = [.. held.Values.Where(rendition => rendition.Watcher.Equals(watcher))];

            foreach (HlsRendition rendition in revoked)
            {
                held.Remove(rendition.Digest);
            }
        }

        foreach (HlsRendition rendition in revoked)
        {
            rendition.Close();
        }

        return revoked.Count;
    }

    public async ValueTask DisposeAsync()
    {
        List<HlsRendition> closing;

        lock (gate)
        {
            disposed = true;
            closing = [.. held.Values];
            held.Clear();
        }

        foreach (HlsRendition rendition in closing)
        {
            rendition.Close();
        }

        await Task.WhenAll(closing.Select(rendition => rendition.Life));
    }

    private IssuedHlsKey? Issue(Subject watcher, Func<HlsHolding, HlsRendition> making)
    {
        string key = Unguessable.Issue();
        HlsRendition made;
        HlsRendition? displaced = null;

        lock (gate)
        {
            if (disposed || held.Count >= settings.MostHeldAtOnce)
            {
                return null;
            }

            if (held.Values.Count(rendition => rendition.Watcher.Equals(watcher)) >= settings.MostHeldPerWatcher)
            {
                displaced = held.Values
                    .Where(rendition => rendition.Watcher.Equals(watcher))
                    .MinBy(rendition => rendition.AskedAt);
                held.Remove(displaced!.Digest);
            }

            made = making(new HlsHolding(DigestOf(key), watcher));
            held[made.Digest] = made;
        }

        displaced?.Close();
        made.Start();

        return new IssuedHlsKey(key, made);
    }

    private static string DigestOf(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private void Forget(HlsRendition lapsed)
    {
        lock (gate)
        {
            if (held.TryGetValue(lapsed.Digest, out HlsRendition? kept) && ReferenceEquals(kept, lapsed))
            {
                held.Remove(lapsed.Digest);
            }
        }

        lapsed.Close();
    }
}
