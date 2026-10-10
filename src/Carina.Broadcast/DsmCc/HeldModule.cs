namespace Carina.Broadcast.DsmCc;

internal sealed record HeldModule(int ModuleVersion, long ModuleSize, int BlockSize)
{
    public static HeldModule Of(ModuleInfo module, int blockSize) => new(module.ModuleVersion, module.ModuleSize, blockSize);

    public bool HoldsTheSameContentAs(ModuleInfo module) => ModuleVersion == module.ModuleVersion && ModuleSize == module.ModuleSize;
}
