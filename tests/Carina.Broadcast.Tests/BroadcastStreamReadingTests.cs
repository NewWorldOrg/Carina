using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Sections;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests;

public sealed class BroadcastStreamReadingTests
{
    private const int SomeNetworkId = 50001;
    private const int SomeTransportStreamId = 50002;
    private const int FirstServiceId = 50101;

    [Fact]
    public void OnePacketStreamYieldsBothTablesAScanNeeds()
    {
        var network = new TransportStreamWriter(NetworkInformationTable.Pid)
            .Sections(NitSection(version: 1))
            .Packets;
        var services = new TransportStreamWriter(ServiceDescriptionTable.Pid)
            .Sections(SdtSection(version: 1, serviceCount: 3))
            .Packets;

        var reader = new SectionReader(NetworkInformationTable.Pid, ServiceDescriptionTable.Pid);
        var read = Interleave(network, services).SelectMany(packet => reader.Push(packet)).ToArray();

        Assert.All(read, outcome => Assert.IsType<SectionRead.Assembled>(outcome));

        var sections = read.Cast<SectionRead.Assembled>().ToArray();
        var nit = Assert.IsType<TableRead<NetworkInformationTable>.Parsed>(
            NetworkInformationTable.Read(sections.Single(section => section.Pid == NetworkInformationTable.Pid)
                .Section)).Table;
        var sdt = Assert.IsType<TableRead<ServiceDescriptionTable>.Parsed>(
            ServiceDescriptionTable.Read(sections.Single(section => section.Pid == ServiceDescriptionTable.Pid)
                .Section)).Table;

        Assert.Equal(SomeNetworkId, nit.NetworkId);
        Assert.Equal(SomeTransportStreamId, Assert.Single(nit.TransportStreams).TransportStreamId);
        Assert.Equal(SomeTransportStreamId, sdt.TransportStreamId);
        Assert.Equal(SomeNetworkId, sdt.OriginalNetworkId);
        Assert.Equal(3, sdt.Services.Count);
        Assert.Equal("試験テレビ", sdt.Services[0].Name);
    }

    [Fact]
    public void AServiceTableTooBigForOnePacketSurvivesTheRoundTrip()
    {
        var section = SdtSection(version: 1, serviceCount: 40);
        var packets = new TransportStreamWriter(ServiceDescriptionTable.Pid).Sections(section).Packets;

        Assert.True(packets.Count > 1);

        var reader = new SectionReader(ServiceDescriptionTable.Pid);
        var read = packets.SelectMany(packet => reader.Push(packet)).ToArray();
        var table = Assert.IsType<TableRead<ServiceDescriptionTable>.Parsed>(
            ServiceDescriptionTable.Read(
                Assert.IsType<SectionRead.Assembled>(Assert.Single(read)).Section)).Table;

        Assert.Equal(40, table.Services.Count);
        Assert.Equal<int>(
            Enumerable.Range(FirstServiceId, 40).ToArray(),
            table.Services.Select(service => service.ServiceId).ToArray());
    }

    [Fact]
    public void ASectionWhoseChecksumFailsLeavesTheTableIncompleteUntilItIsSentAgain()
    {
        var set = new SectionSet(ServiceDescriptionTable.ActualStreamTableId, SomeTransportStreamId);
        var stream = new ServiceStream(set);

        stream.Carry(SdtSection(version: 1, serviceCount: 1, sectionNumber: 0, lastSectionNumber: 1));
        var broken = stream.Carry(
            SdtSection(version: 1, serviceCount: 1, sectionNumber: 1, lastSectionNumber: 1, corruptChecksum: true));

        Assert.Equal(SectionDefect.ChecksumMismatch, Assert.IsType<SectionRead.Rejected>(Assert.Single(broken)).Defect);
        Assert.False(set.IsComplete);

        stream.Carry(SdtSection(version: 1, serviceCount: 1, sectionNumber: 1, lastSectionNumber: 1));

        Assert.True(set.TryComplete(out var sections));
        Assert.Equal(2, sections.Count);
    }

