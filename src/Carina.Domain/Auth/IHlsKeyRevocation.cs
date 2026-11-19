namespace Carina.Domain.Auth;

public interface IHlsKeyRevocation
{
    int RevokeEverythingOf(Subject watcher);
}
