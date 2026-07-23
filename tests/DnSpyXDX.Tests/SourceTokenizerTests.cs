using DnSpyXDX.Application;
using DnSpyXDX.UI;
using Xunit;

namespace DnSpyXDX.Tests;

public sealed class SourceTokenizerTests
{
    private static readonly SourceDocumentKey Key = new(Guid.Empty, 0x02000001, "csharp", "default");
    private static readonly Dictionary<string, SymbolId?> Links = new(StringComparer.Ordinal)
    {
        ["AudioSource"] = new SymbolId(Guid.Empty, 0x02000004),
        ["Overloaded"] = null
    };

    [Fact]
    public void Classifies_existing_syntax_categories()
    {
        var (tokens, _) = SourceTokenizer.Tokenize("public AudioSource Run(string value = \"x\", int count = 42)", SourceTokenizerState.Initial, Links);

        Assert.Equal(SourceTokenKind.Visibility, Kind(tokens, "public", "public AudioSource Run(string value = \"x\", int count = 42)"));
        Assert.Equal(SourceTokenKind.Type, Kind(tokens, "AudioSource", "public AudioSource Run(string value = \"x\", int count = 42)"));
        Assert.Equal(SourceTokenKind.Method, Kind(tokens, "Run", "public AudioSource Run(string value = \"x\", int count = 42)"));
        Assert.Contains(tokens, token => token.Kind == SourceTokenKind.String);
        Assert.Contains(tokens, token => token.Kind == SourceTokenKind.Number);
        Assert.Equal(0x02000004, tokens.Single(token => token.SymbolName == "AudioSource").Target?.MetadataToken);
    }

    [Fact]
    public void Does_not_resolve_names_in_comments_or_strings()
    {
        var (comment, _) = SourceTokenizer.Tokenize("// AudioSource", SourceTokenizerState.Initial, Links);
        var (literal, _) = SourceTokenizer.Tokenize("\"AudioSource\"", SourceTokenizerState.Initial, Links);

        Assert.All(comment, token => Assert.Null(token.Target));
        Assert.All(literal, token => Assert.Null(token.Target));
    }

    [Fact]
    public void Carries_block_comment_state_across_lines()
    {
        var (first, state) = SourceTokenizer.Tokenize("/* open", SourceTokenizerState.Initial);
        var (second, end) = SourceTokenizer.Tokenize("still */ public", state);

        Assert.Equal(SourceLexicalMode.BlockComment, state.Mode);
        Assert.Equal(SourceTokenKind.Comment, first.Single().Kind);
        Assert.Equal(SourceTokenKind.Comment, second[0].Kind);
        Assert.Contains(second, token => token.Kind == SourceTokenKind.Visibility);
        Assert.Equal(SourceLexicalMode.Normal, end.Mode);
    }

    [Fact]
    public void Carries_verbatim_and_raw_string_state_across_lines()
    {
        var (_, verbatim) = SourceTokenizer.Tokenize("var x = @\"open", SourceTokenizerState.Initial);
        var (verbatimEnd, normal) = SourceTokenizer.Tokenize("close\"; public", verbatim);
        var (_, raw) = SourceTokenizer.Tokenize("var y = \"\"\"open", SourceTokenizerState.Initial);
        var (rawEnd, rawNormal) = SourceTokenizer.Tokenize("close\"\"\"; public", raw);

        Assert.Equal(SourceLexicalMode.VerbatimString, verbatim.Mode);
        Assert.Contains(verbatimEnd, token => token.Kind == SourceTokenKind.Visibility);
        Assert.Equal(SourceLexicalMode.Normal, normal.Mode);
        Assert.Equal(SourceLexicalMode.RawString, raw.Mode);
        Assert.Contains(rawEnd, token => token.Kind == SourceTokenKind.Visibility);
        Assert.Equal(SourceLexicalMode.Normal, rawNormal.Mode);
    }

    [Fact]
    public void Carries_brace_pairs_and_ignores_braces_in_literals()
    {
        var (first, state) = SourceTokenizer.Tokenize("void M() { var x = \"{\";", SourceTokenizerState.Initial);
        var (second, end) = SourceTokenizer.Tokenize("}", state);
        var opening = first.Single(token => token.Kind == SourceTokenKind.Brace);
        var closing = second.Single(token => token.Kind == SourceTokenKind.Brace);

        Assert.Equal(opening.BracePair, closing.BracePair);
        Assert.Equal(0, opening.BraceDepth);
        Assert.Equal(0, end.BraceDepth);
    }

    [Fact]
    public async Task Range_tokenization_resumes_from_checkpoints_with_correct_state()
    {
        var lines = Enumerable.Range(0, 300).Select(index => index switch
        {
            120 => "/* open",
            140 => "close */ public class C",
            _ => $"line_{index}"
        });
        var model = SourceDocumentModel.Create(Key, string.Join('\n', lines));

        var warmup = await model.TokenizeLinesAsync(260, 1);
        var range = await model.TokenizeLinesAsync(130, 12);

        Assert.Single(warmup);
        Assert.Equal(SourceLexicalMode.BlockComment, range[0].StartState.Mode);
        Assert.All(range.Take(10), line => Assert.All(line.Tokens, token => Assert.Equal(SourceTokenKind.Comment, token.Kind)));
        Assert.Contains(range[^2].Tokens, token => token.Kind == SourceTokenKind.Visibility);
    }

    [Fact]
    public async Task Tokenized_lines_expose_starting_brace_depth_for_visible_guides()
    {
        var model = SourceDocumentModel.Create(Key, "class C\n{\n    void M()\n    {\n        Call();\n    }\n}");

        var lines = await model.TokenizeLinesAsync(4, 1);

        Assert.Equal(2, Assert.Single(lines).StartState.BraceDepth);
        Assert.Equal([0, 4], Assert.Single(lines).StartState.Braces.Select(brace => brace.Column));
    }

    [Fact]
    public void Brace_guides_use_visual_columns_with_tabs()
    {
        var (_, state) = SourceTokenizer.Tokenize("\t{", SourceTokenizerState.Initial);

        Assert.Equal(4, Assert.Single(state.Braces).Column);
    }

    [Fact]
    public async Task Range_tokenization_honors_cancellation()
    {
        var model = SourceDocumentModel.Create(Key, string.Join('\n', Enumerable.Repeat("public class C {}", 10_000)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.TokenizeLinesAsync(9_000, 100, cancellationToken: cancellation.Token));
    }

    private static SourceTokenKind Kind(IEnumerable<SourceToken> tokens, string value, string line) =>
        tokens.Single(token => line.AsSpan(token.Start, token.Length).SequenceEqual(value)).Kind;
}
