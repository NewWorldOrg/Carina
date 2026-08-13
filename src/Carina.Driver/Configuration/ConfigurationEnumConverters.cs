using Carina.Contracts;

namespace Carina.Driver.Configuration;

internal sealed class TunerBackendConverter : TolerantEnumConverter<TunerBackend>
{
    protected override string NameOf(TunerBackend value) =>
        value switch
        {
            TunerBackend.Dvb => "dvb",
            TunerBackend.Fake => "fake",
            _ => "unspecified",
        };

    protected override TunerBackend? ValueOf(string name) =>
        name switch
        {
            "dvb" => TunerBackend.Dvb,
            "fake" => TunerBackend.Fake,
            _ => null,
        };
}

internal sealed class DeviceKindConverter : TolerantEnumConverter<DeviceKind>
{
    protected override string NameOf(DeviceKind value) =>
        value switch
        {
            DeviceKind.Terrestrial => "terrestrial",
            DeviceKind.Satellite => "satellite",
            _ => "unspecified",
        };

    protected override DeviceKind? ValueOf(string name) =>
        name switch
        {
            "terrestrial" => DeviceKind.Terrestrial,
            "satellite" => DeviceKind.Satellite,
            _ => null,
        };
}
