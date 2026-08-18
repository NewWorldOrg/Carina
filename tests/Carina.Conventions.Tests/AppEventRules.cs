using System.Reflection;

using Carina.Contracts;
using Carina.Domain.Events;

namespace Carina.Conventions.Tests;

public static class AppEventRules
{
    public static IReadOnlyList<string> AppEventSignals(IEnumerable<Type> types)
        => Describe(SignalMethods(types));

    public static IReadOnlyList<string> SignalsThatAcceptANameOutsideTheSet(IEnumerable<Type> types)
        => Describe(SignalMethods(types).Where(method => !NamesItsEventFromTheSet(method)));

    public static IReadOnlyList<string> SignalsThatCarryAPayload(IEnumerable<Type> types)
        => Describe(SignalMethods(types).Where(CarriesMoreThanTheName));

    private static IEnumerable<MethodInfo> SignalMethods(IEnumerable<Type> types)
        => types
            .SelectMany(type =>
                type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName)
            .Where(IsAnAppEventSignal);

    private static bool IsAnAppEventSignal(MethodInfo method)
    {
        if (method.GetParameters().Any(parameter => parameter.ParameterType == typeof(AppEventName)))
        {
            return true;
        }

        return typeof(IAppEventPublisher).IsAssignableFrom(method.DeclaringType) && IsSignalShaped(method.Name);
    }

    private static bool IsSignalShaped(string name)
        => name.StartsWith("Signal", StringComparison.Ordinal)
           || name.StartsWith("Publish", StringComparison.Ordinal);

    private static bool NamesItsEventFromTheSet(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();

        return parameters.Length > 0 && parameters[0].ParameterType == typeof(AppEventName);
    }

    private static bool CarriesMoreThanTheName(MethodInfo method)
        => method.GetParameters()
            .Skip(1)
            .Any(parameter => parameter.ParameterType != typeof(CancellationToken));

    private static IReadOnlyList<string> Describe(IEnumerable<MethodInfo> methods)
        => methods
            .Select(method =>
                $"{method.DeclaringType!.FullName}.{method.Name}({Parameters(method)})")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string Parameters(MethodInfo method)
        => string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name));
}
