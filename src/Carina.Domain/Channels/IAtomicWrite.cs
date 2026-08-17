namespace Carina.Domain.Channels;

/// <summary>
/// A write that lands whole or not at all. What a caller does inside it may span several
/// repositories and several saves; what the store is left holding is either everything the
/// caller asked for or nothing of it.
/// </summary>
public interface IAtomicWrite
{
    Task<T> AllOrNothingAsync<T>(
        Func<CancellationToken, Task<T>> write,
        CancellationToken cancellationToken);
}
