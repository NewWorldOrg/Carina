namespace Carina.Broadcast.Tables;

public enum TableDefect
{
    WrongTableId = 1,

    SectionTooShort = 2,

    LoopOverrun = 3,

    MalformedDescriptor = 4,

    DataModuleOverrun = 5,
}
