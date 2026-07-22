namespace DnSpyXDX.Application;

public interface IDecompilerBackend : IAsyncDisposable
{
    IReadOnlyList<AssemblyDescriptor> Assemblies { get; }
    Task<AssemblyDescriptor> OpenAsync(string path, CancellationToken cancellationToken = default);
    Task<AssemblyDescriptor> OpenReferenceAsync(NodeId reference, CancellationToken cancellationToken = default);
    Task CloseAsync(Guid sessionId);
    Task<IReadOnlyList<TreeNodeDescriptor>> GetChildrenAsync(NodeId parent, CancellationToken cancellationToken = default);
    Task<DecompilerDocument> DecompileAsync(SymbolId symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NodeId>> GetPathAsync(SymbolId symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
    bool TryGetAssembly(Guid sessionId, out AssemblyDescriptor? assembly);
}

public interface IProjectExportService
{
    Task<ExportReport> ExportAsync(ExportRequest request, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default);
}

public interface IFileDialogService
{
    Task<string?> OpenAssemblyAsync();
    Task<string?> SelectExportFolderAsync();
}

public interface IWorkspaceSessionService
{
    UiSessionState UiState { get; set; }
    Task RestoreAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}
