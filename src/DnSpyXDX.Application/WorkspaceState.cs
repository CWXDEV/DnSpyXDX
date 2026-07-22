namespace DnSpyXDX.Application;

public sealed class WorkspaceState
{
    private readonly List<DocumentTab> tabs = [];
    public event Action? Changed;
    public IReadOnlyList<DocumentTab> Tabs => tabs;
    public string? ActiveTabId { get; private set; }
    public DocumentTab? ActiveTab => tabs.FirstOrDefault(t => t.Id == ActiveTabId);
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "Ready";

    public void SetBusy(bool busy, string status)
    {
        IsBusy = busy;
        Status = status;
        Changed?.Invoke();
    }

    /// <summary>
    /// Shows a document, following dnSpy: a plain navigation replaces the active tab's content and
    /// pushes the previous document onto that tab's history, while <paramref name="newTab"/> opens
    /// a separate tab instead.
    /// </summary>
    public void Open(DecompilerDocument document, string assemblyName, bool newTab = false)
    {
        var active = ActiveTab;
        if (newTab || active is null)
        {
            var tab = new DocumentTab(Guid.NewGuid().ToString("N"), document, assemblyName);
            tabs.Add(tab);
            ActiveTabId = tab.Id;
        }
        else active.NavigateTo(document, assemblyName);
        Status = $"Decompiled {document.Title}";
        Changed?.Invoke();
    }

    public string OpenLoading(SymbolId symbol, string title, string assemblyName)
    {
        var placeholder = new DecompilerDocument(symbol, title, "csharp", "", [], []);
        var tab = new DocumentTab(Guid.NewGuid().ToString("N"), placeholder, assemblyName, isLoading: true);
        tabs.Add(tab);
        ActiveTabId = tab.Id;
        Status = $"Decompiling {title}…";
        Changed?.Invoke();
        return tab.Id;
    }

    public bool CompleteLoading(string id, DecompilerDocument document)
    {
        var tab = tabs.FirstOrDefault(t => t.Id == id);
        if (tab is null || !tab.IsLoading) return false;
        tab.CompleteLoading(document);
        Status = $"Decompiled {document.Title}";
        Changed?.Invoke();
        return true;
    }

    public bool FailLoading(string id, string message)
    {
        var tab = tabs.FirstOrDefault(t => t.Id == id);
        if (tab is null || !tab.IsLoading) return false;
        tab.FailLoading(message);
        Status = message;
        Changed?.Invoke();
        return true;
    }

    public bool GoBack() => Navigate(tab => tab.GoBack());
    public bool GoForward() => Navigate(tab => tab.GoForward());

    private bool Navigate(Func<DocumentTab, bool> move)
    {
        if (ActiveTab is not { } tab || !move(tab)) return false;
        Status = $"Decompiled {tab.Document.Title}";
        Changed?.Invoke();
        return true;
    }

    public void Activate(string id) { ActiveTabId = id; Changed?.Invoke(); }

    public void Close(string id)
    {
        var index = tabs.FindIndex(t => t.Id == id);
        if (index < 0) return;
        tabs.RemoveAt(index);
        if (ActiveTabId == id) ActiveTabId = tabs.ElementAtOrDefault(index)?.Id ?? tabs.LastOrDefault()?.Id;
        Changed?.Invoke();
    }

    public void Clear()
    {
        tabs.Clear();
        ActiveTabId = null;
        Status = "Ready";
        Changed?.Invoke();
    }
}

/// <summary>A tab is a view whose content changes as you navigate, so it keeps its own back/forward
/// history rather than being identified by the symbol it happens to be showing.</summary>
public sealed class DocumentTab(string id, DecompilerDocument document, string assemblyName, bool isLoading = false)
{
    private readonly List<(DecompilerDocument Document, string AssemblyName)> back = [];
    private readonly List<(DecompilerDocument Document, string AssemblyName)> forward = [];

    public string Id { get; } = id;
    public DecompilerDocument Document { get; private set; } = document;
    public string AssemblyName { get; private set; } = assemblyName;
    public string Title => Document.Title;
    public bool IsLoading { get; private set; } = isLoading;
    public string? Error { get; private set; }
    public bool CanGoBack => back.Count > 0;
    public bool CanGoForward => forward.Count > 0;

    internal void NavigateTo(DecompilerDocument next, string nextAssemblyName)
    {
        if (next.Symbol == Document.Symbol) return;
        back.Add((Document, AssemblyName));
        forward.Clear();
        (Document, AssemblyName) = (next, nextAssemblyName);
    }

    internal void CompleteLoading(DecompilerDocument document)
    {
        Document = document;
        IsLoading = false;
        Error = null;
    }

    internal void FailLoading(string message)
    {
        IsLoading = false;
        Error = message;
    }

    internal bool GoBack() => Step(back, forward);
    internal bool GoForward() => Step(forward, back);

    private bool Step(List<(DecompilerDocument Document, string AssemblyName)> from, List<(DecompilerDocument Document, string AssemblyName)> to)
    {
        if (from.Count == 0) return false;
        to.Add((Document, AssemblyName));
        (Document, AssemblyName) = from[^1];
        from.RemoveAt(from.Count - 1);
        return true;
    }
}
