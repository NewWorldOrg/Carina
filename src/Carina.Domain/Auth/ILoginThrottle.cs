namespace Carina.Domain.Auth;

public interface ILoginThrottle
{
    DateTime? TakeTry(string key);

    void Passed(string key);
}
