using Carina.Broadcast.Sections;

namespace Carina.Broadcast.Tables;

/// <summary>
/// The programme association table: which pid carries the programme map of each programme of the stream.
/// </summary>
public sealed class ProgramAssociationTable
{
    public const int Pid = 0x0000;

    public const int TableId = 0x00;

    public const int NetworkProgramNumber = 0;

    private const int EntrySize = 4;

    private ProgramAssociationTable(Section section, IReadOnlyList<AssociatedProgram> programs)
    {
        TransportStreamId = section.TableIdExtension;
        VersionNumber = section.VersionNumber;
        Programs = programs;
    }

    public int TransportStreamId { get; }

    public int VersionNumber { get; }

    public IReadOnlyList<AssociatedProgram> Programs { get; }

    public int? MapPidOf(int programNumber)
        => Programs.FirstOrDefault(program => program.ProgramNumber == programNumber)?.MapPid;

    public static TableRead<ProgramAssociationTable> Read(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (section.TableId != TableId)
        {
            return new TableRead<ProgramAssociationTable>.Rejected(TableDefect.WrongTableId);
        }

        ReadOnlySpan<byte> body = section.Body.Span;

        if (body.Length % EntrySize != 0)
        {
            return new TableRead<ProgramAssociationTable>.Rejected(TableDefect.LoopOverrun);
        }

        var programs = new List<AssociatedProgram>();

        for (int at = 0; at < body.Length; at += EntrySize)
        {
            int programNumber = (body[at] << 8) | body[at + 1];

            if (programNumber != NetworkProgramNumber)
            {
                programs.Add(new AssociatedProgram(programNumber, ((body[at + 2] & 0x1F) << 8) | body[at + 3]));
            }
        }

        return new TableRead<ProgramAssociationTable>.Parsed(new ProgramAssociationTable(section, programs));
    }
}

/// <summary>
/// One programme of the association table and the pid its programme map is carried on.
/// </summary>
public sealed record AssociatedProgram(int ProgramNumber, int MapPid);
