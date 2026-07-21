using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection;
using System.Collections.Immutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using DecompilerApp.Application;

namespace DecompilerApp.Decompilation;

public sealed class DecompilerBackend : IDecompilerBackend
{
    private readonly ConcurrentDictionary<Guid, AssemblySession> sessions = new();
    public IReadOnlyList<AssemblyDescriptor> Assemblies => sessions.Values.Select(s => s.Descriptor).OrderBy(s => s.Name).ToArray();

    public Task<AssemblyDescriptor> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("Assembly not found.", fullPath);
            var session = AssemblySession.Open(fullPath);
            if (!sessions.TryAdd(session.Descriptor.SessionId, session)) { session.Dispose(); throw new InvalidOperationException("Could not add assembly session."); }
            return session.Descriptor;
        }, cancellationToken);
    }

    public Task CloseAsync(Guid sessionId)
    {
        if (sessions.TryRemove(sessionId, out var session)) session.Dispose();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TreeNodeDescriptor>> GetChildrenAsync(NodeId parent, CancellationToken cancellationToken = default) =>
        sessions.TryGetValue(parent.SessionId, out var session)
            ? Task.Run(() => session.GetChildren(parent, cancellationToken), cancellationToken)
            : Task.FromResult<IReadOnlyList<TreeNodeDescriptor>>([]);

    public async Task<DecompilerDocument> DecompileAsync(SymbolId symbol, CancellationToken cancellationToken = default)
    {
        var session = sessions.Values.FirstOrDefault(s => s.Descriptor.ModuleMvid == symbol.ModuleMvid)
            ?? throw new KeyNotFoundException("The symbol's assembly is no longer open.");
        return await session.DecompileAsync(symbol, cancellationToken);
    }

    public Task<IReadOnlyList<NodeId>> GetPathAsync(SymbolId symbol, CancellationToken cancellationToken = default)
    {
        var session = sessions.Values.FirstOrDefault(s => s.Descriptor.ModuleMvid == symbol.ModuleMvid)
            ?? throw new KeyNotFoundException("The symbol's assembly is no longer open.");
        return Task.Run(() => session.GetPath(symbol, cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<SearchResult>>(() =>
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return sessions.Values.SelectMany(s => s.Search(query, cancellationToken)).Take(500).ToArray();
    }, cancellationToken);

    public bool TryGetAssembly(Guid sessionId, out AssemblyDescriptor? assembly)
    {
        if (sessions.TryGetValue(sessionId, out var session)) { assembly = session.Descriptor; return true; }
        assembly = null; return false;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in sessions.Values) session.Dispose();
        sessions.Clear();
        await Task.CompletedTask;
    }
}

internal sealed class AssemblySession : IDisposable
{
    private readonly PEFile module;
    private readonly MetadataReader metadata;
    private readonly CSharpDecompiler decompiler;
    private readonly MetadataTypeNameProvider typeNames;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<int, DecompilerDocument> cache = [];
    public AssemblyDescriptor Descriptor { get; }

    private AssemblySession(PEFile module, CSharpDecompiler decompiler, AssemblyDescriptor descriptor)
    {
        this.module = module;
        metadata = module.Metadata;
        this.decompiler = decompiler;
        typeNames = new MetadataTypeNameProvider(metadata);
        Descriptor = descriptor;
    }

    public static AssemblySession Open(string path)
    {
        PEFile module;
        try { module = new PEFile(path, PEStreamOptions.PrefetchEntireImage); }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException) { throw new BadImageFormatException("The selected file is not a valid managed PE assembly.", ex); }
        if (!module.IsAssembly) { module.Dispose(); throw new BadImageFormatException("The selected file does not contain a managed assembly manifest."); }

        var metadata = module.Metadata;
        var mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
        var name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
        var resolver = new UniversalAssemblyResolver(path, false, module.DetectTargetFrameworkId());
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
        var settings = new DecompilerSettings { ThrowOnAssemblyResolveErrors = false };
        var decompiler = new CSharpDecompiler(module, resolver, settings);
        var sessionId = Guid.NewGuid();
        var descriptor = new AssemblyDescriptor(sessionId, mvid, name, path, module.DetectTargetFrameworkId() ?? "Unknown", module.Reader.PEHeaders.CoffHeader.Machine.ToString(), new NodeId(sessionId, "root"));
        return new AssemblySession(module, decompiler, descriptor);
    }

    public IReadOnlyList<TreeNodeDescriptor> GetChildren(NodeId parent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (parent.Value == "root") return
        [
            new(new NodeId(Descriptor.SessionId, "references"), "References", TreeNodeKind.Group, metadata.AssemblyReferences.Count > 0, Detail: metadata.AssemblyReferences.Count.ToString()),
            new(new NodeId(Descriptor.SessionId, "resources"), "Resources", TreeNodeKind.Group, metadata.ManifestResources.Count > 0, Detail: metadata.ManifestResources.Count.ToString()),
            new(new NodeId(Descriptor.SessionId, "namespaces"), "Namespaces", TreeNodeKind.Group, true)
        ];
        if (parent.Value == "references") return metadata.AssemblyReferences.Select(h =>
        {
            var r = metadata.GetAssemblyReference(h);
            return new TreeNodeDescriptor(new NodeId(Descriptor.SessionId, $"ref:{MetadataTokens.GetToken(h)}"), metadata.GetString(r.Name), TreeNodeKind.Reference, false, Detail: r.Version.ToString());
        }).OrderBy(x => x.Name).ToArray();
        if (parent.Value == "resources") return metadata.ManifestResources.Select(h =>
        {
            var r = metadata.GetManifestResource(h);
            return new TreeNodeDescriptor(new NodeId(Descriptor.SessionId, $"res:{MetadataTokens.GetToken(h)}"), metadata.GetString(r.Name), TreeNodeKind.Resource, false);
        }).OrderBy(x => x.Name).ToArray();
        if (parent.Value == "namespaces") return metadata.TypeDefinitions.Select(h => metadata.GetString(metadata.GetTypeDefinition(h).Namespace)).Distinct().OrderBy(x => x).Select(ns => new TreeNodeDescriptor(new NodeId(Descriptor.SessionId, $"ns:{Uri.EscapeDataString(ns)}"), string.IsNullOrEmpty(ns) ? "<global>" : ns, TreeNodeKind.Namespace, true)).ToArray();
        if (parent.Value.StartsWith("ns:", StringComparison.Ordinal))
        {
            var ns = Uri.UnescapeDataString(parent.Value[3..]);
            return metadata.TypeDefinitions.Where(h =>
            {
                var t = metadata.GetTypeDefinition(h);
                return t.GetDeclaringType().IsNil && metadata.GetString(t.Namespace) == ns && metadata.GetString(t.Name) != "<Module>";
            }).Select(TypeNode).OrderBy(x => x.Name).ToArray();
        }
        if (parent.Value.StartsWith("type:", StringComparison.Ordinal)) return TypeChildren(MetadataTokens.TypeDefinitionHandle(ParseToken(parent)), ct);
        return [];
    }

    private TreeNodeDescriptor TypeNode(TypeDefinitionHandle h)
    {
        var t = metadata.GetTypeDefinition(h);
        var isEnum = IsEnum(t);
        return new(new NodeId(Descriptor.SessionId, $"type:{MetadataTokens.GetToken(h):X8}"), metadata.GetString(t.Name), TreeNodeKind.Type, true, new SymbolId(Descriptor.ModuleMvid, MetadataTokens.GetToken(h)), Visibility: TypeVisibility(t.Attributes), TypeDisplay: isEnum ? "enum" : TypeKeyword(t.Attributes), NameClassification: isEnum ? "enum" : "type", TypeClassification: "keyword");
    }

    private IReadOnlyList<TreeNodeDescriptor> TypeChildren(TypeDefinitionHandle handle, CancellationToken ct)
    {
        var t = metadata.GetTypeDefinition(handle);
        var nodes = new List<TreeNodeDescriptor>();
        foreach (var h in t.GetFields()) { ct.ThrowIfCancellationRequested(); var x = metadata.GetFieldDefinition(h); nodes.Add(MemberNode(h, metadata.GetString(x.Name), TreeNodeKind.Field, MemberVisibility(x.Attributes), x.DecodeSignature(typeNames, null))); }
        foreach (var h in t.GetProperties()) { var x = metadata.GetPropertyDefinition(h); var access = x.GetAccessors(); nodes.Add(MemberNode(h, metadata.GetString(x.Name), TreeNodeKind.Property, AccessorVisibility(access.Getter, access.Setter), x.DecodeSignature(typeNames, null).ReturnType)); }
        foreach (var h in t.GetEvents()) { var x = metadata.GetEventDefinition(h); var access = x.GetAccessors(); nodes.Add(MemberNode(h, metadata.GetString(x.Name), TreeNodeKind.Event, AccessorVisibility(access.Adder, access.Remover), typeNames.GetTypeName(x.Type))); }
        foreach (var h in t.GetMethods()) { var x = metadata.GetMethodDefinition(h); var name = metadata.GetString(x.Name); var kind = name is ".ctor" or ".cctor" ? TreeNodeKind.Constructor : TreeNodeKind.Method; nodes.Add(MemberNode(h, kind == TreeNodeKind.Constructor ? metadata.GetString(t.Name) : name, kind, MemberVisibility(x.Attributes), kind == TreeNodeKind.Constructor ? null : x.DecodeSignature(typeNames, null).ReturnType)); }
        nodes.AddRange(t.GetNestedTypes().Select(TypeNode));
        return nodes.OrderBy(n => n.Kind).ThenBy(n => n.Name).ToArray();
    }

    private TreeNodeDescriptor MemberNode(EntityHandle h, string name, TreeNodeKind kind, string visibility, string? typeDisplay)
    {
        var token = MetadataTokens.GetToken(h);
        return new(new NodeId(Descriptor.SessionId, $"member:{token:X8}"), name, kind, false, new SymbolId(Descriptor.ModuleMvid, token), Visibility: visibility, TypeDisplay: typeDisplay, NameClassification: kind.ToString().ToLowerInvariant(), TypeClassification: IsStandardType(typeDisplay) ? "standard" : "type");
    }

    private bool IsEnum(TypeDefinition definition)
    {
        var baseType = definition.BaseType;
        return baseType.Kind == HandleKind.TypeReference && metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)baseType).Name) == "Enum";
    }

    private static bool IsStandardType(string? type) => type is not null && type.TrimEnd('[', ']', '*', '?') is "bool" or "byte" or "char" or "decimal" or "double" or "float" or "int" or "long" or "object" or "sbyte" or "short" or "string" or "uint" or "ulong" or "ushort" or "void";

    private string AccessorVisibility(params MethodDefinitionHandle[] handles)
    {
        var values = handles.Where(h => !h.IsNil).Select(h => MemberVisibility(metadata.GetMethodDefinition(h).Attributes)).ToArray();
        return values.Contains("public") ? "public" : values.Contains("protected") ? "protected" : values.Contains("internal") ? "internal" : "private";
    }

    private static string MemberVisibility(MethodAttributes attributes) => (attributes & MethodAttributes.MemberAccessMask) switch
    {
        MethodAttributes.Public => "public", MethodAttributes.Family => "protected", MethodAttributes.Assembly => "internal",
        MethodAttributes.FamORAssem => "protected internal", MethodAttributes.FamANDAssem => "private protected", _ => "private"
    };
    private static string MemberVisibility(FieldAttributes attributes) => (attributes & FieldAttributes.FieldAccessMask) switch
    {
        FieldAttributes.Public => "public", FieldAttributes.Family => "protected", FieldAttributes.Assembly => "internal",
        FieldAttributes.FamORAssem => "protected internal", FieldAttributes.FamANDAssem => "private protected", _ => "private"
    };
    private static string TypeVisibility(TypeAttributes attributes) => (attributes & TypeAttributes.VisibilityMask) switch
    {
        TypeAttributes.Public or TypeAttributes.NestedPublic => "public", TypeAttributes.NestedFamily => "protected",
        TypeAttributes.NestedAssembly => "internal", TypeAttributes.NestedFamORAssem => "protected internal",
        TypeAttributes.NestedFamANDAssem => "private protected", TypeAttributes.NestedPrivate => "private", _ => "internal"
    };
    private static string TypeKeyword(TypeAttributes attributes) => (attributes & TypeAttributes.Interface) != 0 ? "interface" : "type";

    public async Task<DecompilerDocument> DecompileAsync(SymbolId symbol, CancellationToken ct)
    {
        if (cache.TryGetValue(symbol.MetadataToken, out var cached)) return cached;
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(symbol.MetadataToken, out cached)) return cached;
            var handle = MetadataTokens.EntityHandle(symbol.MetadataToken);
            var text = await Task.Run(() => AddTokenComments(decompiler.DecompileAsString([handle]), handle), ct);
            var title = GetEntityName(handle);
            var result = new DecompilerDocument(symbol, title, "csharp", text, [], []);
            cache[symbol.MetadataToken] = result;
            return result;
        }
        finally { gate.Release(); }
    }

    private string AddTokenComments(string source, EntityHandle selected)
    {
        if (selected.Kind != HandleKind.TypeDefinition) return TokenComment(selected) + Environment.NewLine + source;
        var type = metadata.GetTypeDefinition((TypeDefinitionHandle)selected);
        var declarations = new List<(EntityHandle Handle, string Name, bool Callable)>
        {
            (selected, metadata.GetString(type.Name), false)
        };
        declarations.AddRange(type.GetFields().Select(h => ((EntityHandle)h, metadata.GetString(metadata.GetFieldDefinition(h).Name), false)));
        declarations.AddRange(type.GetProperties().Select(h => ((EntityHandle)h, metadata.GetString(metadata.GetPropertyDefinition(h).Name), false)));
        declarations.AddRange(type.GetEvents().Select(h => ((EntityHandle)h, metadata.GetString(metadata.GetEventDefinition(h).Name), false)));
        declarations.AddRange(type.GetMethods().Where(h => !metadata.GetMethodDefinition(h).Attributes.HasFlag(MethodAttributes.SpecialName) || metadata.GetString(metadata.GetMethodDefinition(h).Name) is ".ctor" or ".cctor").Select(h =>
        {
            var name = metadata.GetString(metadata.GetMethodDefinition(h).Name);
            return ((EntityHandle)h, name is ".ctor" or ".cctor" ? metadata.GetString(type.Name) : name, true);
        }));

        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var insertions = new List<(int Index, string Comment)>();
        var used = new HashSet<int>();
        foreach (var declaration in declarations)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (used.Contains(i) || !ContainsIdentifier(lines[i], declaration.Name) || declaration.Callable && !lines[i].Contains('(')) continue;
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                used.Add(i);
                var indent = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)];
                insertions.Add((i, indent + TokenComment(declaration.Handle)));
                break;
            }
        }
        foreach (var insertion in insertions.OrderByDescending(x => x.Index)) lines.Insert(insertion.Index, insertion.Comment);
        return string.Join(Environment.NewLine, lines);
    }

    private static bool ContainsIdentifier(string line, string name)
    {
        var index = line.IndexOf(name, StringComparison.Ordinal);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(line[index - 1]) && line[index - 1] != '_';
            var end = index + name.Length;
            var after = end == line.Length || !char.IsLetterOrDigit(line[end]) && line[end] != '_';
            if (before && after) return true;
            index = line.IndexOf(name, end, StringComparison.Ordinal);
        }
        return false;
    }

    private static string TokenComment(EntityHandle handle)
    {
        var token = MetadataTokens.GetToken(handle);
        return $"// Token: 0x{token:X8} RID: {token & 0x00FFFFFF}";
    }

    public IEnumerable<SearchResult> Search(string query, CancellationToken ct)
    {
        foreach (var h in metadata.TypeDefinitions)
        {
            ct.ThrowIfCancellationRequested();
            var t = metadata.GetTypeDefinition(h); var typeName = metadata.GetString(t.Name); var ns = metadata.GetString(t.Namespace);
            if (typeName.Contains(query, StringComparison.OrdinalIgnoreCase)) yield return Result(h, typeName, "Type", ns);
            foreach (var m in t.GetMethods()) { var name = metadata.GetString(metadata.GetMethodDefinition(m).Name); if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) yield return Result(m, name, "Method", ns); }
            foreach (var f in t.GetFields()) { var name = metadata.GetString(metadata.GetFieldDefinition(f).Name); if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) yield return Result(f, name, "Field", ns); }
            foreach (var p in t.GetProperties()) { var name = metadata.GetString(metadata.GetPropertyDefinition(p).Name); if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) yield return Result(p, name, "Property", ns); }
            foreach (var e in t.GetEvents()) { var name = metadata.GetString(metadata.GetEventDefinition(e).Name); if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) yield return Result(e, name, "Event", ns); }
        }
    }

    public IReadOnlyList<NodeId> GetPath(SymbolId symbol, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var handle = MetadataTokens.EntityHandle(symbol.MetadataToken);
        var typeHandle = handle.Kind switch
        {
            HandleKind.TypeDefinition => (TypeDefinitionHandle)handle,
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType(),
            HandleKind.FieldDefinition => metadata.GetFieldDefinition((FieldDefinitionHandle)handle).GetDeclaringType(),
            HandleKind.PropertyDefinition => metadata.GetPropertyDefinition((PropertyDefinitionHandle)handle).GetAccessors() is var a && !a.Getter.IsNil ? metadata.GetMethodDefinition(a.Getter).GetDeclaringType() : FindPropertyDeclaringType((PropertyDefinitionHandle)handle),
            HandleKind.EventDefinition => metadata.GetEventDefinition((EventDefinitionHandle)handle).GetAccessors() is var e && !e.Adder.IsNil ? metadata.GetMethodDefinition(e.Adder).GetDeclaringType() : FindEventDeclaringType((EventDefinitionHandle)handle),
            _ => default
        };
        if (typeHandle.IsNil) return [Descriptor.RootNode];
        var chain = new Stack<TypeDefinitionHandle>();
        for (var current = typeHandle; !current.IsNil; current = metadata.GetTypeDefinition(current).GetDeclaringType()) chain.Push(current);
        var outer = chain.Peek();
        var ns = metadata.GetString(metadata.GetTypeDefinition(outer).Namespace);
        var path = new List<NodeId> { Descriptor.RootNode, new(Descriptor.SessionId, "namespaces"), new(Descriptor.SessionId, $"ns:{Uri.EscapeDataString(ns)}") };
        while (chain.Count > 0) { var type = chain.Pop(); path.Add(new NodeId(Descriptor.SessionId, $"type:{MetadataTokens.GetToken(type):X8}")); }
        if (handle.Kind != HandleKind.TypeDefinition) path.Add(new NodeId(Descriptor.SessionId, $"member:{symbol.MetadataToken:X8}"));
        return path;
    }

    private TypeDefinitionHandle FindPropertyDeclaringType(PropertyDefinitionHandle target) => metadata.TypeDefinitions.FirstOrDefault(t => metadata.GetTypeDefinition(t).GetProperties().Contains(target));
    private TypeDefinitionHandle FindEventDeclaringType(EventDefinitionHandle target) => metadata.TypeDefinitions.FirstOrDefault(t => metadata.GetTypeDefinition(t).GetEvents().Contains(target));

    private SearchResult Result(EntityHandle h, string name, string kind, string ns) => new(new SymbolId(Descriptor.ModuleMvid, MetadataTokens.GetToken(h)), name, kind, Descriptor.Name, ns);
    private string GetEntityName(EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)h).Name),
        HandleKind.MethodDefinition => metadata.GetString(metadata.GetMethodDefinition((MethodDefinitionHandle)h).Name),
        HandleKind.FieldDefinition => metadata.GetString(metadata.GetFieldDefinition((FieldDefinitionHandle)h).Name),
        HandleKind.PropertyDefinition => metadata.GetString(metadata.GetPropertyDefinition((PropertyDefinitionHandle)h).Name),
        HandleKind.EventDefinition => metadata.GetString(metadata.GetEventDefinition((EventDefinitionHandle)h).Name),
        _ => $"0x{MetadataTokens.GetToken(h):X8}"
    };
    private static int ParseToken(NodeId node) => Convert.ToInt32(node.Value[(node.Value.IndexOf(':') + 1)..], 16);
    public void Dispose() { gate.Dispose(); module.Dispose(); }
}

