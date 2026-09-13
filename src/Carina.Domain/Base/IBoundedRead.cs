namespace Carina.Domain.Base;

public interface IBoundedRead
{
    Task<T> NoLongerThanAsync<T>(
        TimeSpan patience,
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken);
}
