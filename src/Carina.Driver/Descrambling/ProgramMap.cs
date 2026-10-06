using System.Buffers.Binary;

namespace Carina.Driver.Descrambling;

/// <summary>
/// Which ECM unlocks which PID, as the PAT and the PMTs of one stream say, for the conditional-access system of one card.
/// </summary>
public sealed class ProgramMap(int caSystemId)
{
    public const byte PatTableId = 0x00;

    public const byte PmtTableId = 0x02;

    private const byte CaDescriptorTag = 0x09;

    private const int NoPid = 0x1FFF;

    private readonly Dictionary<int, Dictionary<int, int>> patSections = [];

    private readonly Dictionary<int, IReadOnlyList<(int Pid, int? Ecm)>> programs = [];

    private readonly Dictionary<int, int?> programEcms = [];

    private int patVersion = -1;

    private Dictionary<int, int> pmtPids = [];

    private Dictionary<int, int> ecmByPid = [];

    private HashSet<int> ecmPids = [];

    public bool PatSeen => patVersion >= 0;

    /// <summary>
    /// The PAT has been read, and so has the PMT of every programme it lists.
    /// </summary>
    public bool Complete => PatSeen && pmtPids.Keys.All(programs.ContainsKey);

    public IReadOnlyCollection<int> EcmPids => ecmPids;

    public bool IsPmt(int pid) => pmtPids.ContainsValue(pid);

    public bool IsEcm(int pid) => ecmPids.Contains(pid);

    /// <summary>
    /// The ECM a PID is unlocked by: the one its PMT names, else, once every PMT has been read, the stream's only ECM when it carries exactly one.
    /// </summary>
    public int? EcmFor(int pid)
    {
        if (ecmByPid.TryGetValue(pid, out int ecm))
        {
            return ecm;
        }

        return Complete && ecmPids.Count is 1 ? ecmPids.First() : null;
    }

    public void ReadPat(ReadOnlySpan<byte> section)
    {
        if (section[0] is not PatTableId || !PsiSection.IsCurrent(section))
        {
            return;
        }

        int version = PsiSection.Version(section);
        if (version != patVersion)
        {
            patSections.Clear();
            patVersion = version;
        }

        Dictionary<int, int> listed = [];
        ReadOnlySpan<byte> loop = PsiSection.Body(section);
        for (int offset = 0; offset + 4 <= loop.Length; offset += 4)
        {
            int programme = BinaryPrimitives.ReadUInt16BigEndian(loop[offset..]);
            int pid = BinaryPrimitives.ReadUInt16BigEndian(loop[(offset + 2)..]) & NoPid;
            if (programme is not 0)
            {
                listed[programme] = pid;
            }
        }

        patSections[section[6]] = listed;
        pmtPids = patSections.Values.SelectMany(part => part).ToDictionary();

        foreach (int gone in programs.Keys.Where(programme => !pmtPids.ContainsKey(programme)).ToList())
        {
            programs.Remove(gone);
            programEcms.Remove(gone);
        }

        Rebuild();
    }

    public void ReadPmt(ReadOnlySpan<byte> section)
    {
        if (section[0] is not PmtTableId || !PsiSection.IsCurrent(section))
        {
            return;
        }

        int programme = PsiSection.Extension(section);
        if (!pmtPids.ContainsKey(programme))
        {
            return;
        }

        ReadOnlySpan<byte> body = PsiSection.Body(section);
        if (body.Length < 4)
        {
            return;
        }

        int infoLength = BinaryPrimitives.ReadUInt16BigEndian(body[2..]) & 0x0FFF;
        if (4 + infoLength > body.Length)
        {
            return;
        }

        int? programEcm = EcmIn(body.Slice(4, infoLength));
        List<(int Pid, int? Ecm)> streams = [];

        ReadOnlySpan<byte> rest = body[(4 + infoLength)..];
        while (rest.Length >= 5)
        {
            int pid = BinaryPrimitives.ReadUInt16BigEndian(rest[1..]) & NoPid;
            int esInfoLength = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(rest[3..]) & 0x0FFF, rest.Length - 5);

            streams.Add((pid, EcmIn(rest.Slice(5, esInfoLength)) ?? programEcm));
            rest = rest[(5 + esInfoLength)..];
        }

        programs[programme] = streams;
        programEcms[programme] = programEcm;
        Rebuild();
    }

    private int? EcmIn(ReadOnlySpan<byte> descriptors)
    {
        ReadOnlySpan<byte> rest = descriptors;

        while (rest.Length >= 2)
        {
            int length = Math.Min(rest[1], rest.Length - 2);
            ReadOnlySpan<byte> content = rest.Slice(2, length);

            if (rest[0] is CaDescriptorTag && length >= 4 && BinaryPrimitives.ReadUInt16BigEndian(content) == caSystemId)
            {
                int pid = BinaryPrimitives.ReadUInt16BigEndian(content[2..]) & NoPid;

                return pid is 0 or NoPid ? null : pid;
            }

            rest = rest[(2 + length)..];
        }

        return null;
    }

    private void Rebuild()
    {
        Dictionary<int, int> byPid = [];
        HashSet<int> ecms = [.. programEcms.Values.OfType<int>()];

        foreach ((int pid, int? ecm) in programs.Values.SelectMany(streams => streams))
        {
            if (ecm is int named)
            {
                byPid[pid] = named;
                ecms.Add(named);
            }
        }

        ecmByPid = byPid;
        ecmPids = ecms;
    }
}
