using System.Xml;

namespace DnSpyXDX.Export;

public static class SlnxWriter
{
    public static async Task WriteAsync(string path, IEnumerable<string> projectPaths, CancellationToken cancellationToken = default)
    {
        var settings = new XmlWriterSettings { Async = true, Indent = true, OmitXmlDeclaration = true, NewLineChars = "\n" };
        await using var stream = File.Create(path);
        await using var writer = XmlWriter.Create(stream, settings);
        await writer.WriteStartElementAsync(null, "Solution", null);
        foreach (var project in projectPaths.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteStartElementAsync(null, "Project", null);
            await writer.WriteAttributeStringAsync(null, "Path", null, project.Replace('\\', '/'));
            await writer.WriteEndElementAsync();
        }
        await writer.WriteEndElementAsync();
    }
}
