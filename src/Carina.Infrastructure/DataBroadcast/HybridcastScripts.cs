using System.Collections.Frozen;
using System.Text;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// One change to a script: the characters from <see cref="Start"/> up to <see cref="End"/> are replaced.
/// </summary>
public readonly record struct ScriptEdit(int Start, int End, string Replacement);

/// <summary>
/// Points a Hybridcast application's script at the stand-in the receiver script gives the frame for its location.
/// Inside the frame the location is this installation's, and an application that reads its own host from it would
/// ask the wrong server, so the location read as a global is rewritten: the bare name used as a value, assigned to
/// or not, becomes <see cref="StandIn"/>, and the location of window, self, document or globalThis becomes
/// <see cref="WindowStandIn"/>. The script is read token by token, so strings, template text, comments and regular
/// expressions are never touched, nor is a property name, a member of any other object, or a name only spelt
/// alike. A scope that declares a location of its own, or a self of its own, is left as it is; a window, document or
/// globalThis taken as a parameter is the one passed in, which is the global. A script longer than
/// <see cref="Largest"/> is not read at all, and one whose reading would take more than a few passes over it is
/// left as it is. In Shift_JIS the second byte of a character is never read as code.
/// </summary>
public static class HybridcastScripts
{
    public const int Largest = 2 * 1024 * 1024;

    public const string StandIn = "__hcLocation";

    public const string WindowStandIn = "window.__hcLocation";

    private const string Location = "location";

