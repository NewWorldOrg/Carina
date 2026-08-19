namespace Carina.Domain.Auth;

public interface IPasswordHasher
{
    PasswordHash Hash(string password, PasswordHashPolicy policy);

    bool Matches(string password, PasswordHash hash);
}
