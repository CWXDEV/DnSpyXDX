namespace DecompilerApp.Application;

public sealed class WorkspaceState
{
    private readonly List<DocumentTab> tabs = [];
    public event Action? Changed;
    public IReadOnlyList<DocumentTab> Tabs => tabs;
    public string? ActiveKey { get; private set; }
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "Ready";

    public void SetBusy(bool busy, string status)
    {
        IsBusy = busy;
        Status = status;
        Changed?.Invoke();
    }

    public void Open(DecompilerDocument document, string assemblyName)
    {
        var key = $"{document.Symbol.ModuleMvid:N}:{document.Symbol.MetadataToken:X8}";
        var existing = tabs.FindIndex(t => t.Key == key);
        if (existing >= 0) tabs[existing] = tabs[existing] with { Document = document };
        else tabs.Add(new DocumentTab(key, document.Title, assemblyName, document));
        ActiveKey = key;
        Status = $"Decompiled {document.Title}";
        Changed?.Invoke();
    }

    public void Activate(string key) { ActiveKey = key; Changed?.Invoke(); }
    public void Close(string key)
    {
        var index = tabs.FindIndex(t => t.Key == key);
        if (index < 0) return;
        tabs.RemoveAt(index);
        if (ActiveKey == key) ActiveKey = tabs.LastOrDefault()?.Key;
        Changed?.Invoke();
    }

    public void Clear()
    {
        tabs.Clear();
        ActiveKey = null;
        Status = "Ready";
        Changed?.Invoke();
    }
}

public sealed record DocumentTab(string Key, string Title, string AssemblyName, DecompilerDocument Document);
