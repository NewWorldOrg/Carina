using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Auth;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// Holds the keys players open a live channel's segments with, by a digest of each so that the key itself is held
/// nowhere, and lets go of a key and the seat it holds once nothing has been asked with it for a while.
/// </summary>
public sealed class HlsKeys(HlsSettings settings, TimeProvider clock) : IHlsKeys, IAsyncDisposable
{
    private readonly Lock gate = new();

    private readonly Dictionary<string, LiveHlsRendition> held = new(StringComparer.Ordinal);

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

        string key = Unguessable.Issue();
        LiveHlsRendition made;
        LiveHlsRendition? displaced = null;

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

            made = new LiveHlsRendition(DigestOf(key), seat, watcher, profile, settings, clock, Forget);
            held[made.Digest] = made;
        }

        displaced?.Close();
        made.Start();

        return new IssuedHlsKey(key, made);
    }

    public IHlsRendition? Find(string? offered)
    {
        if (!Unguessable.IsOne(offered))
        {
            return null;
        }

        LiveHlsRendition? found;

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

        LiveHlsRendition? found;

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

        List<LiveHlsRendition> revoked;

        lock (gate)
        {
            revoked = [.. held.Values.Where(rendition => rendition.Watcher.Equals(watcher))];

            foreach (LiveHlsRendition rendition in revoked)
            {
                held.Remove(rendition.Digest);
            }
        }

        foreach (LiveHlsRendition rendition in revoked)
        {
            rendition.Close();
        }

        return revoked.Count;
    }

    public async ValueTask DisposeAsync()
    {
        List<LiveHlsRendition> closing;

        lock (gate)
        {
            disposed = true;
            closing = [.. held.Values];
            held.Clear();
        }

        foreach (LiveHlsRendition rendition in closing)
        {
            rendition.Close();
        }

        await Task.WhenAll(closing.Select(rendition => rendition.Life));
    }

    private static string DigestOf(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private void Forget(LiveHlsRendition lapsed)
    {
        lock (gate)
        {
            if (held.TryGetValue(lapsed.Digest, out LiveHlsRendition? kept) && ReferenceEquals(kept, lapsed))
            {
                held.Remove(lapsed.Digest);
            }
        }

        lapsed.Close();
    }
}
