using System.Text.Json;
using DnSpyXDX.Application;

namespace DnSpyXDX.Host;

public sealed class WorkspaceSessionService(IDecompilerBackend backend, WorkspaceState workspace) : IWorkspaceSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    public UiSessionState UiState { get; set; } = new();
    private string SessionPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DnSpyXDX", "session.json");

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SessionPath)) return;
        SessionSnapshot? snapshot;
        try
        {
            await using var stream = File.OpenRead(SessionPath);
            snapshot = await JsonSerializer.DeserializeAsync<SessionSnapshot>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return; }
        if (snapshot is null) return;
        UiState = snapshot.UiState ?? new UiSessionState();

        foreach (var path in snapshot.AssemblyPaths.Distinct(PathComparer()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path)) continue;
            try { await backend.OpenAsync(path, cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        foreach (var saved in snapshot.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!backend.Assemblies.Any(a => a.ModuleMvid == saved.Symbol.ModuleMvid)) continue;
            try
            {
                var document = await backend.DecompileAsync(saved.Symbol, cancellationToken);
                var assembly = backend.Assemblies.First(a => a.ModuleMvid == saved.Symbol.ModuleMvid);
                workspace.Open(document, assembly.Name, newTab: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        // Tab ids are generated per run, so the active tab is restored by position.
        if (workspace.Tabs.ElementAtOrDefault(snapshot.ActiveIndex) is { } active) workspace.Activate(active.Id);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = new SessionSnapshot(
                backend.Assemblies.Select(a => a.Path).ToArray(),
                workspace.Tabs.Select(t => new SavedDocument(t.Document.Symbol)).ToArray(),
                workspace.Tabs.ToList().FindIndex(t => t.Id == workspace.ActiveTabId),
                UiState);
            var directory = Path.GetDirectoryName(SessionPath)!;
            Directory.CreateDirectory(directory);
            var temporary = SessionPath + ".tmp";
            await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
            File.Move(temporary, SessionPath, true);
        }
        finally { gate.Release(); }
    }

    private static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private sealed record SessionSnapshot(string[] AssemblyPaths, SavedDocument[] Documents, int ActiveIndex, UiSessionState? UiState = null);
    private sealed record SavedDocument(SymbolId Symbol);
}
