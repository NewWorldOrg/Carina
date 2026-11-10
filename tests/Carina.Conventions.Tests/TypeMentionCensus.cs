using System.Reflection;

namespace Carina.Conventions.Tests;

public static class TypeMentionCensus
{
    private const BindingFlags Everything = BindingFlags.Public
                                            | BindingFlags.NonPublic
                                            | BindingFlags.Instance
                                            | BindingFlags.Static
                                            | BindingFlags.DeclaredOnly;

    public static IReadOnlyList<string> MentionsOf(IEnumerable<Type> readers, IReadOnlySet<Type> named)
    {
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(named);

        return
        [
            .. readers
                .SelectMany(Mentions)
                .Where(mention => Unrolled(mention.Mentioned).Any(named.Contains))
                .Select(mention => mention.Where)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    public static IReadOnlyList<Type> TypesUnder(IEnumerable<Assembly> assemblies, Func<Type, bool> outermost)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(outermost);

        return [.. assemblies.SelectMany(assembly => assembly.GetTypes()).Where(type => outermost(Outermost(type)))];
    }

    private static Type Outermost(Type type)
    {
        Type outer = type;

        while (outer.DeclaringType is not null)
        {
            outer = outer.DeclaringType;
        }

        return outer;
    }

    private static IEnumerable<Mention> Mentions(Type reader)
        => Inherited(reader)
            .Concat(reader.GetFields(Everything).Select(field => new Mention(Named(reader, field), field.FieldType)))
            .Concat(MethodsOn(reader).SelectMany(method => MentionsIn(reader, method)));

    private static IEnumerable<Mention> Inherited(Type reader)
        => reader.GetInterfaces()
            .Append(reader.BaseType)
            .OfType<Type>()
            .Select(inherited => new Mention(reader.FullName!, inherited));

    private static IEnumerable<MethodBase> MethodsOn(Type reader)
        => reader.GetMethods(Everything).Cast<MethodBase>().Concat(reader.GetConstructors(Everything));

    private static IEnumerable<Mention> MentionsIn(Type reader, MethodBase method)
    {
        string where = Named(reader, method);

        return Signature(method)
            .Concat(Locals(method))
            .Concat(Reached(method, where))
            .Select(mentioned => new Mention(where, mentioned));
    }

    private static string Named(Type reader, MemberInfo member) => $"{reader.FullName}.{member.Name}";

    private static IEnumerable<Type> Signature(MethodBase method)
        => method.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Concat(method is MethodInfo returning ? [returning.ReturnType] : []);

    private static IEnumerable<Type> Locals(MethodBase method)
        => Body(method)?.LocalVariables.Select(local => local.LocalType) ?? [];

    private static IEnumerable<Type> Reached(MethodBase method, string where)
    {
        if (Body(method)?.GetILAsByteArray() is not { } il)
        {
            return [];
        }

        Type[] typeArguments = method.DeclaringType?.IsGenericTypeDefinition is true
            ? method.DeclaringType.GetGenericArguments()
            : [];
        Type[] methodArguments = method.IsGenericMethodDefinition ? method.GetGenericArguments() : [];

        return CallSiteCensus.TokensIn(il, where)
            .Select(token => Resolve(method.Module, token, typeArguments, methodArguments))
            .OfType<MemberInfo>()
            .SelectMany(TypesOf);
    }

    private static MethodBody? Body(MethodBase method)
    {
        try
        {
            return method.GetMethodBody();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static MemberInfo? Resolve(Module module, int token, Type[] typeArguments, Type[] methodArguments)
    {
        try
        {
            return module.ResolveMember(token, typeArguments, methodArguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<Type> TypesOf(MemberInfo member)
        => member switch
        {
            Type type => [type],
            FieldInfo field => Declaring(field).Append(field.FieldType),
            MethodBase called => Declaring(called)
                .Concat(Signature(called))
                .Concat(called.IsGenericMethod ? called.GetGenericArguments() : []),
            _ => [],
        };

    private static IEnumerable<Type> Declaring(MemberInfo member)
        => member.DeclaringType is { } declaring ? [declaring] : [];

    private static IEnumerable<Type> Unrolled(Type type)
    {
        if (type.HasElementType)
        {
            return Unrolled(type.GetElementType()!);
        }

        return type.IsGenericType
            ? type.GetGenericArguments().SelectMany(Unrolled).Prepend(type.GetGenericTypeDefinition())
            : [type];
    }

    private sealed record Mention(string Where, Type Mentioned);
}
