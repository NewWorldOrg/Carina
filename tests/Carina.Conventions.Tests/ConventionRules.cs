using System.Reflection;

using Carina.Api.Common;
using Carina.Domain.Auth;
using Carina.Domain.Base;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Conventions.Tests;

public static class ConventionRules
{
    public static IReadOnlyList<string> ControllersWithoutASingleInvokeAction(IEnumerable<Type> types)
        => types
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .Where(type =>
            {
                MethodInfo[] actions = PublicDeclaredInstanceMethods(type);
                return actions.Length != 1 || actions[0].Name != "Invoke";
            })
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> ServiceMethodsNotReturningAServiceResult(IEnumerable<Type> types)
        => types
            .Where(type => type.IsClass
                && !type.IsAbstract
                && type.Name.EndsWith("Service", StringComparison.Ordinal)
                && type.Namespace?.EndsWith(".Services", StringComparison.Ordinal) == true)
            .SelectMany(PublicDeclaredInstanceMethods)
            .Where(method => !ReturnsAServiceResult(method.ReturnType))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> MutableValueObjects(IEnumerable<Type> types)
        => types
            .Where(IsValueObject)
            .Where(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(property => property.SetMethod is not null))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> RehydratableTypesWithAPublicConstructor(IEnumerable<Type> types)
        => types
            .Where(type => type.IsClass
                && !type.IsAbstract
                && type.GetMethod("Rehydrate", BindingFlags.Public | BindingFlags.Static) is not null)
            .Where(type => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length > 0)
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> ControllerDependenciesOutsideTheServicesNamespace(
        IEnumerable<Type> types
    )
        => types
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type =>
                type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .SelectMany(constructor => constructor.GetParameters())
                    .Where(parameter => IsAnApplicationDependency(parameter.ParameterType))
                    .Where(parameter =>
                        parameter.ParameterType.Namespace?.EndsWith(
                            ".Services",
                            StringComparison.Ordinal
                        ) != true
                    )
                    .Select(parameter =>
                        $"{type.FullName}({parameter.ParameterType.FullName})"
                    )
            )
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsAnApplicationDependency(Type type)
    {
        string space = type.Namespace ?? string.Empty;

        return space.StartsWith("Carina.", StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> CarrierHandouts(IEnumerable<Type> types)
        => types
            .Where(HandlesACarrier)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && HandsOutOrHonoursACarrier(method)))
            .Select(Signature)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> CarrierHandoutsNamingNoTarget(IEnumerable<Type> types)
        => types
            .Where(HandlesACarrier)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && HandsOutOrHonoursACarrier(method)))
            .Where(method => method.GetParameters().All(parameter => parameter.ParameterType != typeof(PlaybackTarget)))
            .Select(Signature)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static MethodInfo[] PublicDeclaredInstanceMethods(Type type)
        => type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();

    private static bool ReturnsAServiceResult(Type returnType)
    {
        Type type = returnType;
        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
            {
                type = type.GetGenericArguments()[0];
            }
        }

        return typeof(ServiceResult).IsAssignableFrom(type);
    }

    private static bool IsValueObject(Type type)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(CommonValueObject<>))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HandlesACarrier(Type type)
        => typeof(IPlaybackTicketStore).IsAssignableFrom(type)
           || typeof(IPlaybackGrantStore).IsAssignableFrom(type)
           || type == typeof(PlaybackTicket)
           || type == typeof(PlaybackGrant);

    private static bool HandsOutOrHonoursACarrier(MethodInfo method)
        => method.ReturnType == typeof(void)
           || method.ReturnType == typeof(IssuedPlaybackTicket)
           || method.ReturnType == typeof(PlaybackTicket)
           || method.ReturnType == typeof(PlaybackGrant)
           || method.ReturnType == typeof(Subject);

    private static string Signature(MethodInfo method)
        => $"{method.DeclaringType!.FullName}.{method.Name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})";
}
