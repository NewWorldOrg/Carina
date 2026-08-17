namespace Carina.Domain.Base;

/// <summary>
/// A write that lands whole or not at all. What a caller does inside it may span several
/// repositories and several saves; what the store is left holding is either everything the
/// caller asked for or nothing of it.
/// </summary>
/// <remarks>
/// An implementation binds one store, so every write inside the callback has to reach that same
/// one — anything resolved from another scope writes outside the boundary and lands on its own.
/// The callback is run once. An implementation that ran it again would repeat whatever the
/// callback counted, so anything accumulated over the write belongs inside it rather than around
/// it.
/// </remarks>
public interface IAtomicWrite
{
    Task<T> AllOrNothingAsync<T>(
        Func<CancellationToken, Task<T>> write,
        CancellationToken cancellationToken);
}
