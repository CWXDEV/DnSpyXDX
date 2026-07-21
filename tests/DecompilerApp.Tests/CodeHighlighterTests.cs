using DecompilerApp.UI;
using Xunit;

namespace DecompilerApp.Tests;

public sealed class CodeHighlighterTests
{
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
        Assert.Contains("code-string\">&quot;{ignored}&quot;", html, StringComparison.Ordinal);
    }
}