    private static readonly FrozenSet<string> Globals =
        new[] { "window", "self", "document", "globalThis" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Watched =
        new[] { Location, "self" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ObjectMemberStarts =
        new[] { "{", ",", "get", "set", "async", "*", "static" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ClassMemberStarts =
        new[] { "{", "}", ";", "get", "set", "async", "*", "static" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> BindingStarts =
        new[] { "(", ",", "{", "[", "..." }.ToFrozenSet(StringComparer.Ordinal);

    public static string? Rewritten(string source, bool shiftJis = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        IReadOnlyList<ScriptEdit> edits = Edits(source, shiftJis);

        return edits.Count == 0 ? null : Applied(source, edits);
    }

    public static IReadOnlyList<ScriptEdit> Edits(string source, bool shiftJis = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Length > Largest)
        {
            return [];
        }

        string read = shiftJis ? SecondBytesApart(source) : source;

        return new Reading(read, new ScriptScanner(read).Read()).Edits();
    }

    private static string SecondBytesApart(string source)
    {
        char[] read = source.ToCharArray();

        for (int at = 0; at + 1 < read.Length; at++)
        {
            if (read[at] is (>= '\u0081' and <= '\u009F') or (>= '\u00E0' and <= '\u00FC'))
            {
                at++;
                read[at] = read[at] < 0x80 ? '\u00FF' : read[at];
            }
        }

        return new string(read);
    }

    public static string Applied(string source, IReadOnlyList<ScriptEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(edits);

        StringBuilder written = new(source.Length + (edits.Count * 16));
        int from = 0;

        foreach (ScriptEdit edit in edits)
        {
            written.Append(source, from, edit.Start - from).Append(edit.Replacement);
            from = edit.End;
        }

        return written.Append(source, from, source.Length - from).ToString();
    }

    private sealed class Reading(string source, IReadOnlyList<ScriptToken> tokens)
    {
        private readonly Dictionary<string, int[]> declared = new(StringComparer.Ordinal);

        private readonly int budget = (8 * tokens.Count) + 1024;

        private int spent;

        private int Last => tokens.Count - 1;

        public IReadOnlyList<ScriptEdit> Edits()
        {
            for (int at = 0; at < tokens.Count; at++)
            {
                Declare(at);
            }

            if (spent > budget)
            {
                return [];
            }

            foreach (int[] marks in declared.Values)
            {
                for (int at = 1; at < marks.Length; at++)
                {
                    marks[at] += marks[at - 1];
                }
            }

            List<ScriptEdit> edits = [];

            for (int at = 0; at < tokens.Count; at++)
            {
                if (tokens[at].Kind is ScriptTokenKind.Name && Is(at, Location) && Edit(at) is { } edit)
                {
                    edits.Add(edit);
                }
            }

            return edits;
        }

        private string Text(int at) => at < 0 || at >= tokens.Count ? string.Empty : tokens[at].Text(source);

        private bool Is(int at, string text) => at >= 0 && at < tokens.Count && tokens[at].Is(source, text);

        private ScriptGroup GroupOf(int opener) => opener < 0 || opener >= tokens.Count ? ScriptGroup.None : tokens[opener].Group;

        private bool Opens(int at, ScriptGroup group) => GroupOf(at) == group && !tokens[at].Closes;

        private int EndOf(int opener) => opener < 0 || tokens[opener].Match < 0 ? Last : tokens[opener].Match;

        private void Declare(int at)
        {
            if (tokens[at].Kind is not ScriptTokenKind.Name || !Watched.Contains(Text(at)) || Is(at - 1, ".") || Is(at - 1, "?."))
            {
                return;
            }

            if (Scope(at) is not { } scope || scope.To < scope.From)
            {
                return;
            }

            string name = Text(at);

            if (!declared.TryGetValue(name, out int[]? marks))
            {
                marks = new int[tokens.Count + 1];
                declared[name] = marks;
            }

            marks[Math.Max(scope.From, 0)]++;
            marks[scope.To + 1]--;
        }

        private (int From, int To)? Scope(int at)
        {
            string before = at > 0 && tokens[at - 1].Kind is ScriptTokenKind.Name ? Text(at - 1) : string.Empty;
            ScriptPlace place = tokens[at].Place;

            string declaring = Is(at - 1, ",") && tokens[at].Declarer >= 0 ? Text(tokens[at].Declarer) : before;

            if (declaring is "var" or "function")
            {
                return (Math.Max(place.Function, 0), EndOf(place.Function));
            }

            if (declaring is "let" or "const" or "class" or "import" or "as" || (place.UnderBinding && BindingPosition(at)))
            {
                return (Math.Max(place.Scope, 0), EndOf(place.Scope));
            }

            if (Is(at + 1, "=>"))
            {
                return (at, ArrowEnd(at + 1));
            }

            return BindingPosition(at) ? ParameterScope(place.Outer) : null;
        }

        private bool BindingPosition(int at)
            => BindingStarts.Contains(Text(at - 1)) || (Is(at - 1, ":") && GroupOf(tokens[at].Parent) is ScriptGroup.Object or ScriptGroup.Binding);

        private (int From, int To)? ParameterScope(int holder)
        {
            if (GroupOf(holder) is ScriptGroup.Catch)
            {
                return (holder, FollowingEnd(tokens[holder].Match));
            }

            return GroupOf(holder) is ScriptGroup.Call && IsParameters(holder) ? (holder, FollowingEnd(tokens[holder].Match)) : null;
        }

        private bool IsParameters(int paren)
        {
            int closing = tokens[paren].Match;

            return closing >= 0 && (Is(closing + 1, "=>") || Opens(closing + 1, ScriptGroup.Function));
        }

        private int FollowingEnd(int closing)
        {
            if (closing < 0)
            {
                return Last;
            }

            return Is(closing + 1, "=>") ? ArrowEnd(closing + 1) : EndOf(Opens(closing + 1, ScriptGroup.Function) || Opens(closing + 1, ScriptGroup.Block) ? closing + 1 : -1);
        }

        private int ArrowEnd(int arrow)
        {
            if (Opens(arrow + 1, ScriptGroup.Function))
            {
                return EndOf(arrow + 1);
            }

            int at = arrow + 1;

            while (at <= Last && !EndsAnExpression(at) && spent++ <= budget)
            {
                at = Past(at);
            }

            return at - 1;
        }

        private int Past(int at)
        {
            int reached = at;

            while (tokens[reached].Match > reached && spent++ <= budget)
            {
                reached = tokens[reached].Match;
            }

            return reached + 1;
        }

        private bool EndsAnExpression(int at)
            => tokens[at].Closes || Is(at, ",") || Is(at, ";");

        private bool IsDeclared(string name, int at)
            => declared.TryGetValue(name, out int[]? marks) && marks[at] > 0;

        private ScriptEdit? Edit(int at)
        {
            if (Is(at - 1, "."))
            {
                return Member(at);
            }

            if (Is(at - 1, "?.") || IsDeclared(Location, at) || IsName(at))
            {
                return null;
            }

            ScriptToken token = tokens[at];

            return IsShorthand(at)
                ? new ScriptEdit(token.Start, token.End, $"{Location}: {StandIn}")
                : new ScriptEdit(token.Start, token.End, StandIn);
        }

        private ScriptEdit? Member(int at)
        {
            int holder = at - 2;

            if (holder < 0 || tokens[holder].Kind is not ScriptTokenKind.Name || !Globals.Contains(Text(holder)))
            {
                return null;
            }

            if (Is(holder - 1, ".") || Is(holder - 1, "?.") || IsDeclared(Text(holder), holder))
            {
                return null;
            }

            return new ScriptEdit(tokens[holder].Start, tokens[at].End, WindowStandIn);
        }

        private bool IsName(int at)
        {
            string before = Text(at - 1);
            ScriptGroup enclosing = GroupOf(tokens[at].Parent);

            if (Is(at + 1, ":") && !tokens[at + 1].Ternary && before != "case")
            {
                return true;
            }

            if (before is "break" or "continue")
            {
                return true;
            }

            if (enclosing is ScriptGroup.Class)
            {
                return ClassMemberStarts.Contains(before);
            }

            return enclosing is ScriptGroup.Object && ObjectMemberStarts.Contains(before) && Is(at + 1, "(");
        }

        private bool IsShorthand(int at)
            => GroupOf(tokens[at].Parent) is ScriptGroup.Object
               && (Is(at - 1, "{") || Is(at - 1, ","))
               && (Is(at + 1, ",") || Is(at + 1, "}"));
    }
}