internal sealed class MetadataTypeNameProvider(MetadataReader metadata) : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => "ref " + elementType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType.Split('`')[0] + "<" + string.Join(", ", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch { PrimitiveTypeCode.Boolean => "bool", PrimitiveTypeCode.Byte => "byte", PrimitiveTypeCode.Char => "char", PrimitiveTypeCode.Double => "double", PrimitiveTypeCode.Int16 => "short", PrimitiveTypeCode.Int32 => "int", PrimitiveTypeCode.Int64 => "long", PrimitiveTypeCode.Object => "object", PrimitiveTypeCode.SByte => "sbyte", PrimitiveTypeCode.Single => "float", PrimitiveTypeCode.String => "string", PrimitiveTypeCode.UInt16 => "ushort", PrimitiveTypeCode.UInt32 => "uint", PrimitiveTypeCode.UInt64 => "ulong", PrimitiveTypeCode.Void => "void", _ => typeCode.ToString() };
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeReference(handle).Name);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    public string GetTypeName(EntityHandle handle) => handle.Kind switch { HandleKind.TypeDefinition => GetTypeFromDefinition(metadata, (TypeDefinitionHandle)handle, 0), HandleKind.TypeReference => GetTypeFromReference(metadata, (TypeReferenceHandle)handle, 0), HandleKind.TypeSpecification => GetTypeFromSpecification(metadata, null, (TypeSpecificationHandle)handle, 0), _ => "object" };
}
