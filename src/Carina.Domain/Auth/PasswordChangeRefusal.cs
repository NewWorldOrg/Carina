namespace Carina.Domain.Auth;

/// <summary>
/// Why the password of the local account is not changed.
/// </summary>
public enum PasswordChangeRefusal
{
    WrongPassword = 1,

    OutOfLength = 2,
}
