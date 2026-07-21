using DecompilerApp.Decompilation;
using Xunit;

namespace DecompilerApp.Tests;

public sealed class DecompilerBackendTests
{
    [Fact]
    public async Task Opens_browses_and_decompiles_a_managed_assembly()
    {
        await using var backend = new DecompilerBackend();
        var assembly = await backend.OpenAsync(typeof(DecompilerBackendTests).Assembly.Location);
        Assert.Equal("DecompilerApp.Tests", assembly.Name);

        var root = await backend.GetChildrenAsync(assembly.RootNode);
        var namespaces = Assert.Single(root, n => n.Name == "Namespaces");
        var namespaceNodes = await backend.GetChildrenAsync(namespaces.Id);
        var ownNamespace = Assert.Single(namespaceNodes, n => n.Name == "DecompilerApp.Tests");
        var types = await backend.GetChildrenAsync(ownNamespace.Id);
        var testType = Assert.Single(types, n => n.Name == nameof(DecompilerBackendTests));
        Assert.Equal("public", testType.Visibility);
        Assert.Equal("type", testType.TypeDisplay);
        var members = await backend.GetChildrenAsync(testType.Id);
        var method = Assert.Single(members, n => n.Name == nameof(Opens_browses_and_decompiles_a_managed_assembly));
        Assert.Equal("public", method.Visibility);
        Assert.NotNull(method.TypeDisplay);
        var path = await backend.GetPathAsync(method.Symbol!.Value);
        Assert.Equal("root", path[0].Value);
        Assert.Equal("namespaces", path[1].Value);
        Assert.Equal(method.Id, path[^1]);
        var sampleEnum = Assert.Single(members, n => n.Name == nameof(SampleEnum));
        Assert.Equal("enum", sampleEnum.NameClassification);
        var document = await backend.DecompileAsync(testType.Symbol!.Value);

        Assert.Contains("class DecompilerBackendTests", document.Text, StringComparison.Ordinal);
        Assert.Contains("// Token: 0x", document.Text, StringComparison.Ordinal);
        Assert.Contains("RID:", document.Text, StringComparison.Ordinal);
    }

    private enum SampleEnum { One }
}
