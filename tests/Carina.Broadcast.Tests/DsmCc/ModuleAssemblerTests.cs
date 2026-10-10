using System.Text;
using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class ModuleAssemblerTests
{
    private const int EntryTag = 0x40;

    private const int SmallBlock = 16;

    private static readonly byte[] Css = Encoding.ASCII.GetBytes("p{color:red;}body{margin:0;}div{padding:1px;}span{}");

    [Fact]
    public void BR_BD_002_AModuleIsCompleteWhenEveryBlockTheSizeAndBlockSizeCallForHasArrivedInAnyOrder()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(0x0001, 3)));
        IReadOnlyList<SectionWriter> blocks = DsmCcWriter.Blocks(1, 0x0001, 3, Css, SmallBlock);

        List<CarouselChange> changes = [];

        foreach (SectionWriter block in blocks.Reverse())
        {
            changes.AddRange(assembler.Accept(Block(block)));
        }

        CompletedModule module = Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes)).Module;
        Assert.Equal(EntryTag, changes[0].ComponentTag);
        Assert.Equal(0x0001, module.ModuleId);
        Assert.Equal(3, module.ModuleVersion);
        Assert.Equal("a.css", module.Name);
        Assert.Equal(Css, module.Resources.Single().Body.ToArray());
    }

    [Fact]
    public void BR_BD_002_AModuleMissingABlockIsNotComplete()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        IReadOnlyList<SectionWriter> blocks = DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock);

        IReadOnlyList<CarouselChange> changes = blocks.Skip(1).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Empty(changes);
        Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(assembler.Accept(Block(blocks[0]))));
    }

    [Fact]
    public void BR_BD_002_AModuleOfNoBytesIsCompletedByOneEmptyBlock()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, DiiModule.Of(1, 0, 0, ModuleDescriptorWriter.Type("image/png"))));

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Block(new DdbWriter { ModuleId = 1 }.ToSection()));

        Assert.Empty(Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes)).Module.Resources.Single().Body.ToArray());
    }

    [Fact]
    public void BR_BV_003_ABlockBeforeAnyIndicationIsDiscarded()
    {
        ModuleAssembler assembler = new(EntryTag);

        Assert.Equal(CarouselDefect.NotInCatalogue, Rejected(assembler.Accept(Block(new DdbWriter { ModuleId = 1, Data = [0x00] }.ToSection()))).Defect);
    }

    [Fact]
    public void BR_BV_003_ABlockOfAModuleTheIndicationDoesNotListIsDiscarded()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));

        CarouselChange.Rejected rejected = Rejected(assembler.Accept(Block(new DdbWriter { ModuleId = 2, Data = [0x00] }.ToSection())));

        Assert.Equal(CarouselDefect.NotInCatalogue, rejected.Defect);
        Assert.Equal(2, rejected.ModuleId);
    }

    [Fact]
    public void BR_BV_003_ABlockOfAnotherDownloadIsDiscarded()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Block(DsmCcWriter.Blocks(2, 1, 0, Css, SmallBlock)[0]));

        Assert.Equal(CarouselDefect.NotInCatalogue, Rejected(changes).Defect);
    }

    [Fact]
    public void BR_BV_003_ABlockOfAnOldVersionIsDiscardedAndDoesNotBreakTheAssemblyOfTheCurrentOne()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 5)));
        IReadOnlyList<SectionWriter> current = DsmCcWriter.Blocks(1, 1, 5, Css, SmallBlock);
        byte[] stale = new byte[Css.Length];

        Assert.Equal(CarouselDefect.VersionMismatch, Rejected(assembler.Accept(Block(DsmCcWriter.Blocks(1, 1, 4, stale, SmallBlock)[0]))).Defect);

        IReadOnlyList<CarouselChange> changes = current.SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Equal(Css, Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes)).Module.Resources.Single().Body.ToArray());
    }

    [Fact]
    public void BR_BV_003_ABlockNumberPastWhatTheSizeAndBlockSizeCallForIsDiscarded()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        int count = (Css.Length + SmallBlock - 1) / SmallBlock;

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Block(new DdbWriter { ModuleId = 1, BlockNumber = count, Data = [0x00] }.ToSection()));

        Assert.Equal(CarouselDefect.BlockOutOfRange, Rejected(changes).Defect);
    }

    [Theory]
    [InlineData(0, SmallBlock - 1)]
    [InlineData(0, SmallBlock + 1)]
    [InlineData(3, 1)]
    public void BR_BV_003_ABlockOfAnotherLengthThanItsPlaceCallsForIsDiscarded(int blockNumber, int length)
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Block(new DdbWriter
        {
            ModuleId = 1,
            BlockNumber = blockNumber,
            Data = new byte[length],
        }.ToSection()));

        Assert.Equal(CarouselDefect.BlockSizeMismatch, Rejected(changes).Defect);
    }

    [Fact]
    public void BR_BV_003_ABlockReceivedTwiceKeepsTheFirstAndTheModuleCompletesOnce()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        IReadOnlyList<SectionWriter> blocks = DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock);
        byte[] other = new byte[SmallBlock];

        IReadOnlyList<CarouselChange> first = assembler.Accept(Block(blocks[0]));
        IReadOnlyList<CarouselChange> twice = assembler.Accept(Block(new DdbWriter { ModuleId = 1, Data = other }.ToSection()));
        IReadOnlyList<CarouselChange> completed = blocks.Skip(1).SelectMany(block => assembler.Accept(Block(block))).ToArray();
        IReadOnlyList<CarouselChange> again = blocks.SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Empty(first);
        Assert.Empty(twice);
        Assert.Equal(Css, Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(completed)).Module.Resources.Single().Body.ToArray());
        Assert.Empty(again);
    }

    [Fact]
    public void BR_BS_002_ANewTransactionWithdrawsTheModulesItNoLongerListsAndTheirBlocksAreDiscarded()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0), CssModule(2, 0)));

        DataCarouselCatalogue catalogue = Catalogue(assembler.Accept(Indication(1, SmallBlock, 0x8000_0004, CssModule(1, 0))));

        Assert.Equal([2], catalogue.Withdrawn);
        Assert.Equal([1], catalogue.Modules.Select(module => module.ModuleId));
        Assert.Equal(CarouselDefect.NotInCatalogue, Rejected(assembler.Accept(Block(DsmCcWriter.Blocks(1, 2, 0, Css, SmallBlock)[0]))).Defect);
    }

    [Fact]
    public void BR_BS_002_ANewVersionOfAModuleDropsTheBlocksHeldForTheOldOneAndCompletesAgain()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        assembler.Accept(Block(DsmCcWriter.Blocks(1, 1, 0, new byte[Css.Length], SmallBlock)[1]));

        assembler.Accept(Indication(1, SmallBlock, 0x8000_0004, CssModule(1, 1)));
        IReadOnlyList<SectionWriter> blocks = DsmCcWriter.Blocks(1, 1, 1, Css, SmallBlock);
        IReadOnlyList<CarouselChange> changes = blocks.SelectMany(block => assembler.Accept(Block(block))).ToArray();

        CompletedModule module = Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes)).Module;
        Assert.Equal(1, module.ModuleVersion);
        Assert.Equal(Css, module.Resources.Single().Body.ToArray());
    }

    [Fact]
    public void BR_BS_002_AModuleCompletedBeforeANewTransactionThatKeepsItsVersionIsNotCompletedAgain()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        IReadOnlyList<SectionWriter> blocks = DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock);
        _ = blocks.SelectMany(block => assembler.Accept(Block(block))).ToArray();

        DataCarouselCatalogue catalogue = Catalogue(assembler.Accept(Indication(1, SmallBlock, 0x8000_0004, CssModule(1, 0), CssModule(2, 0))));
        IReadOnlyList<CarouselChange> again = blocks.SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Empty(catalogue.Withdrawn);
        Assert.Empty(again);
    }

    [Fact]
    public void BR_BS_002_TheSameTransactionAgainChangesNothing()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));

        Assert.Empty(assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0))));
    }

    [Fact]
    public void BR_BS_002_AnotherDownloadStartsTheCarouselOver()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        _ = DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        DataCarouselCatalogue catalogue = Catalogue(assembler.Accept(Indication(2, SmallBlock, CssModule(1, 0))));
        IReadOnlyList<CarouselChange> changes = DsmCcWriter.Blocks(2, 1, 0, Css, SmallBlock).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Equal(2u, catalogue.DownloadId);
        Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ModuleAssembler.LargestBlock + 1)]
    public void BR_BV_001_ABlockSizeNoSectionCanCarryIsRejectedAndTheCatalogueStaysAsItWas(int blockSize)
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Indication(1, blockSize, 0x8000_0004, CssModule(2, 0)));
        IReadOnlyList<CarouselChange> completed = DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Equal(CarouselDefect.BlockSizeOutOfRange, Rejected(changes).Defect);
        Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(completed));
    }

    [Fact]
    public void BR_BV_002_AModuleOneByteOverSixteenMebibytesIsDiscardedAndTheRestOfTheCatalogueStands()
    {
        ModuleAssembler assembler = new(EntryTag);

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Indication(
            1,
            DsmCcWriter.LargestBlock,
            DiiModule.Of(1, CarouselLimits.Broadcast.LargestModule + 1, 0),
            DiiModule.Of(2, CarouselLimits.Broadcast.LargestModule, 0)));

        Assert.Equal([2], Catalogue(changes).Modules.Select(module => module.ModuleId));
        CarouselChange.Rejected rejected = Assert.Single(changes.OfType<CarouselChange.Rejected>());
        Assert.Equal(CarouselDefect.ModuleTooLarge, rejected.Defect);
        Assert.Equal(1, rejected.ModuleId);
        Assert.Equal(CarouselLimits.Broadcast.LargestModule, assembler.DeclaredSize);
    }

    [Fact]
    public void BR_BV_002_AModuleWhoseOriginalSizeIsOverSixteenMebibytesIsDiscardedFromTheCatalogue()
    {
        ModuleAssembler assembler = new(EntryTag);

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Indication(
            1,
            SmallBlock,
            DiiModule.Of(1, 100, 0, ModuleDescriptorWriter.Compression(CarouselLimits.Broadcast.LargestModule + 1))));

        Assert.Empty(Catalogue(changes).Modules);
        Assert.Equal(CarouselDefect.ModuleTooLarge, Assert.Single(changes.OfType<CarouselChange.Rejected>()).Defect);
    }

    [Fact]
    public void BR_BV_002_AModuleNeedingMoreBlocksThanABlockNumberCanCountIsDiscarded()
    {
        ModuleAssembler assembler = new(EntryTag);

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Indication(1, 1, DiiModule.Of(1, ModuleAssembler.MostBlocks + 1, 0)));

        Assert.Equal(CarouselDefect.BlockCountOutOfRange, Assert.Single(changes.OfType<CarouselChange.Rejected>()).Defect);
    }

    [Fact]
    public void BR_BV_002_ACatalogueOfOneModuleMoreThanTheLimitDropsTheCarousel()
    {
        CarouselLimits limits = CarouselLimits.Broadcast with { MostModules = 4 };
        ModuleAssembler assembler = new(EntryTag, limits);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Indication(
            1,
            SmallBlock,
            0x8000_0004,
            Enumerable.Range(0, limits.MostModules + 1).Select(id => DiiModule.Of(id, 10, 0)).ToArray()));

        CarouselChange.Dropped dropped = Assert.IsType<CarouselChange.Dropped>(Assert.Single(changes));
        Assert.Equal(CarouselDefect.TooManyModules, dropped.Defect);
        Assert.Equal(0, assembler.DeclaredSize);
        Assert.Equal(CarouselDefect.NotInCatalogue, Rejected(assembler.Accept(Block(DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock)[0]))).Defect);
    }

    [Fact]
    public void BR_BV_002_TheLimitsAreSixteenCarouselsFiveHundredTwelveModulesSixteenAndSixtyFourMebibytes()
    {
        Assert.Equal(new CarouselLimits(16, 512, 16L * 1024 * 1024, 64L * 1024 * 1024), CarouselLimits.Broadcast);
    }

    [Fact]
    public void BR_BV_001_TheSameModuleIdListedTwiceKeepsTheFirst()
    {
        ModuleAssembler assembler = new(EntryTag);

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0), DiiModule.Of(1, 5, 7)));

        Assert.Equal([0], Catalogue(changes).Modules.Select(module => module.ModuleVersion));
        Assert.Equal(CarouselDefect.DuplicateModule, Assert.Single(changes.OfType<CarouselChange.Rejected>()).Defect);
    }

    [Fact]
    public void BR_BV_002_AModuleThatCannotBeInflatedIsReportedOnceAndNotAssembledAgain()
    {
        ModuleAssembler assembler = new(EntryTag);
        byte[] compressed = EntityWriter.Zlib(new byte[100]);
        assembler.Accept(Indication(1, SmallBlock, DiiModule.Of(1, compressed.Length, 0, ModuleDescriptorWriter.Compression(50))));
        IReadOnlyList<SectionWriter> blocks = DsmCcWriter.Blocks(1, 1, 0, compressed, SmallBlock);

        IReadOnlyList<CarouselChange> first = blocks.SelectMany(block => assembler.Accept(Block(block))).ToArray();
        IReadOnlyList<CarouselChange> again = blocks.SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Equal(CarouselDefect.OriginalSizeExceeded, Rejected(first).Defect);
        Assert.Empty(again);
    }

    private static DiiModule CssModule(int moduleId, int version)
        => DiiModule.Of(moduleId, Css.Length, version, ModuleDescriptorWriter.Type("text/css"), ModuleDescriptorWriter.Name("a.css"));

    private static DownloadInfoIndication Indication(long downloadId, int blockSize, params DiiModule[] modules)
        => Indication(downloadId, blockSize, 0x8000_0002, modules);

    private static DownloadInfoIndication Indication(long downloadId, int blockSize, long transactionId, params DiiModule[] modules)
        => Assert.IsType<TableRead<DownloadInfoIndication>.Parsed>(DownloadInfoIndication.Read(CarriedSection.Of(new DiiWriter
        {
            DownloadId = downloadId,
            TransactionId = transactionId,
            BlockSize = blockSize,
            Modules = modules,
        }.ToSection()))).Table;

    private static DownloadDataBlock Block(SectionWriter section)
        => Assert.IsType<TableRead<DownloadDataBlock>.Parsed>(DownloadDataBlock.Read(CarriedSection.Of(section))).Table;

    private static CarouselChange.Rejected Rejected(IReadOnlyList<CarouselChange> changes)
        => Assert.IsType<CarouselChange.Rejected>(Assert.Single(changes));

    private static DataCarouselCatalogue Catalogue(IReadOnlyList<CarouselChange> changes)
        => Assert.Single(changes.OfType<CarouselChange.CatalogueUpdated>()).Catalogue;
}
