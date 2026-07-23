using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DnSpyXDX.Application;

namespace DnSpyXDX.UI;

public static partial class CodeHighlighter
{
    private static readonly HashSet<string> Visibility = ["public", "private", "protected", "internal"];
    private static readonly HashSet<string> BuiltInTypes = ["bool", "byte", "char", "decimal", "double", "float", "int", "long", "object", "sbyte", "short", "string", "uint", "ulong", "ushort", "void", "dynamic"];
    private static readonly HashSet<string> Constants = ["false", "null", "true"];
    private static readonly HashSet<string> Keywords = ["abstract", "as", "async", "await", "base", "break", "case", "catch", "checked", "class", "const", "continue", "default", "delegate", "do", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "for", "foreach", "get", "if", "implicit", "in", "interface", "is", "lock", "namespace", "new", "null", "operator", "out", "override", "params", "readonly", "record", "ref", "required", "return", "sealed", "set", "sizeof", "stackalloc", "static", "struct", "switch", "this", "throw", "true", "try", "typeof", "unchecked", "unsafe", "using", "virtual", "volatile", "while", "yield"];

    public static string Highlight(string source, IReadOnlyDictionary<string, SymbolId?>? symbolLinks = null)
    {
        var output = new StringBuilder(source.Length + source.Length / 4);
        var braceDepth = 0;
        var nextBracePair = 0;
        var bracePairs = new Stack<(int Pair, int Depth)>();
        foreach (Match match in TokenRegex().Matches(source))
        {
            var token = match.Value;
            string? css;
            int? bracePair = null;
            if (match.Groups["comment"].Success) css = "code-comment";
            else if (match.Groups["literal"].Success) css = IsString(token) ? "code-string" : "code-number";
            else if (match.Groups["word"].Success) css = WordClass(source, match);
            else if (token == "{")
            {
                bracePair = nextBracePair++;
                bracePairs.Push((bracePair.Value, braceDepth));
                css = $"code-brace brace-{braceDepth % 7}";
                braceDepth++;
            }
            else if (token == "}")
            {
                if (bracePairs.TryPop(out var opening))
                {
                    bracePair = opening.Pair;
                    braceDepth = opening.Depth;
                }
                else braceDepth = Math.Max(0, braceDepth - 1);
                css = $"code-brace brace-{braceDepth % 7}";
            }
            else css = null;
            var encoded = WebUtility.HtmlEncode(token);
            if (css is null) { output.Append(encoded); continue; }
            // Only word tokens can be symbols, so comments and string literals that happen to
            // mention a name are never highlighted or made clickable.
            if (IsLinkable(css) && symbolLinks is not null && symbolLinks.TryGetValue(token, out var target))
            {
                // data-symbol groups the occurrences that highlight together; data-token is only
                // present when the name resolves to exactly one definition worth navigating to.
                output.Append("<span class=\"").Append(css).Append(" code-link\" data-symbol=\"").Append(encoded).Append('"');
                if (target is { } resolved) output.Append(" data-token=\"").Append(resolved.MetadataToken).Append('"');
                output.Append('>').Append(encoded).Append("</span>");
            }
            else
            {
                output.Append("<span class=\"").Append(css).Append('"');
                if (bracePair is { } pair) output.Append(" data-brace-pair=\"").Append(pair).Append('"');
                output.Append('>').Append(encoded).Append("</span>");
            }
        }
        return output.ToString();
    }

    // Membership in the link table is the real gate; this only rules out the classifications that
    // can never be a symbol reference, such as keywords and built-in type names.
    private static bool IsLinkable(string css) => css is "code-type" or "code-method" or "code-field" or "code-name";

    private static string? WordClass(string source, Match match)
    {
        var word = match.Value;
        if (Visibility.Contains(word)) return "code-visibility";
        if (Constants.Contains(word)) return "code-constant";
        if (Keywords.Contains(word)) return "code-keyword";
        var next = match.Index + match.Length;
        while (next < source.Length && char.IsWhiteSpace(source[next])) next++;
        if (next < source.Length && source[next] == '(') return "code-method";
        if (BuiltInTypes.Contains(word)) return "code-standard-type";
        if (char.IsUpper(word.TrimStart('@')[0])) return "code-type";
        return word.StartsWith('_') ? "code-field" : "code-name";
    }

    private static bool IsString(string token) => token.StartsWith('"') || token.StartsWith("@\"", StringComparison.Ordinal) || token.StartsWith('\'');

    [GeneratedRegex("(?<comment>//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/)|(?<literal>@?\"(?:\"\"|\\\\.|[^\"])*\"|'(?:\\\\.|[^'])+'|\\b\\d+(?:\\.\\d+)?\\b)|(?<word>@?[A-Za-z_][A-Za-z0-9_]*)|(?<other>[\\s\\S])", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();
}
