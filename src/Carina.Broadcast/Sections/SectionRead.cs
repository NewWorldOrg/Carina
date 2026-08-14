namespace Carina.Broadcast.Sections;

public abstract record SectionRead
{
    private SectionRead(int pid)
    {
        Pid = pid;
    }

    public int Pid { get; }

    public sealed record Assembled : SectionRead
    {
        internal Assembled(int pid, Section section)
            : base(pid)
        {
            Section = section;
        }

        public Section Section { get; }
    }

    public sealed record Rejected : SectionRead
    {
        internal Rejected(int pid, SectionDefect defect)
            : base(pid)
        {
            Defect = defect;
        }

        public SectionDefect Defect { get; }
    }
}
