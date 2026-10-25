using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

if (args.Contains("--self-test"))
{
    Atlas.SelfTest();
    return 0;
}

string Option(string name, string fallback)
{
    int at = Array.IndexOf(args, name);
    if (at < 0) return fallback;
    if (at + 1 >= args.Length || args[at + 1].StartsWith("--"))
        throw new ArgumentException($"{name} requires a path.");
    return args[at + 1];
}

try
{
    string repo = Path.GetFullPath(Option("--root", Directory.GetCurrentDirectory()));
    string output = Path.GetFullPath(Option("--out", Path.Combine(repo, "reports", "code-atlas")));
    string projectPath = Path.Combine(repo, "BoscaliSummer.csproj");
    if (!File.Exists(projectPath)) throw new FileNotFoundException("Pass --root with the Boscali Summer repository.", projectPath);
    MSBuildLocator.RegisterDefaults();
    using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string> { ["Configuration"] = "Release" });
    var workspaceWarnings = new System.Collections.Concurrent.ConcurrentQueue<string>();
    workspace.WorkspaceFailed += (_, e) => workspaceWarnings.Enqueue(e.Diagnostic.Message);
    Console.WriteLine("Loading the evaluated Release project (source analysis only)...");
    var project = await workspace.OpenProjectAsync(projectPath);
    var compilation = await project.GetCompilationAsync() ?? throw new InvalidOperationException("Roslyn returned no compilation.");
    var sourcePaths = project.Documents.Where(d => d.FilePath is not null)
        .Select(d => Path.GetFullPath(d.FilePath!)).Where(p => Atlas.IsSourcePath(repo, p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var data = Atlas.Extract(compilation, repo, sourcePaths);
    var diagnostics = compilation.GetDiagnostics();
    data.Meta = new
    {
        project = "Boscali Summer",
        generatedAt = DateTimeOffset.UtcNow.ToString("O"),
        gitRevision = Atlas.Git(repo, "rev-parse", "--short", "HEAD").Trim(),
        dirtyFiles = Atlas.Git(repo, "status", "--porcelain=v1", "--untracked-files=no").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length,
        sourceFiles = data.Files.Count,
        sourceLines = data.Files.Sum(f => f.Lines),
        typeCount = data.Modules.Sum(m => m.Types),
        analysis = "Roslyn semantic references in the evaluated Release BoscaliSummer.csproj",
        preprocessorSymbols = (project.ParseOptions as CSharpParseOptions)?.PreprocessorSymbolNames.Order(StringComparer.Ordinal).ToArray() ?? [],
        scope = new[] { "Core/**/*.cs", "modules/**/*.cs (project Editor exclusions respected)", "AvionicsUi/**/*.cs (project *Tests.cs exclusions respected)" },
        limitations = new[]
        {
            "Static source references, not a runtime call graph. Reflection strings, Harmony-discovered calls, Unity messages, assets and dynamic dispatch are not reconstructed.",
            "Release preprocessor configuration only; inactive conditional branches, generated obj sources, tests and tooling are excluded.",
            "Arrows mean source uses target. Edge weight counts unique (source file, referenced internal named type) pairs; repeated mentions in one file count once.",
            "Types, including partial types, are attributed once to their lexicographically first declaring source file. File lines count nonblank physical lines, including comments.",
            "Only successfully resolved symbols are counted. Compilation errors and unbound identifiers can leave dependencies incomplete; this is not build or runtime validation."
        },
        diagnostics = new { errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error), warnings = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning) },
        unresolvedIdentifiers = data.UnresolvedIdentifiers,
        workspaceWarnings = workspaceWarnings.ToArray()
    };
    Directory.CreateDirectory(output);
    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    await File.WriteAllTextAsync(Path.Combine(output, "atlas-data.json"), json, new UTF8Encoding(false));
    await File.WriteAllTextAsync(Path.Combine(output, "atlas-data.js"), "window.CODE_ATLAS = " + json + ";\n", new UTF8Encoding(false));
    Console.WriteLine($"Wrote {data.Files.Count:N0} files, {data.Modules.Count} components, {data.Modules.Sum(m => m.Types):N0} types, {data.Edges.Count:N0} component links, {data.FileEdges.Count:N0} file links.");
    Console.WriteLine($"Diagnostics: {diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} errors, {diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)} warnings; {data.UnresolvedIdentifiers:N0} unbound identifier occurrences.");
    Console.WriteLine(output);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static class Atlas
{
    public sealed record FileNode(string Id, string Name, string Module, int Lines, int TypeCount, string Namespace);
    public sealed record ModuleNode(string Id, string Name, string Path, int Files, int Lines, int Types, int AbstractTypes);
    public sealed record Evidence(string File, int Line, string Symbol, string TargetFile, int TargetLine);
    public sealed record ModuleEdge(string Source, string Target, int Weight, List<Evidence> Evidence);
    public sealed record FileEdge(string Source, string Target, int Weight);
    sealed record Declaration(string File, int Line, string Name, bool Abstract);

    public sealed class Data
    {
        public object? Meta { get; set; }
        public List<ModuleNode> Modules { get; set; } = [];
        public List<FileNode> Files { get; set; } = [];
        public List<ModuleEdge> Edges { get; set; } = [];
        public List<FileEdge> FileEdges { get; set; } = [];
        [System.Text.Json.Serialization.JsonIgnore]
        public int UnresolvedIdentifiers { get; set; }
    }

    static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    public static bool IsSourcePath(string root, string path)
    {
        string relative = Relative(root, path);
        return relative.StartsWith("Core/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("modules/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("AvionicsUi/", StringComparison.OrdinalIgnoreCase);
    }

    static string ModuleOf(string file)
    {
        var parts = file.Split('/');
        return parts[0] == "modules" && parts.Length > 1 ? parts[1] : parts[0];
    }

    public static Data Extract(Compilation compilation, string repo, HashSet<string> sourcePaths)
    {
        var data = new Data();
        var trees = compilation.SyntaxTrees.Where(t => sourcePaths.Contains(Path.GetFullPath(t.FilePath)))
            .OrderBy(t => Relative(repo, t.FilePath), StringComparer.Ordinal).ToList();
        var bySymbol = new Dictionary<INamedTypeSymbol, Declaration>(SymbolEqualityComparer.Default);
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes().Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                if (model.GetDeclaredSymbol(node) is not INamedTypeSymbol symbol) continue;
                symbol = symbol.OriginalDefinition;
                string file = Relative(repo, tree.FilePath);
                int line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
                if (!bySymbol.TryGetValue(symbol, out var existing) || StringComparer.Ordinal.Compare(file, existing.File) < 0)
                    bySymbol[symbol] = new Declaration(file, line, symbol.ToDisplayString(),
                        symbol.TypeKind == TypeKind.Interface || (symbol.IsAbstract && !symbol.IsStatic));
            }
        }

        var namespaceByFile = new Dictionary<string, string>();
        var dependencyByFile = new Dictionary<(string Source, string Target), HashSet<string>>();
        var evidenceByModule = new Dictionary<(string Source, string Target), List<Evidence>>();
        foreach (var tree in trees)
        {
            string file = Relative(repo, tree.FilePath);
            var model = compilation.GetSemanticModel(tree);
            var syntax = tree.GetRoot();
            var namespaceNames = syntax.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
                .Select(n => model.GetDeclaredSymbol(n)?.ToDisplayString()).Where(n => n is not null).Distinct().Order(StringComparer.Ordinal);
            namespaceByFile[file] = string.Join(", ", namespaceNames);
            var referenced = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var name in syntax.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var info = model.GetSymbolInfo(name);
                if (info.Symbol is null)
                {
                    if (name.Identifier.ValueText == "nameof" && name.Parent is InvocationExpressionSyntax) continue;
                    data.UnresolvedIdentifiers++;
                    continue; // Candidate symbols are ambiguous: do not turn guesses into architecture links.
                }
                foreach (var target in ReferencedTypes(info.Symbol))
                {
                    var original = target.OriginalDefinition;
                    if (!bySymbol.TryGetValue(original, out var declaration) || !referenced.Add(original) || declaration.File == file) continue;
                    var pair = (Source: file, Target: declaration.File);
                    if (!dependencyByFile.TryGetValue(pair, out var types)) dependencyByFile[pair] = types = new(StringComparer.Ordinal);
                    types.Add(declaration.Name);
                    var modulePair = (Source: ModuleOf(file), Target: ModuleOf(declaration.File));
                    if (modulePair.Source == modulePair.Target) continue;
                    if (!evidenceByModule.TryGetValue(modulePair, out var evidence)) evidenceByModule[modulePair] = evidence = [];
                    if (evidence.Count < 8)
                        evidence.Add(new Evidence(file, tree.GetLineSpan(name.Span).StartLinePosition.Line + 1, declaration.Name, declaration.File, declaration.Line));
                }
            }
            data.Files.Add(new FileNode(file, Path.GetFileName(file), ModuleOf(file),
                tree.GetText().Lines.Count(l => !string.IsNullOrWhiteSpace(l.ToString())),
                bySymbol.Values.Count(d => d.File == file), namespaceByFile[file]));
        }

        data.FileEdges = dependencyByFile.OrderBy(p => p.Key.Source, StringComparer.Ordinal).ThenBy(p => p.Key.Target, StringComparer.Ordinal)
            .Select(p => new FileEdge(p.Key.Source, p.Key.Target, p.Value.Count)).ToList();
        data.Modules = data.Files.GroupBy(f => f.Module).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ModuleNode(g.Key, g.Key, g.Key is "Core" or "AvionicsUi" ? g.Key : "modules/" + g.Key,
                g.Count(), g.Sum(f => f.Lines), g.Sum(f => f.TypeCount), bySymbol.Values.Count(d => ModuleOf(d.File) == g.Key && d.Abstract)))
            .ToList();
        data.Edges = data.FileEdges.GroupBy(e => (Source: ModuleOf(e.Source), Target: ModuleOf(e.Target)))
            .Where(g => g.Key.Source != g.Key.Target).OrderBy(g => g.Key.Source, StringComparer.Ordinal).ThenBy(g => g.Key.Target, StringComparer.Ordinal)
            .Select(g => new ModuleEdge(g.Key.Source, g.Key.Target, g.Sum(e => e.Weight), evidenceByModule[g.Key])).ToList();
        return data;
    }

    static IEnumerable<INamedTypeSymbol> ReferencedTypes(ISymbol symbol)
    {
        ITypeSymbol? type = symbol switch
        {
            INamedTypeSymbol named => named,
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            IEventSymbol @event => @event.Type,
            IMethodSymbol method => method.ReturnType,
            IAliasSymbol alias => alias.Target as ITypeSymbol,
            _ => null
        };
        if (type is not null)
            foreach (var named in NamedTypes(type)) yield return named;
        if (symbol.ContainingType is not null)
            foreach (var named in NamedTypes(symbol.ContainingType)) yield return named;
        if (symbol is IMethodSymbol genericMethod)
            foreach (var argument in genericMethod.TypeArguments)
                foreach (var named in NamedTypes(argument)) yield return named;
    }

    static IEnumerable<INamedTypeSymbol> NamedTypes(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            foreach (var named in NamedTypes(array.ElementType)) yield return named;
        }
        else if (type is IPointerTypeSymbol pointer)
        {
            foreach (var named in NamedTypes(pointer.PointedAtType)) yield return named;
        }
        else if (type is INamedTypeSymbol named)
        {
            if (named.TypeKind != TypeKind.Error) yield return named;
            foreach (var argument in named.TypeArguments)
                foreach (var argumentType in NamedTypes(argument)) yield return argumentType;
        }
    }

    public static string Git(string repo, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git failed: " + error.Trim());
        return output;
    }

    public static void SelfTest()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "boscali-code-atlas-fixture"));
        var sources = new Dictionary<string, string>
        {
            ["Core/Shared.cs"] = "namespace Fixture; public interface IShared {} public partial class Shared : IShared { public static int Value; } public static class StaticUtility {}",
            ["Core/Shared.Partial.cs"] = "namespace Fixture; public partial class Shared { public static void Work() {} }",
            ["modules/Alpha/Use.cs"] = "using Fixture; namespace Alpha; class Use { Shared a = new Shared(); IShared b = new Shared(); string reflectionName = \"Beta.Alone\"; string typeName = nameof(Shared); void Run() { Shared.Work(); Shared.Work(); var x = Shared.Value; } }",
            ["modules/Beta/Alone.cs"] = "namespace Beta; class Alone {}",
            ["modules/Models/Payload.cs"] = "namespace Inferred; public class Payload { public Child Child => null; } public class Child { public string Text => null; }",
            ["modules/Factory/Api.cs"] = "namespace Inferred; public static class Api { public static Payload Create() => null; public static Payload Current => null; public static int Count<T>(T value) => 1; }",
            ["modules/Inference/Calls.cs"] = "using Inferred; namespace Consumer; class Calls { void Run() { Api.Create(); var length = Api.Current.Child.Text.Length; Api.Count(Api.Create()); } }",
            ["tests/Excluded.cs"] = "namespace Tests; class TestOnly {}"
        };
        var trees = sources.Select(p => CSharpSyntaxTree.ParseText(p.Value, path: Path.Combine(root, p.Key))).ToArray();
        var compilation = CSharpCompilation.Create("AtlasFixture", trees, [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var paths = trees.Select(t => Path.GetFullPath(t.FilePath)).Where(p => IsSourcePath(root, p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var data = Extract(compilation, root, paths);
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("Self-test failed: " + message); }
        Check(!compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), "fixture must bind successfully");
        Check(data.Files.Count == 7 && data.Modules.Count == 6, "include isolated files, exclude tests");
        Check(data.Modules.Sum(m => m.Types) == 9, "count partial types once");
        Check(data.Modules.Single(m => m.Id == "Core").AbstractTypes == 1, "count interfaces as abstract, not static classes");
        var edge = data.Edges.Single(e => e.Source == "Alpha");
        Check(edge.Source == "Alpha" && edge.Target == "Core" && edge.Weight == 2, "reference direction and per-type deduplication");
        Check(edge.Evidence.All(e => e.File == "modules/Alpha/Use.cs" && e.Line == 1 && (e.TargetFile == "Core/Shared.Partial.cs" || e.TargetFile == "Core/Shared.cs")), "evidence source and target locations");
        Check(data.Files.Sum(f => f.Lines) == 7, "nonblank physical lines");
        Check(data.Edges.Single(e => e.Source == "Inference" && e.Target == "Models").Weight == 2,
            "inferred return and property chain types without explicit type names");
        var inferenceTree = trees.Single(t => t.FilePath.EndsWith("Calls.cs", StringComparison.Ordinal));
        var countName = inferenceTree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>().Single(n => n.Identifier.ValueText == "Count");
        var countSymbol = compilation.GetSemanticModel(inferenceTree).GetSymbolInfo(countName).Symbol!;
        Check(ReferencedTypes(countSymbol).Any(t => t.Name == "Payload"), "inferred generic method type arguments");
        Console.WriteLine("CodeAtlas self-test passed: semantic member references, repeated mentions, partial types, interface metrics, isolated components, source exclusions and evidence.");
    }
}
