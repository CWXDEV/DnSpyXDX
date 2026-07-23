namespace DnSpyXDX.UI;

public sealed record SourceViewState(double ScrollTop, double ScrollLeft, int? ActiveMatch = null);

public sealed class SourceViewStateStore
{
    private readonly Dictionary<(string TabId, SourceDocumentKey Document), SourceViewState> states = [];
    private readonly HashSet<string> closedTabs = [];

    public bool TryGet(string tabId, SourceDocumentKey document, out SourceViewState state) =>
        states.TryGetValue((tabId, document), out state!);

    public void Set(string tabId, SourceDocumentKey document, SourceViewState state)
    {
        if (!closedTabs.Contains(tabId)) states[(tabId, document)] = state;
    }

    public void RemoveTab(string tabId)
    {
        closedTabs.Add(tabId);
        foreach (var key in states.Keys.Where(key => key.TabId == tabId).ToArray()) states.Remove(key);
    }

    public void RemoveAssembly(Guid moduleMvid)
    {
        foreach (var key in states.Keys.Where(key => key.Document.ModuleMvid == moduleMvid).ToArray()) states.Remove(key);
    }

    public void Clear()
    {
        states.Clear();
        closedTabs.Clear();
    }
}
