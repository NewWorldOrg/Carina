namespace Carina.Domain.Auth;

public interface ILoginThrottle
{
    DateTime? RefusesUntil(string key);

    void Failed(string key);

    void Passed(string key);
}
