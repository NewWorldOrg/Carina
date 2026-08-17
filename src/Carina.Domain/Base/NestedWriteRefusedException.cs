namespace Carina.Domain.Base;

public sealed class NestedWriteRefusedException()
    : Exception(
        "An all-or-nothing write cannot be started while the store is already in the middle of one. "
        + "It would join what is already open, so the outer could keep what this one failed to finish.");
