using System.Collections.Frozen;

namespace Carina.Infrastructure.DataBroadcast;

internal enum ScriptTokenKind
{
    Name,

    PrivateName,

    Number,

    Text,

    Template,

    TemplateOpen,

    Pattern,

    Mark,
}

/// <summary>
/// What a bracket opens: the condition of a statement, a catch clause, a call or parameter list, an array, a
/// binding pattern, a block, a function body, an object literal, a class body, or a substitution in a template.
/// </summary>
internal enum ScriptGroup
{
    None,

    Control,

    Catch,

    Call,

    Array,

    Binding,

    Block,

    Function,

    Object,

    Class,

    Substitution,
}

/// <summary>
/// Where a token stands among the brackets: the one it stands inside, the function body and the scope around it,
/// the nearest bracket around it that is not an object, an array or a binding pattern, and whether a binding
/// pattern stands between it and that bracket. Each is a token index, or -1 for the script itself.
/// </summary>
internal readonly record struct ScriptPlace(int Parent, int Function, int Scope, int Outer, bool UnderBinding)
{
    public static ScriptPlace Script { get; } = new(-1, -1, -1, -1, false);

    public ScriptPlace Inside(int opener, ScriptGroup group)
    {
        bool pattern = group is ScriptGroup.Object or ScriptGroup.Array or ScriptGroup.Binding;

        return new ScriptPlace(
            opener,
            group is ScriptGroup.Function ? opener : Function,
            group is ScriptGroup.Function or ScriptGroup.Block or ScriptGroup.Class ? opener : Scope,
            pattern ? Outer : opener,
            group is ScriptGroup.Binding || (pattern && UnderBinding));
    }
}

/// <summary>
/// One token of a script: where it stands, the var, let or const its statement began with at its own depth (-1 for
/// none), and for a bracket, what it opens and the index of its partner; a colon knows whether it answers a question
/// mark.
/// </summary>
internal sealed class ScriptToken(ScriptTokenKind kind, int start, int end, ScriptPlace place, int declarer)
{
    public ScriptTokenKind Kind { get; } = kind;

    public int Start { get; } = start;

    public int End { get; } = end;

    public ScriptPlace Place { get; } = place;

    public int Parent => Place.Parent;

    public int Declarer { get; } = declarer;

    public ScriptGroup Group { get; set; }

    public int Match { get; set; } = -1;

    public bool Closes { get; set; }

    public bool Ternary { get; set; }

    public string Text(string source) => source[Start..End];

    public bool Is(string source, string text)
        => End - Start == text.Length && string.CompareOrdinal(source, Start, text, 0, text.Length) == 0;
}

