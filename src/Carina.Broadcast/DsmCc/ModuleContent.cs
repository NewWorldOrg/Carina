namespace Carina.Broadcast.DsmCc;

public static class ModuleContent
{
    public static ModuleContentRead Open(ReadOnlyMemory<byte> module, ModuleInfo info, long largestModule)
    {
        ArgumentNullException.ThrowIfNull(info);

        ReadOnlyMemory<byte> entity = module;

        if (info.Compression is { } compression)
        {
            if (!compression.IsZlib)
            {
                return new ModuleContentRead.Rejected(CarouselDefect.UnsupportedCompression);
            }

            if (compression.OriginalSize > largestModule || compression.OriginalSize > int.MaxValue)
            {
                return new ModuleContentRead.Rejected(CarouselDefect.ModuleTooLarge);
            }

            if (!ModuleInflater.TryInflate(module, (int)compression.OriginalSize, out byte[]? inflated, out CarouselDefect defect))
            {
                return new ModuleContentRead.Rejected(defect);
            }

            entity = inflated;
        }

        return ModuleEntity.TrySplit(entity, info.Type, info.Name, out IReadOnlyList<ModuleResource>? resources)
            ? new ModuleContentRead.Opened(resources)
            : new ModuleContentRead.Rejected(CarouselDefect.EntityMalformed);
    }
}
