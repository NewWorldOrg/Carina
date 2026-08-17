namespace Carina.Domain.Base;

public interface IAtomicWrite
{
    Task<T> AllOrNothingAsync<T>(
        Func<CancellationToken, Task<T>> write,
        CancellationToken cancellationToken);
}