/// <summary>
/// Reads a script into tokens in one pass and without recursion: strings with their escapes, template literals with
/// substitutions nested to any depth, line and block comments and the HTML-like comments a script may carry, and a
/// regular expression told from a division by the token before it. A bracket that closes a condition, a block or a
/// function body lets a regular expression follow; one that closes an expression does not. A regular expression that
/// does not close on its line makes that slash a division, and no later slash on the line is tried as one, so the
/// reading stays linear. A script that ends part way through anything is read to its end.
/// </summary>
internal sealed class ScriptScanner(string source)
{
    private static readonly FrozenSet<string> RegexAfter = new[]
    {
        "return", "typeof", "instanceof", "in", "of", "new", "delete", "void", "throw", "case", "do", "else", "yield", "await",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ObjectAfter = new[]
    {
        "return", "typeof", "instanceof", "in", "of", "new", "delete", "void", "throw", "case", "yield", "await",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Controls =
        new[] { "if", "while", "for", "with", "switch" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Declarations =
        new[] { "var", "let", "const" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ModuleNames =
        new[] { "import", "export" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly string[] LongMarks = ["...", "??=", "??", "?.", "=>", "++", "--", "/="];

    private readonly List<ScriptToken> tokens = [];

    private readonly List<int> open = [];

    private readonly List<ScriptPlace> places = [ScriptPlace.Script];

    private readonly List<int> questions = [0];

    private readonly List<int> declarers = [-1];

    private int regexFailsUntil = -1;

    private int classAt = -1;

    private int at;

    private bool lineStart = true;

    private bool classPending;

    public List<ScriptToken> Read()
    {
        while (at < source.Length)
        {
            Step();
        }

        return tokens;
    }

    private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c is '$' or '_' or '\\' || c >= 0x80;

    private static bool IsNamePart(char c) => IsNameStart(c) || char.IsAsciiDigit(c);

    private char Ahead(int by) => at + by < source.Length ? source[at + by] : '\0';

    private bool Starts(string text) => string.CompareOrdinal(source, at, text, 0, text.Length) == 0;

    private void Step()
    {
        char c = source[at];

        if (c is '\n' or '\r')
        {
            lineStart = true;
            at++;

            return;
        }

        if (c is ' ' or '\t' or '\v' or '\f')
        {
            at++;

            return;
        }

        if (Comment(c))
        {
            return;
        }

        lineStart = false;
        Token(c);
    }

    private bool Comment(char c)
    {
        if ((c == '/' && Ahead(1) == '/') || Starts("<!--") || (lineStart && Starts("-->")))
        {
            int ends = source.AsSpan(at).IndexOfAny('\n', '\r');
            at = ends < 0 ? source.Length : at + ends;

            return true;
        }

        if (c != '/' || Ahead(1) != '*')
        {
            return false;
        }

        int closes = source.IndexOf("*/", at + 2, StringComparison.Ordinal);
        int stops = closes < 0 ? source.Length : closes + 2;
        lineStart |= source.AsSpan(at, stops - at).IndexOfAny('\n', '\r') >= 0;
        at = stops;

        return true;
    }

    private void Token(char c)
    {
        if (c is '"' or '\'')
        {
            Quoted(c);
        }
        else if (c == '`')
        {
            Template();
        }
        else if (IsNameStart(c) || (c == '#' && IsNameStart(Ahead(1))))
        {
            Name(c == '#' ? ScriptTokenKind.PrivateName : ScriptTokenKind.Name);
        }
        else if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(Ahead(1))))
        {
            Number();
        }
        else if (c == '/' && RegexAllowed())
        {
            Pattern();
        }
        else
        {
            Punctuator(c);
        }
    }

    private int Emit(ScriptTokenKind kind, int end)
    {
        ScriptToken token = new(kind, at, Math.Min(end, source.Length), places[^1], declarers[^1]);
        tokens.Add(token);
        at = token.End;

        if (classPending && classAt == tokens.Count - 2 && kind is ScriptTokenKind.Mark && !token.Is(source, "{"))
        {
            classPending = false;
        }

        return tokens.Count - 1;
    }

    private void Name(ScriptTokenKind kind)
    {
        int end = at + (kind is ScriptTokenKind.PrivateName ? 1 : 0);

        while (end < source.Length && IsNamePart(source[end]))
        {
            end += source[end] == '\\' ? 2 : 1;
        }

        bool member = tokens.Count > 0 && tokens[^1].Is(source, ".");
        int index = Emit(kind, end);

        if (kind is ScriptTokenKind.Name && !member && tokens[index].Is(source, "class"))
        {
            classPending = true;
            classAt = index;
        }

        if (kind is ScriptTokenKind.Name && !member && Declarations.Contains(tokens[index].Text(source)))
        {
            declarers[^1] = index;
        }
    }

    private void Quoted(char quote)
    {
        int end = at + 1;

        while (end < source.Length && source[end] != quote && source[end] is not ('\n' or '\r'))
        {
            end += source[end] == '\\' ? Escaped(end) : 1;
        }

        Emit(ScriptTokenKind.Text, end < source.Length && source[end] == quote ? end + 1 : end);
    }

    private int Escaped(int backslash)
        => backslash + 2 < source.Length && source[backslash + 1] == '\r' && source[backslash + 2] == '\n' ? 3 : 2;

    private int Template()
    {
        int end = at + 1;

        while (end < source.Length)
        {
            char c = source[end];

            if (c == '`')
            {
                return Emit(ScriptTokenKind.Template, end + 1);
            }

            if (c == '$' && end + 1 < source.Length && source[end + 1] == '{')
            {
                return Opened(Emit(ScriptTokenKind.TemplateOpen, end + 2), ScriptGroup.Substitution);
            }

            end += c == '\\' ? 2 : 1;
        }

        return Emit(ScriptTokenKind.Template, source.Length);
    }

    private void Number()
    {
        bool hex = Starts("0x") || Starts("0X");
        int end = at;

        while (end < source.Length && IsNumberPart(end, hex))
        {
            end++;
        }

        Emit(ScriptTokenKind.Number, end);
    }

    private bool IsNumberPart(int index, bool hex)
    {
        char c = source[index];

        if (char.IsAsciiLetterOrDigit(c) || c is '_' or '.')
        {
            return true;
        }

        return !hex && c is '+' or '-' && index > at && source[index - 1] is 'e' or 'E';
    }

    private void Pattern()
    {
        int end = at + 1;
        bool inClass = false;

        while (end < source.Length && source[end] is not ('\n' or '\r') && (inClass || source[end] != '/'))
        {
            char c = source[end];
            inClass = c == '[' || (inClass && c != ']');
            end += c == '\\' ? 2 : 1;
        }

        if (end >= source.Length || source[end] != '/')
        {
            regexFailsUntil = end;
            Emit(ScriptTokenKind.Mark, at + 1);

            return;
        }

        end++;

        while (end < source.Length && IsNamePart(source[end]))
        {
            end++;
        }

        Emit(ScriptTokenKind.Pattern, end);
    }

    private bool RegexAllowed()
    {
        if (at < regexFailsUntil)
        {
            return false;
        }

        if (tokens.Count == 0)
        {
            return true;
        }

        ScriptToken last = tokens[^1];

        return last.Kind switch
        {
            ScriptTokenKind.Name => RegexAfter.Contains(last.Text(source)),
            ScriptTokenKind.TemplateOpen => true,
            ScriptTokenKind.Mark => MarkAllowsRegex(last),
            _ => false,
        };
    }

    private bool MarkAllowsRegex(ScriptToken last)
    {
        if (!last.Closes)
        {
            return !last.Is(source, "++") && !last.Is(source, "--");
        }

        return last.Is(source, ")") ? last.Group is ScriptGroup.Control : last.Is(source, "}") && last.Group is not ScriptGroup.Object;
    }

    private void Punctuator(char c)
    {
        string? mark = Array.Find(LongMarks, candidate => Starts(candidate) && !(candidate == "?." && char.IsAsciiDigit(Ahead(2))));

        if (mark is not null)
        {
            Emit(ScriptTokenKind.Mark, at + mark.Length);

            return;
        }

        switch (c)
        {
            case '(' or '[' or '{':
                Open(c);

                return;
            case ')' or ']' or '}':
                Close(c);

                return;
            case '?':
                questions[^1]++;
                Emit(ScriptTokenKind.Mark, at + 1);

                return;
            case ':':
                Colon();

                return;
            case ';':
                declarers[^1] = -1;
                Emit(ScriptTokenKind.Mark, at + 1);

                return;
            default:
                Emit(ScriptTokenKind.Mark, at + 1);

                return;
        }
    }

    private int Opened(int index, ScriptGroup group)
    {
        tokens[index].Group = group;
        open.Add(index);
        places.Add(places[^1].Inside(index, group));
        questions.Add(0);
        declarers.Add(-1);

        return index;
    }

    private void Open(char c)
    {
        ScriptGroup group = c switch
        {
            '(' => ParenGroup(),
            '[' => LastNameIs(Declarations) ? ScriptGroup.Binding : ScriptGroup.Array,
            _ => BraceGroup(),
        };

        Opened(Emit(ScriptTokenKind.Mark, at + 1), group);
    }

    private void Close(char c)
    {
        if (open.Count > 0 && c == '}' && tokens[open[^1]].Group is ScriptGroup.Substitution)
        {
            int substitution = Closed();
            int continued = Template();
            tokens[continued].Closes = true;
            tokens[substitution].Match = continued;

            return;
        }

        char opener = c switch
        {
            ')' => '(',
            ']' => '[',
            _ => '{',
        };

        if (open.Count == 0 || source[tokens[open[^1]].Start] != opener)
        {
            Emit(ScriptTokenKind.Mark, at + 1);

            return;
        }

        int match = Closed();
        ScriptToken closing = tokens[Emit(ScriptTokenKind.Mark, at + 1)];

        if (tokens[match].Group is ScriptGroup.Block or ScriptGroup.Class)
        {
            declarers[^1] = -1;
        }
        closing.Match = match;
        closing.Closes = true;
        closing.Group = tokens[match].Group;
        tokens[match].Match = tokens.Count - 1;
    }

    private int Closed()
    {
        int match = open[^1];
        open.RemoveAt(open.Count - 1);
        places.RemoveAt(places.Count - 1);
        questions.RemoveAt(questions.Count - 1);
        declarers.RemoveAt(declarers.Count - 1);

        return match;
    }

    private void Colon()
    {
        int index = Emit(ScriptTokenKind.Mark, at + 1);

        if (questions[^1] > 0)
        {
            questions[^1]--;
            tokens[index].Ternary = true;
        }
    }

    private bool LastNameIs(FrozenSet<string> names)
        => tokens.Count > 0 && tokens[^1].Kind is ScriptTokenKind.Name && names.Contains(tokens[^1].Text(source));

    private ScriptGroup ParenGroup()
    {
        if (LastNameIs(Controls))
        {
            return ScriptGroup.Control;
        }

        return tokens.Count > 0 && tokens[^1].Is(source, "catch") ? ScriptGroup.Catch : ScriptGroup.Call;
    }

    private ScriptGroup BraceGroup()
    {
        if (classPending)
        {
            classPending = false;

            return ScriptGroup.Class;
        }

        if (tokens.Count == 0)
        {
            return ScriptGroup.Block;
        }

        ScriptToken last = tokens[^1];

        return last.Kind switch
        {
            ScriptTokenKind.Mark => MarkBraceGroup(last),
            ScriptTokenKind.Name => NameBraceGroup(last.Text(source)),
            _ => ScriptGroup.Object,
        };
    }

    private ScriptGroup MarkBraceGroup(ScriptToken last)
    {
        if (last.Is(source, ")"))
        {
            return last.Group is ScriptGroup.Control or ScriptGroup.Catch ? ScriptGroup.Block : ScriptGroup.Function;
        }

        if (last.Is(source, "=>"))
        {
            return ScriptGroup.Function;
        }

        if (last.Is(source, ":"))
        {
            return last.Ternary || (open.Count > 0 && tokens[open[^1]].Group is ScriptGroup.Object) ? ScriptGroup.Object : ScriptGroup.Block;
        }

        return last.Is(source, ";") || last.Is(source, "{") || last.Is(source, "}") || last.Is(source, "]") ? ScriptGroup.Block : ScriptGroup.Object;
    }

    private static ScriptGroup NameBraceGroup(string name)
    {
        if (Declarations.Contains(name) || ModuleNames.Contains(name))
        {
            return ScriptGroup.Binding;
        }

        return ObjectAfter.Contains(name) ? ScriptGroup.Object : ScriptGroup.Block;
    }
}
