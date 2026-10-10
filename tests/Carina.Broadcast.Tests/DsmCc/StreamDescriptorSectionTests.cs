using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class StreamDescriptorSectionTests
{
    [Fact]
    public void BR_BV_001_TheSectionHandsBackItsEventsAndNptReferences()
    {
        StreamDescriptorSection read = Parse(new StreamDescriptorWriter
        {
            DataEventId = 3,
            EventMessageGroupId = 0x0123,
            VersionNumber = 7,
            Descriptors = DescriptorWriter.Loop(
                StreamDescriptorWriter.GeneralEvent(0x0123, StreamDescriptorWriter.Immediate, 0, 0x01, 0x0405, 0xAA, 0xBB),
                StreamDescriptorWriter.GeneralEvent(0x0123, StreamDescriptorWriter.Npt, 0x1_2345_6789, 0x02, 0x0406),
                StreamDescriptorWriter.NptReference(stc: 0x1_0000_0001, npt: 0x0_0000_0002, scaleNumerator: 1, scaleDenominator: 1, contentId: 5)),
        });

        Assert.Equal(3, read.DataEventId);
        Assert.Equal(0x0123, read.EventMessageGroupId);
        Assert.Equal(7, read.VersionNumber);

        GeneralEvent immediate = read.Events[0];
        Assert.Equal(0x0123, immediate.EventMessageGroupId);
        Assert.Equal(GeneralEvent.Immediate, immediate.TimeMode);
        Assert.Null(immediate.Npt);
        Assert.Equal(0x01, immediate.EventMessageType);
        Assert.Equal(0x0405, immediate.EventMessageId);
        Assert.Equal([0xAA, 0xBB], immediate.PrivateData.ToArray());

        GeneralEvent npt = read.Events[1];
        Assert.Equal(GeneralEvent.NptTime, npt.TimeMode);
        Assert.Equal(0x1_2345_6789, npt.Npt);
        Assert.Empty(npt.PrivateData.ToArray());

        NptReference reference = read.NptReferences.Single();
        Assert.Equal(0x1_0000_0001, reference.Stc);
        Assert.Equal(0x0_0000_0002, reference.Npt);
        Assert.Equal(5, reference.ContentId);
        Assert.False(reference.PostDiscontinuity);
        Assert.Equal(1, reference.ScaleNumerator);
        Assert.Equal(1, reference.ScaleDenominator);
    }

    [Fact]
    public void BR_BV_001_ADescriptorOfAnotherTagIsKeptButNotTakenForAnEvent()
    {
        StreamDescriptorSection read = Parse(new StreamDescriptorWriter
        {
            Descriptors = DescriptorWriter.Loop(
                DescriptorWriter.Of(0x1A, 0x00, 0x01),
                StreamDescriptorWriter.GeneralEvent(1, StreamDescriptorWriter.Immediate, 0, 0, 1)),
        });

        Assert.Equal(2, read.Descriptors.Count);
        Assert.Single(read.Events);
        Assert.Empty(read.NptReferences);
    }

    [Fact]
    public void BR_BV_001_AGeneralEventTooShortForItsFixedFieldsIsRejected()
    {
        TableRead<StreamDescriptorSection> read = Read(new StreamDescriptorWriter
        {
            Descriptors = DescriptorWriter.Of(StreamDescriptorWriter.GeneralEventTag, new byte[10]),
        });

        Assert.Equal(TableDefect.MalformedDescriptor, Defect(read));
    }

    [Fact]
    public void BR_BV_001_AnNptReferenceTooShortForItsFieldsIsRejected()
    {
        TableRead<StreamDescriptorSection> read = Read(new StreamDescriptorWriter
        {
            Descriptors = DescriptorWriter.Of(StreamDescriptorWriter.NptReferenceTag, new byte[17]),
        });

        Assert.Equal(TableDefect.MalformedDescriptor, Defect(read));
    }

    [Fact]
    public void BR_BV_001_ADescriptorRunningPastTheSectionIsRejected()
    {
        TableRead<StreamDescriptorSection> read = Read(new StreamDescriptorWriter
        {
            Descriptors = DescriptorWriter.Overrunning(StreamDescriptorWriter.GeneralEventTag, 40, 0x00),
        });

        Assert.Equal(TableDefect.MalformedDescriptor, Defect(read));
    }

    [Fact]
    public void BR_BV_001_AnotherTableIdIsRejected()
    {
        TableRead<StreamDescriptorSection> read = StreamDescriptorSection.Read(CarriedSection.Of(new SectionWriter
        {
            TableId = DsmCcWriter.DownloadDataBlockTableId,
        }));

        Assert.Equal(TableDefect.WrongTableId, Defect(read));
    }

    [Fact]
    public void BR_BV_001_NoBodyOfRandomBytesMakesTheReaderThrow()
    {
        var random = new Random(20261011);

        for (int round = 0; round < 2000; round++)
        {
            byte[] body = new byte[random.Next(0, 120)];
            random.NextBytes(body);

            if (body.Length >= 2)
            {
                body[0] = (byte)(round % 2 == 0 ? StreamDescriptorWriter.GeneralEventTag : StreamDescriptorWriter.NptReferenceTag);
            }

            _ = StreamDescriptorSection.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = DsmCcWriter.StreamDescriptorsTableId,
                Body = body,
            }));
        }
    }

    private static TableDefect Defect(TableRead<StreamDescriptorSection> read)
        => Assert.IsType<TableRead<StreamDescriptorSection>.Rejected>(read).Defect;

    private static TableRead<StreamDescriptorSection> Read(StreamDescriptorWriter writer)
        => StreamDescriptorSection.Read(CarriedSection.Of(writer.ToSection()));

    private static StreamDescriptorSection Parse(StreamDescriptorWriter writer)
        => Assert.IsType<TableRead<StreamDescriptorSection>.Parsed>(Read(writer)).Table;
}
