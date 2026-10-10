using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class DownloadInfoIndicationTests
{
    private const long SomeTransaction = 0x8000_1234;

    private const long SomeDownload = 0x0102_0304;

    [Fact(DisplayName = "BR-BV-001: the indication hands back its transaction download block size and modules")]
    public void TheIndicationHandsBackItsTransactionDownloadBlockSizeAndModules()
    {
        DownloadInfoIndication read = Parse(new DiiWriter
        {
            TransactionId = SomeTransaction,
            DownloadId = SomeDownload,
            BlockSize = 4066,
            Modules =
            [
                DiiModule.Of(0x0000, 12_345, 3),
                DiiModule.Of(0x0001, 0x00FF_FFFF, 255),
            ],
        });

        Assert.Equal((uint)SomeTransaction, read.TransactionId);
        Assert.Equal((uint)SomeDownload, read.DownloadId);
        Assert.Equal(4066, read.BlockSize);
        Assert.Equal([0x0000, 0x0001], read.Modules.Select(module => module.ModuleId));
        Assert.Equal([12_345L, 0x00FF_FFFFL], read.Modules.Select(module => module.ModuleSize));
        Assert.Equal([3, 255], read.Modules.Select(module => module.ModuleVersion));
    }

    [Fact(DisplayName = "BR-BV-001: the module descriptors give the type name information and compression")]
    public void TheModuleDescriptorsGiveTheTypeNameInformationAndCompression()
    {
        DownloadInfoIndication read = Parse(new DiiWriter
        {
            Modules =
            [
                DiiModule.Of(
                    0x0000,
                    100,
                    1,
                    ModuleDescriptorWriter.Type("text/X-arib-bml"),
                    ModuleDescriptorWriter.Name("startup.bml"),
                    ModuleDescriptorWriter.Info("jpn", new AribTextWriter().Kanji("天気").ToArray()),
                    ModuleDescriptorWriter.Compression(4000)),
            ],
        });

        ModuleInfo module = read.Modules.Single();
        Assert.Equal("text/X-arib-bml", module.Type);
        Assert.Equal("startup.bml", module.Name);
        Assert.Equal(new ModuleInformation("jpn", "天気"), module.Info);
        Assert.Equal(new ModuleCompression(ModuleCompression.Zlib, 4000), module.Compression);
        Assert.Equal(4, module.Descriptors.Count);
    }

    [Fact(DisplayName = "BR-BV-001: a module without descriptors has no type name information or compression")]
    public void AModuleWithoutDescriptorsHasNoTypeNameInformationOrCompression()
    {
        ModuleInfo module = Parse(new DiiWriter { Modules = [DiiModule.Of(7, 10, 0)] }).Modules.Single();

        Assert.Null(module.Type);
        Assert.Null(module.Name);
        Assert.Null(module.Info);
        Assert.Null(module.Compression);
        Assert.Empty(module.Descriptors);
    }

    [Fact(DisplayName = "BR-BV-001: a compatibility descriptor and an adaptation header do not move where the modules start")]
    public void ACompatibilityDescriptorAndAnAdaptationHeaderDoNotMoveWhereTheModulesStart()
    {
        DiiWriter writer = new()
        {
            Compatibility = [0x00, 0x01, 0x02, 0x03],
            Modules = [DiiModule.Of(0x0010, 77, 2)],
        };
        byte[] message = writer.ToMessage();
        byte[] payload = message[DsmCcWriter.MessageHeaderSize..];
        byte[] adapted = DsmCcWriter.Message(
            DsmCcWriter.DownloadInfoIndicationMessageId,
            writer.TransactionId,
            payload,
            adaptation: [0x01, 0x02, 0x03, 0x04]);

        DownloadInfoIndication read = Parse(writer.Section(adapted));

        Assert.Equal(0x0010, read.Modules.Single().ModuleId);
        Assert.Equal(77, read.Modules.Single().ModuleSize);
    }

    [Fact(DisplayName = "BR-BV-001: another table id is rejected")]
    public void AnotherTableIdIsRejected()
    {
        TableRead<DownloadInfoIndication> read = DownloadInfoIndication.Read(CarriedSection.Of(new SectionWriter
        {
            TableId = DsmCcWriter.DownloadDataBlockTableId,
            Body = new DiiWriter().ToMessage(),
        }));

        Assert.Equal(TableDefect.WrongTableId, Defect(read));
    }

    [Theory(DisplayName = "BR-BV-001: a message that is not a download info indication is rejected")]
    [InlineData(0x12, DsmCcWriter.DownloadType, DsmCcWriter.DownloadInfoIndicationMessageId)]
    [InlineData(DsmCcWriter.ProtocolDiscriminator, 0x04, DsmCcWriter.DownloadInfoIndicationMessageId)]
    [InlineData(DsmCcWriter.ProtocolDiscriminator, DsmCcWriter.DownloadType, DsmCcWriter.DownloadServerInitiateMessageId)]
    public void AMessageThatIsNotADownloadInfoIndicationIsRejected(int protocol, int type, int messageId)
    {
        DiiWriter writer = new();
        byte[] payload = writer.ToMessage()[DsmCcWriter.MessageHeaderSize..];

        TableRead<DownloadInfoIndication> read = DownloadInfoIndication.Read(CarriedSection.Of(writer.Section(
            DsmCcWriter.Message(messageId, writer.TransactionId, payload, protocolDiscriminator: protocol, dsmccType: type))));

        Assert.Equal(TableDefect.UnexpectedMessage, Defect(read));
    }

    [Fact(DisplayName = "BR-BV-001: a message length past the section is rejected")]
    public void AMessageLengthPastTheSectionIsRejected()
    {
        DiiWriter writer = new() { Modules = [DiiModule.Of(1, 10, 0)] };
        byte[] payload = writer.ToMessage()[DsmCcWriter.MessageHeaderSize..];

        TableRead<DownloadInfoIndication> read = DownloadInfoIndication.Read(CarriedSection.Of(writer.Section(
            DsmCcWriter.Message(DsmCcWriter.DownloadInfoIndicationMessageId, writer.TransactionId, payload, declaredMessageLength: payload.Length + 1))));

        Assert.Equal(TableDefect.LoopOverrun, Defect(read));
    }

    [Fact(DisplayName = "BR-BV-001: an adaptation header longer than the message is rejected")]
    public void AnAdaptationHeaderLongerThanTheMessageIsRejected()
    {
        DiiWriter writer = new();
        byte[] payload = writer.ToMessage()[DsmCcWriter.MessageHeaderSize..];

        TableRead<DownloadInfoIndication> read = DownloadInfoIndication.Read(CarriedSection.Of(writer.Section(
            DsmCcWriter.Message(DsmCcWriter.DownloadInfoIndicationMessageId, writer.TransactionId, payload, declaredAdaptationLength: payload.Length + 1))));

        Assert.Equal(TableDefect.LoopOverrun, Defect(read));
    }

    [Fact(DisplayName = "BR-BV-001: more modules announced than carried is rejected")]
    public void MoreModulesAnnouncedThanCarriedIsRejected()
    {
        TableRead<DownloadInfoIndication> read = Read(new DiiWriter
        {
            Modules = [DiiModule.Of(1, 10, 0)],
            DeclaredModuleCount = 2,
        });

        Assert.Equal(TableDefect.LoopOverrun, Defect(read));
    }

    [Fact(DisplayName = "BR-BV-001: module information running past the message is rejected")]
    public void ModuleInformationRunningPastTheMessageIsRejected()
    {
        TableRead<DownloadInfoIndication> read = Read(new DiiWriter
        {
            Modules = [new DiiModule(1, 10, 0, ModuleDescriptorWriter.Name("a")) { DeclaredInfoLength = 200 }],
        });

        Assert.Equal(TableDefect.LoopOverrun, Defect(read));
    }

    [Fact(DisplayName = "BR-BV-001: private data running past the message is rejected")]
    public void PrivateDataRunningPastTheMessageIsRejected()
    {
        TableRead<DownloadInfoIndication> read = Read(new DiiWriter
        {
            Modules = [DiiModule.Of(1, 10, 0)],
            DeclaredPrivateDataLength = 5,
        });

        Assert.Equal(TableDefect.LoopOverrun, Defect(read));
    }

    [Fact(DisplayName = "BR-BV-001: private data that ends short of the message is rejected")]
    public void PrivateDataThatEndsShortOfTheMessageIsRejected()
    {
        TableRead<DownloadInfoIndication> read = Read(new DiiWriter
        {
            Modules = [DiiModule.Of(1, 10, 0)],
            PrivateData = [0x01, 0x02, 0x03],
            DeclaredPrivateDataLength = 1,
        });

        Assert.Equal(TableDefect.LoopOverrun, Defect(read));
    }

    [Fact(DisplayName = "BR-BV-001: a broken descriptor in the module information is rejected")]
    public void ABrokenDescriptorInTheModuleInformationIsRejected()
    {
        TableRead<DownloadInfoIndication> read = Read(new DiiWriter
        {
            Modules = [new DiiModule(1, 10, 0, DescriptorWriter.Overrunning(ModuleDescriptorWriter.NameTag, 9, 0x61))],
        });

        Assert.Equal(TableDefect.MalformedDescriptor, Defect(read));
    }

    [Fact(DisplayName = "BR-BV-001: a compression type descriptor too short for the original size is rejected")]
    public void ACompressionTypeDescriptorTooShortForTheOriginalSizeIsRejected()
    {
        TableRead<DownloadInfoIndication> read = Read(new DiiWriter
        {
            Modules = [DiiModule.Of(1, 10, 0, DescriptorWriter.Of(ModuleDescriptorWriter.CompressionTypeTag, 0x00, 0x00, 0x01))],
        });

        Assert.Equal(TableDefect.MalformedDescriptor, Defect(read));
    }

    [Fact(DisplayName = "BR-BV-001: an info descriptor too short for its language is rejected")]
    public void AnInfoDescriptorTooShortForItsLanguageIsRejected()
    {
        TableRead<DownloadInfoIndication> read = Read(new DiiWriter
        {
            Modules = [DiiModule.Of(1, 10, 0, DescriptorWriter.Of(ModuleDescriptorWriter.InfoTag, 0x6A, 0x70))],
        });

        Assert.Equal(TableDefect.MalformedDescriptor, Defect(read));
    }

    [Fact(DisplayName = "BR-BV-001: an indication cut short at any length is rejected without throwing")]
    public void AnIndicationCutShortAtAnyLengthIsRejectedWithoutThrowing()
    {
        DiiWriter writer = new()
        {
            Compatibility = [0x00, 0x00],
            Modules =
            [
                DiiModule.Of(0, 100, 1, ModuleDescriptorWriter.Name("startup.bml"), ModuleDescriptorWriter.Compression(400)),
                DiiModule.Of(1, 200, 2, ModuleDescriptorWriter.Type("image/png")),
            ],
            PrivateData = [0x01, 0x02],
        };
        byte[] whole = writer.ToMessage();

        for (int length = 0; length < whole.Length; length++)
        {
            TableRead<DownloadInfoIndication> read = DownloadInfoIndication.Read(CarriedSection.Of(writer.Section(whole[..length])));

            Assert.IsType<TableRead<DownloadInfoIndication>.Rejected>(read);
        }

        Assert.IsType<TableRead<DownloadInfoIndication>.Parsed>(DownloadInfoIndication.Read(CarriedSection.Of(writer.Section(whole))));
    }

    [Fact(DisplayName = "BR-BV-001: no body of random bytes makes the reader throw")]
    public void NoBodyOfRandomBytesMakesTheReaderThrow()
    {
        var random = new Random(20261010);

        for (int round = 0; round < 2000; round++)
        {
            byte[] body = new byte[random.Next(0, 160)];
            random.NextBytes(body);

            if (body.Length >= 4)
            {
                body[0] = DsmCcWriter.ProtocolDiscriminator;
                body[1] = DsmCcWriter.DownloadType;
                body[2] = 0x10;
                body[3] = 0x02;
            }

            _ = DownloadInfoIndication.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = DsmCcWriter.DownloadInfoIndicationTableId,
                Body = body,
            }));
        }
    }

    private static TableDefect Defect(TableRead<DownloadInfoIndication> read)
        => Assert.IsType<TableRead<DownloadInfoIndication>.Rejected>(read).Defect;

    private static TableRead<DownloadInfoIndication> Read(DiiWriter writer)
        => DownloadInfoIndication.Read(CarriedSection.Of(writer.ToSection()));

    private static DownloadInfoIndication Parse(DiiWriter writer) => Parse(writer.ToSection());

    private static DownloadInfoIndication Parse(SectionWriter section)
        => Assert.IsType<TableRead<DownloadInfoIndication>.Parsed>(DownloadInfoIndication.Read(CarriedSection.Of(section))).Table;
}
