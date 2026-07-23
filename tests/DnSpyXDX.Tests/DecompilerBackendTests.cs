using DnSpyXDX.Application;
using DnSpyXDX.Decompilation;
using Xunit;

namespace DnSpyXDX.Tests;

public sealed class DecompilerBackendTests
{
    [Fact]
    public async Task Opens_browses_and_decompiles_a_managed_assembly()
    {
        await using var backend = new DecompilerBackend();
        var assembly = await backend.OpenAsync(typeof(DecompilerBackendTests).Assembly.Location);
        Assert.Equal("DnSpyXDX.Tests", assembly.Name);

        var root = await backend.GetChildrenAsync(assembly.RootNode);
        var namespaces = Assert.Single(root, n => n.Name == "Namespaces");
        var namespaceNodes = await backend.GetChildrenAsync(namespaces.Id);
        var ownNamespace = Assert.Single(namespaceNodes, n => n.Name == "DnSpyXDX.Tests");
        var types = await backend.GetChildrenAsync(ownNamespace.Id);
        var testType = Assert.Single(types, n => n.Name == nameof(DecompilerBackendTests));
        Assert.False(testType.HasChildren);
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

    [Fact]
    public async Task Decompiled_documents_carry_links_for_types_in_the_same_assembly()
    {
        await using var backend = new DecompilerBackend();
        var assembly = await backend.OpenAsync(typeof(DecompilerBackendTests).Assembly.Location);
        var namespaces = await backend.GetChildrenAsync((await backend.GetChildrenAsync(assembly.RootNode)).Single(n => n.Name == "Namespaces").Id);
        var types = await backend.GetChildrenAsync(namespaces.Single(n => n.Name == "DnSpyXDX.Tests").Id);
        var testType = types.Single(n => n.Name == nameof(DecompilerBackendTests));

        var document = await backend.DecompileAsync(testType.Symbol!.Value);

        Assert.NotNull(document.SymbolLinks);
        Assert.Equal(testType.Symbol!.Value, document.SymbolLinks![nameof(DecompilerBackendTests)]);
        Assert.True(document.SymbolLinks.ContainsKey(nameof(CodeHighlighterTests)));
        // Members of the type on screen are linkable too, scoped to that type.
        Assert.True(document.SymbolLinks.ContainsKey(nameof(Opens_browses_and_decompiles_a_managed_assembly)));
    }

    [Fact]
    public async Task Orders_members_the_way_dnSpys_assembly_explorer_does()
    {
        await using var backend = new DecompilerBackend();
        var assembly = await backend.OpenAsync(typeof(DecompilerBackendTests).Assembly.Location);
        var namespaces = await backend.GetChildrenAsync((await backend.GetChildrenAsync(assembly.RootNode)).Single(n => n.Name == "Namespaces").Id);
        var types = await backend.GetChildrenAsync(namespaces.Single(n => n.Name == "DnSpyXDX.Tests").Id);

        var members = await backend.GetChildrenAsync(types.Single(n => n.Name == nameof(SampleMembers)).Id);

        // methods (constructors included) -> properties -> events -> fields -> nested types
        var groups = members.Select(m => Group(m.Kind)).ToArray();
        Assert.Equal(groups.OrderBy(g => g), groups);
        Assert.Equal([0, 1, 2, 3, 4], groups.Distinct());
        // The constructor sits in the method group rather than in one of its own.
        Assert.Contains(members.TakeWhile(m => m.Kind != TreeNodeKind.Property), m => m.Kind == TreeNodeKind.Constructor);
    }

    [Fact]
    public async Task Hides_property_and_event_accessors_from_the_method_list()
    {
        await using var backend = new DecompilerBackend();
        var assembly = await backend.OpenAsync(typeof(DecompilerBackendTests).Assembly.Location);
        var namespaces = await backend.GetChildrenAsync((await backend.GetChildrenAsync(assembly.RootNode)).Single(n => n.Name == "Namespaces").Id);
        var types = await backend.GetChildrenAsync(namespaces.Single(n => n.Name == "DnSpyXDX.Tests").Id);

        var members = await backend.GetChildrenAsync(types.Single(n => n.Name == nameof(SampleMembers)).Id);

        Assert.DoesNotContain(members, m => m.Name.StartsWith("get_", StringComparison.Ordinal));
        Assert.DoesNotContain(members, m => m.Name.StartsWith("set_", StringComparison.Ordinal));
        Assert.DoesNotContain(members, m => m.Name.StartsWith("add_", StringComparison.Ordinal));
        Assert.DoesNotContain(members, m => m.Name.StartsWith("remove_", StringComparison.Ordinal));
        Assert.Contains(members, m => m.Name == nameof(SampleMembers.SampleMethod));

        // They are reachable by expanding the property or event that owns them.
        var property = members.Single(m => m.Kind == TreeNodeKind.Property);
        Assert.True(property.HasChildren);
        var accessors = await backend.GetChildrenAsync(property.Id);
        Assert.Equal(["get_SampleProperty", "set_SampleProperty"], accessors.Select(a => a.Name));

        var @event = members.Single(m => m.Kind == TreeNodeKind.Event);
        Assert.True(@event.HasChildren);
        var eventAccessors = await backend.GetChildrenAsync(@event.Id);
        Assert.Equal(["add_SampleEvent", "remove_SampleEvent"], eventAccessors.Select(a => a.Name));
    }

    [Fact]
    public async Task Displays_generic_types_with_their_parameter_names()
    {
        await using var backend = new DecompilerBackend();
        var assembly = await backend.OpenAsync(typeof(DecompilerBackendTests).Assembly.Location);
        var namespaces = (await backend.GetChildrenAsync(assembly.RootNode)).Single(n => n.Name == "Namespaces");
        var ownNamespace = (await backend.GetChildrenAsync(namespaces.Id)).Single(n => n.Name == "DnSpyXDX.Tests");

        var genericType = (await backend.GetChildrenAsync(ownNamespace.Id)).Single(n => n.Name == "GenericSample<TItem>");
        var members = await backend.GetChildrenAsync(genericType.Id);
        var constructor = members.Single(n => n.Kind == TreeNodeKind.Constructor);
        var searchResult = Assert.Single(await backend.SearchAsync("GenericSample"), result => result.Kind == "Type");
        var fieldSearchResult = Assert.Single(await backend.SearchAsync(nameof(GenericSample<object>.Field)), result => result.Kind == "Field" && result.Name == nameof(GenericSample<object>.Field));
        var document = await backend.DecompileAsync(genericType.Symbol!.Value);

        Assert.Equal("GenericSample<TItem>", constructor.Name);
        Assert.Equal("TItem", members.Single(n => n.Name == nameof(GenericSample<object>.Item)).TypeDisplay);
        Assert.Equal("TItem", members.Single(n => n.Name == nameof(GenericSample<object>.Field)).TypeDisplay);
        Assert.Equal("Action<TItem>", members.Single(n => n.Kind == TreeNodeKind.Event && n.Name == nameof(GenericSample<object>.Changed)).TypeDisplay);
        Assert.Equal("TResult", members.Single(n => n.Name == nameof(GenericSample<object>.Convert)).TypeDisplay);
        Assert.Equal("GenericSample<TItem>", searchResult.Name);
        Assert.Equal(genericType.Symbol, searchResult.DeclaringType);
        Assert.Equal(genericType.Symbol, fieldSearchResult.DeclaringType);
        Assert.Equal(genericType.Symbol, await backend.GetDeclaringTypeAsync(fieldSearchResult.Symbol));
        Assert.Equal("GenericSample<TItem>", document.Title);
    }

    [Fact]
    public async Task Opens_a_referenced_assembly_from_its_tree_node()
    {
        await using var backend = new DecompilerBackend();
        var assembly = await backend.OpenAsync(typeof(DecompilerBackendTests).Assembly.Location);
        var references = (await backend.GetChildrenAsync(assembly.RootNode)).Single(n => n.Name == "References");
        var application = (await backend.GetChildrenAsync(references.Id)).Single(n => n.Name == "DnSpyXDX.Application");

        var opened = await backend.OpenReferenceAsync(application.Id);

        Assert.Equal("DnSpyXDX.Application", opened.Name);
        Assert.Contains(backend.Assemblies, candidate => candidate.SessionId == opened.SessionId);
        Assert.Equal(2, backend.Assemblies.Count);
        Assert.Equal(opened, await backend.OpenReferenceAsync(application.Id));
        Assert.Equal(2, backend.Assemblies.Count);
    }

    [Fact]
    public async Task Token_comments_are_attached_to_declarations_and_include_method_locations()
    {
        await using var backend = new DecompilerBackend();
        var assembly = await backend.OpenAsync(typeof(DecompilerBackendTests).Assembly.Location);
        var namespaces = (await backend.GetChildrenAsync(assembly.RootNode)).Single(n => n.Name == "Namespaces");
        var ownNamespace = (await backend.GetChildrenAsync(namespaces.Id)).Single(n => n.Name == "DnSpyXDX.Tests");
        var sampleType = (await backend.GetChildrenAsync(ownNamespace.Id)).Single(n => n.Name == nameof(SampleMembers));
        var members = await backend.GetChildrenAsync(sampleType.Id);
        var document = await backend.DecompileAsync(sampleType.Symbol!.Value);
        var lines = document.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        foreach (var name in new[] { nameof(SampleMembers.CallsLater), nameof(SampleMembers.Later) })
        {
            var method = members.Single(member => member.Name == name);
            var declaration = Array.FindIndex(lines, line => line.Contains($"void {name}(", StringComparison.Ordinal));
            Assert.True(declaration > 0, $"Could not find the declaration for {name}.");
            var token = method.Symbol!.Value.MetadataToken;
            Assert.StartsWith($"// Token: 0x{token:X8} RID: {token & 0x00FFFFFF} RVA: 0x", lines[declaration - 1].Trim(), StringComparison.Ordinal);
            Assert.Contains(" File Offset: 0x", lines[declaration - 1], StringComparison.Ordinal);
        }
    }

    private static int Group(TreeNodeKind kind) => kind switch
    {
        TreeNodeKind.Constructor or TreeNodeKind.Method => 0,
        TreeNodeKind.Property => 1,
        TreeNodeKind.Event => 2,
        TreeNodeKind.Field => 3,
        TreeNodeKind.Type => 4,
        _ => 5
    };

    private enum SampleEnum { One }
}

#pragma warning disable CS0067, CS0649 // this sample exists purely to be read back out of metadata
/// <summary>A top-level type carrying one of every member kind, so member ordering can be asserted
/// against something stable rather than whatever a test class happens to contain.</summary>
public sealed class SampleMembers
{
    public int SampleField;
    public int SampleProperty { get; set; }
    public event Action? SampleEvent;
    public void SampleMethod() { }
    public void CallsLater() => Later();
    public void Later() { }
    public sealed class SampleNested { }
}

public sealed class GenericSample<TItem>
{
    public TItem? Item { get; set; }
    public TItem? Field;
    public event Action<TItem>? Changed;
    public TResult? Convert<TResult>() => default;
}
#pragma warning restore CS0067, CS0649
