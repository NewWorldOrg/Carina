namespace Carina.Domain.Base;

public sealed class NestedWriteRefusedException()
    : Exception(
        "An all-or-nothing write cannot be started inside another one. "
        + "The inner write would join the outer, so the outer could keep what the inner failed to finish.");
