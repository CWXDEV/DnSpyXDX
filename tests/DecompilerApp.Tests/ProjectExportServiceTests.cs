using DecompilerApp.Application;
using DecompilerApp.Export;
using Xunit;

namespace DecompilerApp.Tests;

public sealed class ProjectExportServiceTests
{
    [Fact]
    public async Task Exports_sdk_project_solution_and_report()
    {
        var root = Path.Combine(Path.GetTempPath(), $"baby-dnspy-export-{Guid.NewGuid():N}");
        var destination = Path.Combine(root, "output");
        Directory.CreateDirectory(root);
        try
        {
            var id = Guid.NewGuid();
            var descriptor = new AssemblyDescriptor(id, Guid.NewGuid(), "DecompilerApp.Tests", typeof(ProjectExportServiceTests).Assembly.Location, ".NETCoreApp,Version=v10.0", "Amd64", new NodeId(id, "root"));
            await using var backend = new ExportBackendStub(descriptor);
            var exporter = new ProjectExportService(backend);
            var report = await exporter.ExportAsync(new ExportRequest([id], destination));

            Assert.True(report.Success);
            Assert.True(File.Exists(Path.Combine(destination, "DnSpyXDXExport.slnx")));
            Assert.True(File.Exists(Path.Combine(destination, "export-report.json")));
            Assert.NotEmpty(Directory.EnumerateFiles(destination, "*.csproj", SearchOption.AllDirectories));
            Assert.NotEmpty(Directory.EnumerateFiles(destination, "*.cs", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class ExportBackendStub(AssemblyDescriptor descriptor) : IDecompilerBackend
    {
        public IReadOnlyList<AssemblyDescriptor> Assemblies => [descriptor];
        public bool TryGetAssembly(Guid sessionId, out AssemblyDescriptor? assembly) { assembly = sessionId == descriptor.SessionId ? descriptor : null; return assembly is not null; }
        public Task<AssemblyDescriptor> OpenAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CloseAsync(Guid sessionId) => Task.CompletedTask;
        public Task<IReadOnlyList<TreeNodeDescriptor>> GetChildrenAsync(NodeId parent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DecompilerDocument> DecompileAsync(SymbolId symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<NodeId>> GetPathAsync(SymbolId symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
