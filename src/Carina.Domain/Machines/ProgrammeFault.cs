namespace Carina.Domain.Machines;

public enum ProgrammeFault
{
    ProgrammeMissing = 1,

    TimedOut = 2,
}

public static class ProgrammeFaults
{
    public static ProgrammeFault Named(ProgrammeFault fault)
        => Enum.IsDefined(fault)
            ? fault
            : throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                "A programme fails to be run in one of the ways named here.");
}
