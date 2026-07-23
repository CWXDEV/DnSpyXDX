namespace DnSpyXDX.UI;

public sealed record ThemeDescriptor(string Id, string Name);

public static class Themes
{
    public const string DefaultId = "default";

    public static IReadOnlyList<ThemeDescriptor> All { get; } =
    [
        new(DefaultId, "Default")
    ];

    public static string Normalize(string? id) =>
        All.Any(theme => string.Equals(theme.Id, id, StringComparison.Ordinal)) ? id! : DefaultId;
}
