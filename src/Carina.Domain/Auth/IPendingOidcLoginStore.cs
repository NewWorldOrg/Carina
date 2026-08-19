namespace Carina.Domain.Auth;

public interface IPendingOidcLoginStore
{
    void Hold(PendingOidcLogin pending);

    PendingOidcLogin? Take(string state);
}
