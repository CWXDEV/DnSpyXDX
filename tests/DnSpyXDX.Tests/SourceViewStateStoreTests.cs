using DnSpyXDX.UI;
using Xunit;

namespace DnSpyXDX.Tests;

public sealed class SourceViewStateStoreTests
{
    private static readonly Guid Module = Guid.Parse("fd4da291-c9d7-4db5-8d0c-12fa28425ac8");

    [Fact]
    public void Keeps_separate_state_per_tab_and_document()
    {
        var store = new SourceViewStateStore();
        var document = new SourceDocumentKey(Module, 1, "csharp", "default");
        store.Set("one", document, new SourceViewState(100, 20));
        store.Set("two", document, new SourceViewState(300, 40));

        Assert.Equal(100, Get(store, "one", document).ScrollTop);
        Assert.Equal(300, Get(store, "two", document).ScrollTop);
    }

    [Fact]
    public void Closed_tab_cannot_be_reintroduced_by_late_component_disposal()
    {
        var store = new SourceViewStateStore();
        var document = new SourceDocumentKey(Module, 1, "csharp", "default");
        store.Set("tab", document, new SourceViewState(100, 20));
        store.RemoveTab("tab");
        store.Set("tab", document, new SourceViewState(300, 40));

        Assert.False(store.TryGet("tab", document, out _));
    }

    [Fact]
    public void Assembly_removal_clears_only_related_documents()
    {
        var store = new SourceViewStateStore();
        var removed = new SourceDocumentKey(Module, 1, "csharp", "default");
        var retained = new SourceDocumentKey(Guid.NewGuid(), 1, "csharp", "default");
        store.Set("tab", removed, new SourceViewState(100, 0));
        store.Set("tab", retained, new SourceViewState(200, 0));

        store.RemoveAssembly(Module);

        Assert.False(store.TryGet("tab", removed, out _));
        Assert.True(store.TryGet("tab", retained, out _));
    }

    private static SourceViewState Get(SourceViewStateStore store, string tab, SourceDocumentKey document)
    {
        Assert.True(store.TryGet(tab, document, out var state));
        return state;
    }
}