    [Fact]
    public void ATableSentAgainWithANewVersionReplacesWhatWasGatheredBefore()
    {
        var set = new SectionSet(ServiceDescriptionTable.ActualStreamTableId, SomeTransportStreamId);
        var stream = new ServiceStream(set);

        stream.Carry(SdtSection(version: 1, serviceCount: 1, sectionNumber: 0, lastSectionNumber: 1));
        stream.Carry(SdtSection(version: 1, serviceCount: 1, sectionNumber: 1, lastSectionNumber: 1));
        Assert.True(set.IsComplete);

        stream.Carry(SdtSection(version: 2, serviceCount: 2, sectionNumber: 0, lastSectionNumber: 1));

        Assert.False(set.IsComplete);
        Assert.Equal(2, set.VersionNumber);

        stream.Carry(SdtSection(version: 2, serviceCount: 2, sectionNumber: 1, lastSectionNumber: 1));

        Assert.True(set.TryComplete(out var sections));
        Assert.All(sections, section => Assert.Equal(2, section.VersionNumber));
        Assert.Equal(2, Table(sections[0]).Services.Count);
    }

    private sealed class ServiceStream
    {
        private readonly TransportStreamWriter writer = new(ServiceDescriptionTable.Pid);
        private readonly SectionReader reader = new(ServiceDescriptionTable.Pid);
        private readonly SectionSet set;
        private int carried;

        public ServiceStream(SectionSet set)
        {
            this.set = set;
        }

        public IReadOnlyList<SectionRead> Carry(byte[] section)
        {
            writer.Sections(section);
            var read = new List<SectionRead>();

            for (; carried < writer.Packets.Count; carried++)
            {
                read.AddRange(reader.Push(writer.Packets[carried]));
            }

            foreach (var assembled in read.OfType<SectionRead.Assembled>())
            {
                set.Add(assembled.Section);
            }

            return read;
        }
    }

    private static ServiceDescriptionTable Table(Section section)
        => Assert.IsType<TableRead<ServiceDescriptionTable>.Parsed>(ServiceDescriptionTable.Read(section)).Table;

    private static IEnumerable<byte[]> Interleave(IReadOnlyList<byte[]> first, IReadOnlyList<byte[]> second)
    {
        for (var at = 0; at < Math.Max(first.Count, second.Count); at++)
        {
            if (at < first.Count)
            {
                yield return first[at];
            }

            if (at < second.Count)
            {
                yield return second[at];
            }
        }
    }

    private static byte[] NitSection(int version)
        => new SectionWriter
        {
            TableId = NetworkInformationTable.ActualNetworkTableId,
            TableIdExtension = SomeNetworkId,
            VersionNumber = version,
            Body = new NitWriter
            {
                NetworkDescriptors = SiDescriptorWriter.NetworkName(new AribTextWriter().Kanji("試験").ToArray()),
                TransportStreams =
                [
                    NitWriter.TransportStream(
                        SomeTransportStreamId,
                        SomeNetworkId,
                        DescriptorWriter.Loop(
                            SiDescriptorWriter.ServiceList((FirstServiceId, (int)ServiceKind.Television)),
                            SiDescriptorWriter.TransportStreamInformation(
                                9,
                                new AribTextWriter().Kanji("試験").ToArray()))),
                ],
            }.ToBody(),
        }.ToBytes();

    private static byte[] SdtSection(
        int version,
        int serviceCount,
        int sectionNumber = 0,
        int lastSectionNumber = 0,
        bool corruptChecksum = false)
        => new SectionWriter
        {
            TableId = ServiceDescriptionTable.ActualStreamTableId,
            TableIdExtension = SomeTransportStreamId,
            VersionNumber = version,
            SectionNumber = sectionNumber,
            LastSectionNumber = lastSectionNumber,
            CorruptChecksum = corruptChecksum,
            Body = new SdtWriter
            {
                OriginalNetworkId = SomeNetworkId,
                Services = Enumerable.Range(0, serviceCount)
                    .Select(offset => SdtWriter.Service(
                        FirstServiceId + offset,
                        SiDescriptorWriter.Service(
                            (int)ServiceKind.Television,
                            [],
                            new AribTextWriter().Kanji("試験").KatakanaBySingleShift("テレビ").ToArray())))
                    .ToArray(),
            }.ToBody(),
        }.ToBytes();
}
