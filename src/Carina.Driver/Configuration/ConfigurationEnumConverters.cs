using Carina.Contracts;

namespace Carina.Driver.Configuration;

/// <summary>
/// Reads the backend name, and reads anything else as unstated.
/// </summary>
/// <remarks>
/// A misspelt value has to come back as "tuner.backend: expected 'dvb' or 'fake'",
/// naming the setting the operator has to fix. Letting the reader fail instead
/// would collapse every mistake in the file into one message about JSON, and would
/// stop after the first.
/// </remarks>
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

/// <summary>Reads the device kind, and reads anything else as unstated.</summary>
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
