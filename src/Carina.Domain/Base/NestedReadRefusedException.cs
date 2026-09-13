namespace Carina.Domain.Base;

public sealed class NestedReadRefusedException()
    : Exception(
        "A bounded read cannot be started while the store is already in the middle of a transaction. "
        + "The time it gives one statement would be left on the transaction it joined, "
        + "and ending its own would end that one.");
