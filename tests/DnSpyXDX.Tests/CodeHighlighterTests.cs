using DnSpyXDX.Application;
using DnSpyXDX.UI;
using Xunit;

namespace DnSpyXDX.Tests;

public sealed class CodeHighlighterTests
{
    private static readonly Dictionary<string, SymbolId?> Links = new(StringComparer.Ordinal)
    {
        ["AudioSource"] = new SymbolId(Guid.Empty, 0x02000004),
        ["Overloaded"] = null
    };

    [Fact]
    public void Links_known_types_with_their_metadata_token()
    {
        var html = CodeHighlighter.Highlight("public AudioSource Source;", Links);

        Assert.Contains($"code-type code-link\" data-symbol=\"AudioSource\" data-token=\"{0x02000004}\">AudioSource", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Links_constructor_calls_to_the_type()
    {
        var html = CodeHighlighter.Highlight("var x = new AudioSource();", Links);

        Assert.Contains($"data-token=\"{0x02000004}\">AudioSource", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_link_type_names_inside_comments_or_strings()
    {
        var html = CodeHighlighter.Highlight("// AudioSource here\nvar name = \"AudioSource\";", Links);

        Assert.DoesNotContain("code-link", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Highlights_ambiguous_names_without_making_them_navigable()
    {
        var html = CodeHighlighter.Highlight("public void Overloaded(int value)", Links);

        Assert.Contains("data-symbol=\"Overloaded\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-token", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Groups_occurrences_by_symbol_name()
    {
        var html = CodeHighlighter.Highlight("AudioSource a = new AudioSource();", Links);

        Assert.Equal(2, html.Split("data-symbol=\"AudioSource\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Leaves_unknown_types_unlinked()
    {
        var html = CodeHighlighter.Highlight("public Widget Value;", Links);

        Assert.Contains("code-type\">Widget", html, StringComparison.Ordinal);
        Assert.DoesNotContain("code-link", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Distinguishes_method_names_from_types()
    {
        var html = CodeHighlighter.Highlight("public AudioSource GetContainer(AudioSource value)");

        Assert.Contains("code-type\">AudioSource", html, StringComparison.Ordinal);
        Assert.Contains("code-method\">GetContainer", html, StringComparison.Ordinal);
        Assert.Contains("code-visibility\">public", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Distinguishes_standard_types_strings_and_numbers()
    {
        var html = CodeHighlighter.Highlight("string value = \"brown\"; int count = 42;");

        Assert.Contains("code-standard-type\">string", html, StringComparison.Ordinal);
        Assert.Contains("code-string\">&quot;brown&quot;", html, StringComparison.Ordinal);
        Assert.Contains("code-number\">42", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Colors_nested_brace_pairs_in_sequence_and_ignores_string_braces()
    {
        var html = CodeHighlighter.Highlight("void Run() { if (true) { var text = \"{ignored}\"; } }");

        Assert.Equal(2, html.Split("code-brace brace-0", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, html.Split("code-brace brace-1", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, html.Split("data-brace-pair=\"0\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, html.Split("data-brace-pair=\"1\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("code-string\">&quot;{ignored}&quot;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_pair_braces_inside_comments_or_strings()
    {
        var html = CodeHighlighter.Highlight("void Run() { // } ignored\nvar text = \"{ignored}\";\n}");

        Assert.Equal(2, html.Split("data-brace-pair=\"0\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("data-brace-pair=\"1\"", html, StringComparison.Ordinal);
    }
}
