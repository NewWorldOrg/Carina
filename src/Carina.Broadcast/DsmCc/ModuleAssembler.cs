namespace Carina.Broadcast.DsmCc;

public sealed class ModuleAssembler
{
    public const int LargestBlock = 4066;

    public const int MostBlocks = 0x1_0000;

    private static readonly IReadOnlyList<CarouselChange> Nothing = [];

    private readonly CarouselLimits limits;
    private readonly Dictionary<int, PendingModule> pending = [];
    private readonly Dictionary<int, HeldModule> completed = [];

    private Dictionary<int, ModuleInfo> admitted = [];
    private DownloadInfoIndication? current;

    public ModuleAssembler(int componentTag)
        : this(componentTag, CarouselLimits.Broadcast)
    {
    }

    public ModuleAssembler(int componentTag, CarouselLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        ComponentTag = componentTag;
        this.limits = limits;
    }

    public int ComponentTag { get; }

    public long DeclaredSize { get; private set; }

    public IReadOnlyList<CarouselChange> Accept(DownloadInfoIndication indication)
        => Accept(indication, limits.LargestTotal);

    public IReadOnlyList<CarouselChange> Accept(DownloadInfoIndication indication, long room)
    {
        ArgumentNullException.ThrowIfNull(indication);

        if (current is { } held && held.DownloadId == indication.DownloadId && held.TransactionId == indication.TransactionId)
        {
            return Nothing;
        }

        if (indication.BlockSize is <= 0 or > LargestBlock)
        {
            return [new CarouselChange.Rejected(ComponentTag, CarouselDefect.BlockSizeOutOfRange, null)];
        }

        if (indication.Modules.Count > limits.MostModules)
        {
            return Drop(CarouselDefect.TooManyModules);
        }

        var changes = new List<CarouselChange>();
        Dictionary<int, ModuleInfo> next = Admit(indication, changes);
        long total = next.Values.Sum(module => module.ModuleSize);

        if (total > room)
        {
            return Drop(CarouselDefect.TotalTooLarge);
        }

        int[] withdrawn = admitted.Keys.Where(moduleId => !next.ContainsKey(moduleId)).Order().ToArray();
        bool sameDownload = current?.DownloadId == indication.DownloadId;

        Forget(
            pending,
            moduleId => !sameDownload || !next.TryGetValue(moduleId, out ModuleInfo? kept) || pending[moduleId].Held != HeldModule.Of(kept, indication.BlockSize));
        Forget(
            completed,
            moduleId => !sameDownload || !next.TryGetValue(moduleId, out ModuleInfo? kept) || !completed[moduleId].HoldsTheSameContentAs(kept));
        admitted = next;
        current = indication;
        DeclaredSize = total;
        changes.Insert(0, new CarouselChange.CatalogueUpdated(
            ComponentTag,
            new DataCarouselCatalogue(indication.DownloadId, indication.TransactionId, indication.BlockSize, [.. next.Values], withdrawn)));

        return changes;
    }

    public IReadOnlyList<CarouselChange> Accept(DownloadDataBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (current is null)
        {
            return Nothing;
        }

        if (block.DownloadId != current.DownloadId || !admitted.TryGetValue(block.ModuleId, out ModuleInfo? module))
        {
            return Rejected(CarouselDefect.NotInCatalogue, block.ModuleId);
        }

        if (block.ModuleVersion != module.ModuleVersion)
        {
            return Rejected(CarouselDefect.VersionMismatch, block.ModuleId);
        }

        if (completed.ContainsKey(block.ModuleId))
        {
            return Nothing;
        }

        int blockSize = current.BlockSize;
        int count = BlockCount(module.ModuleSize, blockSize);

        if (block.BlockNumber >= count)
        {
            return Rejected(CarouselDefect.BlockOutOfRange, block.ModuleId);
        }

        long expected = Math.Min(blockSize, module.ModuleSize - ((long)block.BlockNumber * blockSize));

        if (block.Data.Length != expected)
        {
            return Rejected(CarouselDefect.BlockSizeMismatch, block.ModuleId);
        }

        return Place(module, block, count, blockSize);
    }

    public void Reset()
    {
        pending.Clear();
        completed.Clear();
        admitted = [];
        current = null;
        DeclaredSize = 0;
    }

    private IReadOnlyList<CarouselChange> Place(ModuleInfo module, DownloadDataBlock block, int count, int blockSize)
    {
        if (!pending.TryGetValue(module.ModuleId, out PendingModule? assembling))
        {
            assembling = new PendingModule(HeldModule.Of(module, blockSize), count);
            pending[module.ModuleId] = assembling;
        }

        if (!assembling.Place(block.BlockNumber, block.Data.Span, blockSize) || !assembling.IsComplete)
        {
            return Nothing;
        }

        pending.Remove(module.ModuleId);
        completed[module.ModuleId] = assembling.Held;

        return ModuleContent.Open(assembling.Bytes, module, limits) switch
        {
            ModuleContentRead.Opened opened => [new CarouselChange.ModuleCompleted(
                ComponentTag,
                new CompletedModule(module.ModuleId, module.ModuleVersion, module.Name, opened.Resources))],
            ModuleContentRead.Rejected rejected => Rejected(rejected.Defect, module.ModuleId),
            _ => Nothing,
        };
    }

    private Dictionary<int, ModuleInfo> Admit(DownloadInfoIndication indication, List<CarouselChange> changes)
    {
        var next = new Dictionary<int, ModuleInfo>();

        foreach (ModuleInfo module in indication.Modules)
        {
            CarouselDefect? refused = Refusal(module, indication.BlockSize, next);

            if (refused is { } defect)
            {
                changes.Add(new CarouselChange.Rejected(ComponentTag, defect, module.ModuleId));

                continue;
            }

            next[module.ModuleId] = module;
        }

        return next;
    }

    private CarouselDefect? Refusal(ModuleInfo module, int blockSize, Dictionary<int, ModuleInfo> next)
    {
        if (next.ContainsKey(module.ModuleId))
        {
            return CarouselDefect.DuplicateModule;
        }

        if (module.ModuleSize > limits.LargestModule || module.Compression?.OriginalSize > limits.LargestModule)
        {
            return CarouselDefect.ModuleTooLarge;
        }

        return BlockCount(module.ModuleSize, blockSize) > MostBlocks ? CarouselDefect.BlockCountOutOfRange : null;
    }

    private static void Forget<TValue>(Dictionary<int, TValue> held, Func<int, bool> stale)
    {
        foreach (int moduleId in held.Keys.Where(stale).ToArray())
        {
            held.Remove(moduleId);
        }
    }

    private IReadOnlyList<CarouselChange> Drop(CarouselDefect defect)
    {
        Reset();

        return [new CarouselChange.Dropped(ComponentTag, defect)];
    }

    private IReadOnlyList<CarouselChange> Rejected(CarouselDefect defect, int moduleId)
        => [new CarouselChange.Rejected(ComponentTag, defect, moduleId)];

    private static int BlockCount(long moduleSize, int blockSize)
        => (int)Math.Max(1, (moduleSize + blockSize - 1) / blockSize);
}
