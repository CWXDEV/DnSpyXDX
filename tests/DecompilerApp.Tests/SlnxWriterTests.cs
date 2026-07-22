using DecompilerApp.Export;
using Xunit;

namespace DecompilerApp.Tests;

public sealed class SlnxWriterTests
{
    [Fact]
    public async Task Writes_projects_in_deterministic_order()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dnspyxdx-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "test.slnx");
            await SlnxWriter.WriteAsync(path, ["src/Z/Z.csproj", "src/A/A.csproj"]);
            var xml = await File.ReadAllTextAsync(path);
            Assert.True(xml.IndexOf("src/A", StringComparison.Ordinal) < xml.IndexOf("src/Z", StringComparison.Ordinal));
        }
        finally { Directory.Delete(directory, true); }
    }
}
