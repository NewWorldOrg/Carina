using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Carina.Contracts;

/// <summary>
/// How the driver and the app spell their messages.
/// </summary>
/// <remarks>
/// Source-generated so that the driver can be published ahead of time, where
/// reflection-based serialisation is not available. Unknown members are ignored,
/// which is what makes an additive contract change survive the pairing of an old
/// driver with a new app.
/// </remarks>
public static class DriverJson
{
    /// <summary>The generated metadata, for callers that pass a type info explicitly.</summary>
    public static DriverJsonContext Context => DriverJsonContext.Default;

    /// <summary>Writes <paramref name="value"/> in the contract's form.</summary>
    public static string Serialize<T>(T value, JsonTypeInfo<T>? typeInfo = null) =>
        JsonSerializer.Serialize(value, typeInfo ?? Resolve<T>());

    /// <summary>Reads <paramref name="json"/> as <typeparamref name="T"/>.</summary>
    public static T? Deserialize<T>(string json, JsonTypeInfo<T>? typeInfo = null) =>
        JsonSerializer.Deserialize(json, typeInfo ?? Resolve<T>());

    private static JsonTypeInfo<T> Resolve<T>() =>
        (JsonTypeInfo<T>)Context.GetTypeInfo(typeof(T))!;
}

/// <summary>Generated serialisation metadata for every message on the driver socket.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    // A member left out of the JSON keeps its default; a null stays visible so that
    // "no session" and "field not sent by this driver" do not read the same.
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
)]
[JsonSerializable(typeof(DriverHello))]
[JsonSerializable(typeof(StartSessionRequest))]
[JsonSerializable(typeof(TuningRequest))]
[JsonSerializable(typeof(SessionSnapshot))]
[JsonSerializable(typeof(TunerSnapshot))]
[JsonSerializable(typeof(IReadOnlyList<SessionSnapshot>))]
[JsonSerializable(typeof(IReadOnlyList<TunerSnapshot>))]
public sealed partial class DriverJsonContext : JsonSerializerContext;
