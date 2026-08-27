using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace Carina.Conventions.Tests;

public static class CallSiteCensus
{
    private const byte TwoByteOpCode = 0xFE;

    private static readonly IReadOnlyDictionary<short, OperandType> OperandsByOpCode = ReadOpCodeTable();

    private static readonly ConcurrentDictionary<string, Census> Taken = new(StringComparer.Ordinal);

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
            .. Of(assemblies).Calls
                .Where(call => call.Declaring == declaringType
                               && string.Equals(call.Called, methodName, StringComparison.Ordinal))
                .Select(call => call.Caller)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    public static int MethodsRead(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return Of(assemblies).Bodies;
    }

    private static Census Of(IEnumerable<Assembly> assemblies)
    {
        Assembly[] read = [.. assemblies];
        string key = string.Join("|", read.Select(assembly => assembly.FullName));

        return Taken.GetOrAdd(key, _ => Take(read));
    }

    private static Census Take(IReadOnlyList<Assembly> assemblies)
    {
        List<Call> calls = [];
        int bodies = 0;

        foreach (MethodBase caller in assemblies.SelectMany(EveryMethodIn))
        {
            if (Body(caller) is not { } il)
            {
                continue;
            }

            bodies++;

            Type[] typeArguments = caller.DeclaringType?.IsGenericTypeDefinition is true
                ? caller.DeclaringType.GetGenericArguments()
                : [];
            Type[] methodArguments = caller.IsGenericMethodDefinition ? caller.GetGenericArguments() : [];
            string named = Describe(caller);

            foreach (int token in TokensIn(il, named))
            {
                if (Resolve(caller.Module, token, typeArguments, methodArguments) is { } called)
                {
                    calls.Add(new Call(named, called.DeclaringType, called.Name));
                }
            }
        }

        return new Census(calls, bodies);
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

    private static IReadOnlyList<int> TokensIn(byte[] il, string named)
    {
        List<int> tokens = [];
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
                    $"Walking {named} reached 0x{code:X4}, which is not an opcode, so the census has lost the "
                    + "instruction boundaries and would be reporting calls it cannot vouch for.");
            }

            if (operand is OperandType.InlineMethod or OperandType.InlineTok)
            {
                tokens.Add(BitConverter.ToInt32(il, at));
            }

            at += Width(operand, il, at);
        }

        if (at != il.Length)
        {
            throw new InvalidOperationException(
                $"Walking {named} ran {at - il.Length} byte(s) past the end of a {il.Length} byte body, so the "
                + "census read an operand at the wrong width and cannot vouch for what it saw.");
        }

        return tokens;
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

    private sealed record Call(string Caller, Type? Declaring, string Called);

    private sealed record Census(IReadOnlyList<Call> Calls, int Bodies);
}
