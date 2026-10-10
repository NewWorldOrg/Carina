namespace Carina.Broadcast.DsmCc;

public static class ModuleContent
{
    public static ModuleContentRead Open(ReadOnlyMemory<byte> module, ModuleInfo info, CarouselLimits limits)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(limits);

        ReadOnlyMemory<byte> entity = module;

        if (info.Compression is { } compression)
        {
            if (!compression.IsZlib)
            {
                return new ModuleContentRead.Rejected(CarouselDefect.UnsupportedCompression);
            }

            if (compression.OriginalSize > limits.LargestModule)
            {
                return new ModuleContentRead.Rejected(CarouselDefect.ModuleTooLarge);
            }

            if (!ModuleInflater.TryInflate(module, compression.OriginalSize, out byte[]? inflated, out CarouselDefect defect))
            {
                return new ModuleContentRead.Rejected(defect);
            }

            entity = inflated;
        }

        return ModuleEntity.Split(entity, info.Type, info.Name, limits.MostParts);
    }
}
