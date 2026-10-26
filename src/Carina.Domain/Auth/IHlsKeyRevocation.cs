namespace Carina.Domain.Auth;

/// <summary>
/// Lets go of the keys a watcher's players open live playlists with, when what let the watcher in is revoked.
/// </summary>
public interface IHlsKeyRevocation
{
    int RevokeEverythingOf(Subject watcher);
}
