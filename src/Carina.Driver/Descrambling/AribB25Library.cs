using System.Runtime.InteropServices;

namespace Carina.Driver.Descrambling;

[StructLayout(LayoutKind.Sequential)]
internal struct AribBuffer
{
    public unsafe byte* Data;

    public uint Size;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct BCasCard
{
    public void* PrivateData;

    public delegate* unmanaged<BCasCard*, void> Release;

    public delegate* unmanaged<BCasCard*, int> Init;

    public delegate* unmanaged<BCasCard*, void*, int> GetInitStatus;

    public delegate* unmanaged<BCasCard*, void*, int> GetId;

    public delegate* unmanaged<BCasCard*, void*, int> GetPowerOnControl;

    public delegate* unmanaged<BCasCard*, void*, byte*, int, int> ProcessEntitlementControl;

    public delegate* unmanaged<BCasCard*, byte*, int, int> ProcessEntitlementManagement;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AribStdB25
{
    public void* PrivateData;

    public delegate* unmanaged<AribStdB25*, void> Release;

    public delegate* unmanaged<AribStdB25*, int, int> SetMulti2Round;

    public delegate* unmanaged<AribStdB25*, int, int> SetStrip;

    public delegate* unmanaged<AribStdB25*, int, int> SetEntitlementManagementProcessing;

    public delegate* unmanaged<AribStdB25*, int, int> SetSimdMode;

    public delegate* unmanaged<AribStdB25*, int> GetSimdMode;

    public delegate* unmanaged<AribStdB25*, BCasCard*, int> SetCard;

    public delegate* unmanaged<AribStdB25*, int, int> SetUnitSize;

    public delegate* unmanaged<AribStdB25*, int> Reset;

    public delegate* unmanaged<AribStdB25*, int> Flush;

    public delegate* unmanaged<AribStdB25*, AribBuffer*, int> Put;

    public delegate* unmanaged<AribStdB25*, AribBuffer*, int> Get;

    public delegate* unmanaged<AribStdB25*, int> GetProgrammeCount;

    public delegate* unmanaged<AribStdB25*, void*, int, int> GetProgrammeInfo;

    public delegate* unmanaged<AribStdB25*, AribBuffer*, int> Withdraw;
}

internal sealed unsafe class AribB25Library
{
    public const string SharedObject = "libaribb25.so.0";

    private const string StandardEntryPoint = "create_arib_std_b25";

    private const string CardEntryPoint = "create_b_cas_card";

    private readonly delegate* unmanaged<AribStdB25*> createStandard;

    private readonly delegate* unmanaged<BCasCard*> createCard;

    private AribB25Library(nint standard, nint card)
    {
        createStandard = (delegate* unmanaged<AribStdB25*>)standard;
        createCard = (delegate* unmanaged<BCasCard*>)card;
    }

    public static AribB25Library? Load(out string whyNot)
    {
        if (!NativeLibrary.TryLoad(SharedObject, out nint handle))
        {
            whyNot = $"'{SharedObject}' is not installed on this machine.";

            return null;
        }

        if (!NativeLibrary.TryGetExport(handle, StandardEntryPoint, out nint standard))
        {
            whyNot = $"'{SharedObject}' carries no '{StandardEntryPoint}'.";

            return null;
        }

        if (!NativeLibrary.TryGetExport(handle, CardEntryPoint, out nint card))
        {
            whyNot = $"'{SharedObject}' carries no '{CardEntryPoint}'.";

            return null;
        }

        whyNot = string.Empty;

        return new AribB25Library(standard, card);
    }

    public AribStdB25* CreateStandard() => createStandard();

    public BCasCard* CreateCard() => createCard();
}
