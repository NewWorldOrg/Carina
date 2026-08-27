using System.Reflection;
using System.Reflection.Emit;

namespace Carina.Conventions.Tests;

public static class CallSiteCensus
{
    private const byte TwoByteOpCode = 0xFE;

    private static readonly IReadOnlyDictionary<short, OperandType> OperandsByOpCode = ReadOpCodeTable();

    public static IReadOnlyList<string> CallersOf(
        IEnumerable<Assembly> assemblies,
        Type declaringType,
        string methodName)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(methodName);

        return
        [
            .. assemblies
                .SelectMany(EveryMethodIn)
                .Where(caller => CallsIt(caller, declaringType, methodName))
                .Select(Describe)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    public static int MethodsRead(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return assemblies.SelectMany(EveryMethodIn).Count(method => Body(method) is not null);
    }

    private static IEnumerable<MethodBase> EveryMethodIn(Assembly assembly)
        => assembly.GetTypes().SelectMany(EveryMethodOn);

    private static IEnumerable<MethodBase> EveryMethodOn(Type type)
    {
        const BindingFlags Everything = BindingFlags.Public
                                        | BindingFlags.NonPublic
                                        | BindingFlags.Instance
                                        | BindingFlags.Static
                                        | BindingFlags.DeclaredOnly;

        return type.GetMethods(Everything).Cast<MethodBase>().Concat(type.GetConstructors(Everything));
    }

    private static bool CallsIt(MethodBase caller, Type declaringType, string methodName)
        => Called(caller).Any(called =>
            called.DeclaringType == declaringType
            && string.Equals(called.Name, methodName, StringComparison.Ordinal));

    private static IEnumerable<MethodBase> Called(MethodBase caller)
    {
        if (Body(caller) is not { } il)
        {
            yield break;
        }

        Type[] typeArguments = caller.DeclaringType?.IsGenericTypeDefinition is true
            ? caller.DeclaringType.GetGenericArguments()
            : [];
        Type[] methodArguments = caller.IsGenericMethodDefinition ? caller.GetGenericArguments() : [];

        foreach (int token in TokensIn(il))
        {
            MethodBase? called = Resolve(caller.Module, token, typeArguments, methodArguments);

            if (called is not null)
            {
                yield return called;
            }
        }
    }

    private static byte[]? Body(MethodBase method)
    {
        try
        {
            return method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static MethodBase? Resolve(Module module, int token, Type[] typeArguments, Type[] methodArguments)
    {
        try
        {
            return module.ResolveMethod(token, typeArguments, methodArguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<int> TokensIn(byte[] il)
    {
        int at = 0;

        while (at < il.Length)
        {
            short code = il[at] is TwoByteOpCode && at + 1 < il.Length
                ? unchecked((short)((TwoByteOpCode << 8) | il[at + 1]))
                : il[at];
            at += il[at] is TwoByteOpCode ? 2 : 1;

            if (!OperandsByOpCode.TryGetValue(code, out OperandType operand))
            {
                throw new InvalidOperationException(
                    $"The census walked into 0x{code:X4}, which is not an opcode it knows, so it has lost the "
                    + "instruction boundaries and cannot be trusted for the rest of this method.");
            }

            if (operand is OperandType.InlineMethod or OperandType.InlineTok)
            {
                yield return BitConverter.ToInt32(il, at);
            }

            at += Width(operand, il, at);
        }
    }

    private static int Width(OperandType operand, byte[] il, int at)
        => operand switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
            _ => 4,
        };

    private static string Describe(MethodBase method)
    {
        Type type = method.DeclaringType!;
        string name = Enclosing(method.Name) ?? method.Name;

        while (type.DeclaringType is not null && type.Name.StartsWith('<'))
        {
            name = Enclosing(type.Name) ?? name;
            type = type.DeclaringType;
        }

        return $"{type.FullName}.{name}";
    }

    private static string? Enclosing(string name)
    {
        int opening = 0;

        while (opening < name.Length && name[opening] is '<')
        {
            opening++;
        }

        if (opening is 0)
        {
            return null;
        }

        int closing = name.IndexOf('>', opening);

        return closing > opening ? name[opening..closing] : null;
    }

    private static Dictionary<short, OperandType> ReadOpCodeTable()
        => typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => code.Value, code => code.OperandType);
}
