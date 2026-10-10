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

    [Fact(DisplayName = "BR-BD-002: a module is complete when every block the size and block size call for has arrived in any order")]
    public void AModuleIsCompleteWhenEveryBlockTheSizeAndBlockSizeCallForHasArrivedInAnyOrder()
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

    [Fact(DisplayName = "BR-BD-002: a module missing a block is not complete")]
    public void AModuleMissingABlockIsNotComplete()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        IReadOnlyList<SectionWriter> blocks = DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock);

        IReadOnlyList<CarouselChange> changes = blocks.Skip(1).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Empty(changes);
        Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(assembler.Accept(Block(blocks[0]))));
    }

    [Fact(DisplayName = "BR-BD-002: a module of no bytes is completed by one empty block")]
    public void AModuleOfNoBytesIsCompletedByOneEmptyBlock()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, DiiModule.Of(1, 0, 0, ModuleDescriptorWriter.Type("image/png"))));

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Block(new DdbWriter { ModuleId = 1 }.ToSection()));

        Assert.Empty(Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes)).Module.Resources.Single().Body.ToArray());
    }

    [Fact(DisplayName = "BR-BV-003: a block before any indication is discarded without being reported, as it is right after tuning")]
    public void ABlockBeforeAnyIndicationIsDiscardedWithoutBeingReportedAsItIsRightAfterTuning()
    {
        ModuleAssembler assembler = new(EntryTag);

        Assert.Empty(assembler.Accept(Block(new DdbWriter { ModuleId = 1, Data = [0x00] }.ToSection())));
    }

    [Fact(DisplayName = "BR-BV-003: a block of a module the indication does not list is discarded")]
    public void ABlockOfAModuleTheIndicationDoesNotListIsDiscarded()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));

        CarouselChange.Rejected rejected = Rejected(assembler.Accept(Block(new DdbWriter { ModuleId = 2, Data = [0x00] }.ToSection())));

        Assert.Equal(CarouselDefect.NotInCatalogue, rejected.Defect);
        Assert.Equal(2, rejected.ModuleId);
    }

    [Fact(DisplayName = "BR-BV-003: a block of another download is discarded")]
    public void ABlockOfAnotherDownloadIsDiscarded()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Block(DsmCcWriter.Blocks(2, 1, 0, Css, SmallBlock)[0]));

        Assert.Equal(CarouselDefect.NotInCatalogue, Rejected(changes).Defect);
    }

    [Fact(DisplayName = "BR-BV-003: a block of an old version is discarded and does not break the assembly of the current one")]
    public void ABlockOfAnOldVersionIsDiscardedAndDoesNotBreakTheAssemblyOfTheCurrentOne()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 5)));
        IReadOnlyList<SectionWriter> current = DsmCcWriter.Blocks(1, 1, 5, Css, SmallBlock);
        byte[] stale = new byte[Css.Length];

        Assert.Equal(CarouselDefect.VersionMismatch, Rejected(assembler.Accept(Block(DsmCcWriter.Blocks(1, 1, 4, stale, SmallBlock)[0]))).Defect);

        IReadOnlyList<CarouselChange> changes = current.SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Equal(Css, Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes)).Module.Resources.Single().Body.ToArray());
    }

    [Fact(DisplayName = "BR-BV-003: a block number past what the size and block size call for is discarded")]
    public void ABlockNumberPastWhatTheSizeAndBlockSizeCallForIsDiscarded()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        int count = (Css.Length + SmallBlock - 1) / SmallBlock;

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Block(new DdbWriter { ModuleId = 1, BlockNumber = count, Data = [0x00] }.ToSection()));

        Assert.Equal(CarouselDefect.BlockOutOfRange, Rejected(changes).Defect);
    }

    [Theory(DisplayName = "BR-BV-003: a block of another length than its place calls for is discarded")]
    [InlineData(0, SmallBlock - 1)]
    [InlineData(0, SmallBlock + 1)]
    [InlineData(3, 1)]
    public void ABlockOfAnotherLengthThanItsPlaceCallsForIsDiscarded(int blockNumber, int length)
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

    [Fact(DisplayName = "BR-BV-003: a block received twice keeps the first and the module completes once")]
    public void ABlockReceivedTwiceKeepsTheFirstAndTheModuleCompletesOnce()
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

    [Fact(DisplayName = "BR-BS-002: a new transaction withdraws the modules it no longer lists and their blocks are discarded")]
    public void ANewTransactionWithdrawsTheModulesItNoLongerListsAndTheirBlocksAreDiscarded()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0), CssModule(2, 0)));

        DataCarouselCatalogue catalogue = Catalogue(assembler.Accept(Indication(1, SmallBlock, 0x8000_0004, CssModule(1, 0))));

        Assert.Equal([2], catalogue.Withdrawn);
        Assert.Equal([1], catalogue.Modules.Select(module => module.ModuleId));
        Assert.Equal(CarouselDefect.NotInCatalogue, Rejected(assembler.Accept(Block(DsmCcWriter.Blocks(1, 2, 0, Css, SmallBlock)[0]))).Defect);
    }

    [Fact(DisplayName = "BR-BS-002: a new version of a module drops the blocks held for the old one and completes again")]
    public void ANewVersionOfAModuleDropsTheBlocksHeldForTheOldOneAndCompletesAgain()
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

    [Fact(DisplayName = "BR-BS-002: a module completed before a new transaction that keeps its version is not completed again")]
    public void AModuleCompletedBeforeANewTransactionThatKeepsItsVersionIsNotCompletedAgain()
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

    [Fact(DisplayName = "BR-BS-002: a new transaction that keeps the version but changes the size drops the blocks held for the old size")]
    public void ANewTransactionThatKeepsTheVersionButChangesTheSizeDropsTheBlocksHeldForTheOldSize()
    {
        ModuleAssembler assembler = new(EntryTag);
        byte[] longer = [.. Css, .. Css];
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        assembler.Accept(Block(DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock)[^1]));

        assembler.Accept(Indication(1, SmallBlock, 0x8000_0004, DiiModule.Of(1, longer.Length, 0, ModuleDescriptorWriter.Type("text/css"))));
        IReadOnlyList<CarouselChange> changes = DsmCcWriter.Blocks(1, 1, 0, longer, SmallBlock).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Equal(longer, Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes)).Module.Resources.Single().Body.ToArray());
    }

    [Fact(DisplayName = "BR-BS-002: a new transaction that changes the block size drops the blocks held for the old block size")]
    public void ANewTransactionThatChangesTheBlockSizeDropsTheBlocksHeldForTheOldBlockSize()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        assembler.Accept(Block(DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock)[^1]));

        assembler.Accept(Indication(1, SmallBlock * 2, 0x8000_0004, CssModule(1, 0)));
        IReadOnlyList<CarouselChange> changes = DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock * 2).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Equal(Css, Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes)).Module.Resources.Single().Body.ToArray());
    }

    [Fact(DisplayName = "BR-BS-002: a completed module whose size changes under the same version is completed again")]
    public void ACompletedModuleWhoseSizeChangesUnderTheSameVersionIsCompletedAgain()
    {
        ModuleAssembler assembler = new(EntryTag);
        byte[] shorter = Css[..20];
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        _ = DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        assembler.Accept(Indication(1, SmallBlock, 0x8000_0004, DiiModule.Of(1, shorter.Length, 0, ModuleDescriptorWriter.Type("text/css"))));
        IReadOnlyList<CarouselChange> changes = DsmCcWriter.Blocks(1, 1, 0, shorter, SmallBlock).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Equal(shorter, Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes)).Module.Resources.Single().Body.ToArray());
    }

    [Fact(DisplayName = "BR-BS-002: the same transaction again changes nothing")]
    public void TheSameTransactionAgainChangesNothing()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));

        Assert.Empty(assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0))));
    }

    [Fact(DisplayName = "BR-BS-002: another download starts the carousel over")]
    public void AnotherDownloadStartsTheCarouselOver()
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));
        _ = DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        DataCarouselCatalogue catalogue = Catalogue(assembler.Accept(Indication(2, SmallBlock, CssModule(1, 0))));
        IReadOnlyList<CarouselChange> changes = DsmCcWriter.Blocks(2, 1, 0, Css, SmallBlock).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Equal(2u, catalogue.DownloadId);
        Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(changes));
    }

    [Theory(DisplayName = "BR-BV-001: a block size no section can carry is rejected and the catalogue stays as it was")]
    [InlineData(0)]
    [InlineData(ModuleAssembler.LargestBlock + 1)]
    public void ABlockSizeNoSectionCanCarryIsRejectedAndTheCatalogueStaysAsItWas(int blockSize)
    {
        ModuleAssembler assembler = new(EntryTag);
        assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0)));

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Indication(1, blockSize, 0x8000_0004, CssModule(2, 0)));
        IReadOnlyList<CarouselChange> completed = DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock).SelectMany(block => assembler.Accept(Block(block))).ToArray();

        Assert.Equal(CarouselDefect.BlockSizeOutOfRange, Rejected(changes).Defect);
        Assert.IsType<CarouselChange.ModuleCompleted>(Assert.Single(completed));
    }

    [Fact(DisplayName = "BR-BV-002: a module one byte over sixteen mebibytes is discarded and the rest of the catalogue stands")]
    public void AModuleOneByteOverSixteenMebibytesIsDiscardedAndTheRestOfTheCatalogueStands()
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

    [Fact(DisplayName = "BR-BV-002: a module whose original size is over sixteen mebibytes is discarded from the catalogue")]
    public void AModuleWhoseOriginalSizeIsOverSixteenMebibytesIsDiscardedFromTheCatalogue()
    {
        ModuleAssembler assembler = new(EntryTag);

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Indication(
            1,
            SmallBlock,
            DiiModule.Of(1, 100, 0, ModuleDescriptorWriter.Compression(CarouselLimits.Broadcast.LargestModule + 1))));

        Assert.Empty(Catalogue(changes).Modules);
        Assert.Equal(CarouselDefect.ModuleTooLarge, Assert.Single(changes.OfType<CarouselChange.Rejected>()).Defect);
    }

    [Fact(DisplayName = "BR-BV-002: a module needing more blocks than a block number can count is discarded")]
    public void AModuleNeedingMoreBlocksThanABlockNumberCanCountIsDiscarded()
    {
        ModuleAssembler assembler = new(EntryTag);

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Indication(1, 1, DiiModule.Of(1, ModuleAssembler.MostBlocks + 1, 0)));

        Assert.Equal(CarouselDefect.BlockCountOutOfRange, Assert.Single(changes.OfType<CarouselChange.Rejected>()).Defect);
    }

    [Fact(DisplayName = "BR-BV-002: a catalogue of one module more than the limit drops the carousel")]
    public void ACatalogueOfOneModuleMoreThanTheLimitDropsTheCarousel()
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
        Assert.Empty(assembler.Accept(Block(DsmCcWriter.Blocks(1, 1, 0, Css, SmallBlock)[0])));
    }

    [Fact(DisplayName = "BR-BV-002: the limits are sixteen carousels, five hundred and twelve modules, sixteen and sixty-four mebibytes and a thousand and twenty-four parts")]
    public void TheLimitsAreSixteenCarouselsFiveHundredTwelveModulesSixteenAndSixtyFourMebibytesAndOneThousandTwentyFourParts()
    {
        Assert.Equal(new CarouselLimits(16, 512, 16L * 1024 * 1024, 64L * 1024 * 1024, 1024), CarouselLimits.Broadcast);
    }

    [Fact(DisplayName = "BR-BV-001: the same module id listed twice keeps the first")]
    public void TheSameModuleIdListedTwiceKeepsTheFirst()
    {
        ModuleAssembler assembler = new(EntryTag);

        IReadOnlyList<CarouselChange> changes = assembler.Accept(Indication(1, SmallBlock, CssModule(1, 0), DiiModule.Of(1, 5, 7)));

        Assert.Equal([0], Catalogue(changes).Modules.Select(module => module.ModuleVersion));
        Assert.Equal(CarouselDefect.DuplicateModule, Assert.Single(changes.OfType<CarouselChange.Rejected>()).Defect);
    }

    [Fact(DisplayName = "BR-BV-002: a module that cannot be inflated is reported once and not assembled again")]
    public void AModuleThatCannotBeInflatedIsReportedOnceAndNotAssembledAgain()
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
