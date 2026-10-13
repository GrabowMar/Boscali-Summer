// DeadScan: finds code nothing can reach, by walking the compiler's own symbol graph from the plugin entry point
// through every project in the repo (tests and tools included) and the Unity harness sources.
//
//   dotnet run --project tools/DeadScan -c Release -- scan   <repo> <out.tsv> [extraDir...]
//   dotnet run --project tools/DeadScan -c Release -- remove <repo> <ids.txt>
//
// scan writes one row per unreachable declaration, plus <out.tsv>.files.tsv (files in which nothing is reachable).
// extraDir: folders outside the repo whose scripts and scenarios may call the mod by name (the nomodkit checkout).
// Buckets:
//   Y  unreachable, and nothing names it: safe to delete. Put the ids (last-but-one column) in a file for "remove".
//   F  unreachable from code, but a string, sim scenario, script or harness names it (sim seams live here).
//   S  only F code calls it (helpers of the sim seams).
//   T  only tests reach it: tested but not wired into the mod.
// IDE0051 cannot do this job here: it sees neither Harmony patch methods nor anything internal or public.
// After a remove: build (the compiler then reports fields that lost their last reader), run the tests, rescan.
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

MSBuildLocator.RegisterDefaults();
return await Scan.Run(args);

static class Scan
{
    static readonly HashSet<string> UnityMessages = new(StringComparer.Ordinal)
    {
        "Awake","Start","Update","LateUpdate","FixedUpdate","OnEnable","OnDisable","OnDestroy","OnGUI",
        "OnApplicationQuit","OnApplicationFocus","OnApplicationPause","OnRenderImage","OnPreRender","OnPostRender",
        "OnPreCull","OnWillRenderObject","OnRenderObject","OnTriggerEnter","OnTriggerExit","OnTriggerStay",
        "OnCollisionEnter","OnCollisionExit","OnCollisionStay","OnValidate","Reset","OnTransformParentChanged",
        "OnTransformChildrenChanged","OnRectTransformDimensionsChange","OnCanvasGroupChanged","OnBecameVisible",
        "OnBecameInvisible","OnParticleCollision","OnAudioFilterRead","OnDrawGizmos","OnDrawGizmosSelected",
        "OnMouseDown","OnMouseUp","OnMouseEnter","OnMouseExit","OnMouseOver","OnMouseDrag","OnAnimatorIK",
        "OnAnimatorMove","OnParticleSystemStopped","OnParticleTrigger","OnDidApplyAnimationProperties","Main",
    };
    static readonly HashSet<string> DuckNames = new(StringComparer.Ordinal)
    {
        "GetEnumerator","MoveNext","Current","Dispose","GetAwaiter","IsCompleted","GetResult","OnCompleted",
        "Deconstruct","Add","ToString","Equals","GetHashCode","CompareTo",
    };

    sealed class Decl
    {
        public string Id, Kind, Access, File; public int Start, End; public string Name;
        public string ContainerId, ContainerName; public bool IsType, InMain, AutoLive, UnityObject, Root;
        public List<string> CondTargets = new();   // in-source members this one overrides/implements
        public List<string> Bases = new();         // in-source base types and interfaces (types only)
        public HashSet<string> Edges = new();
        public List<string> Members = new();
    }

    static string Norm(string p) => Path.GetFullPath(p).Replace('\\', '/').ToLowerInvariant();
    static string Short(string id) { int p = id.IndexOf('('); if (p > 0) id = id.Substring(0, p); var a = id.Split('.'); return a.Length >= 2 ? a[^2] + "." + a[^1] : id; }

    public static async Task<int> Run(string[] args)
    {
        string mode = args[0], repo = Path.GetFullPath(args[1]);
        var props = new Dictionary<string, string> { ["Configuration"] = "Release" };
        using var ws = MSBuildWorkspace.Create(props);
        ws.WorkspaceFailed += (_, e) => { if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) Console.Error.WriteLine("WS: " + e.Diagnostic.Message); };

        var projFiles = Git(repo, "ls-files *.csproj").Where(p => !p.Contains("DeadScan", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var pf in projFiles)
        {
            try { await ws.OpenProjectAsync(Path.Combine(repo, pf)); Console.Error.WriteLine("loaded " + pf); }
            catch (Exception ex) { Console.Error.WriteLine("FAILED " + pf + ": " + ex.Message); }
        }
        // OpenProjectAsync can add referenced projects twice; work from the solution's distinct set.
        var all = ws.CurrentSolution.Projects.GroupBy(p => p.FilePath).Select(g => g.First()).ToList();
        string mainPath = Norm(Path.Combine(repo, "BoscaliSummer.csproj"));
        var mainProject = all.First(p => Norm(p.FilePath) == mainPath);
        var mainFiles = new HashSet<string>(mainProject.Documents.Select(d => Norm(d.FilePath)));

        if (mode == "remove") return await Remove(mainProject, File.ReadAllLines(args[2]).Where(l => l.Length > 0).ToHashSet());

        var decls = new Dictionary<string, Decl>();
        var compiled = new HashSet<string>();
        var fileDecls = new Dictionary<string, HashSet<string>>();   // main file -> ids declared there (partial types excluded)
        var roots = new HashSet<string>();          // ids referenced from root code (tests, tools, Unity harness)
        var rootsNoTests = new HashSet<string>();
        var tokenRx = new Regex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
        var identRx = new Regex(@"^[A-Za-z_][A-Za-z0-9_.+`]*(,\s*[A-Za-z0-9_.\-]+)?$", RegexOptions.Compiled);
        // per C# file: string literals that are wholly a name or dotted path (possible reflection keys), and every word in the file
        var litFiles = new List<(HashSet<string> lits, HashSet<string> words)>();
        var testLits = new HashSet<string>(StringComparer.Ordinal);  // test and harness code reflects on untyped objects: the name alone counts
        var pendingLits = new List<(string callee, List<string> tokens)>();   // test-side literals with the call they are passed to
        var helperFacts = new List<(string name, HashSet<string> callees)>();
        var unbound = new HashSet<string>(StringComparer.Ordinal);   // names the harness compilation could not bind

        async Task Walk(Compilation comp, bool harness)
        {
            foreach (var tree in comp.SyntaxTrees)
            {
                string file = Norm(tree.FilePath);
                if (!harness) compiled.Add(file);
                bool inMain = mainFiles.Contains(file);
                bool rootFile = !inMain;     // test / tool / harness code: everything it touches is live
                var model = comp.GetSemanticModel(tree);
                var root = await tree.GetRootAsync();
                var lits = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in root.DescendantNodes())
                {
                    if (node is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        string v = lit.Token.ValueText;
                        if (identRx.IsMatch(v))
                        {
                            var toks = tokenRx.Matches(v).Select(m => m.Value).ToList();
                            if (inMain) lits.UnionWith(toks); else pendingLits.Add((LiteralContext(lit), toks));
                        }
                        continue;
                    }
                    if (!inMain)
                    {
                        // which test-side methods forward a string into reflection (Field(obj, "name"), Call(obj, "Method"), ...)
                        var (hn, hp, hb) = node switch
                        {
                            MethodDeclarationSyntax m => (m.Identifier.ValueText, m.ParameterList, (SyntaxNode)m.Body ?? m.ExpressionBody),
                            LocalFunctionStatementSyntax l => (l.Identifier.ValueText, l.ParameterList, (SyntaxNode)l.Body ?? l.ExpressionBody),
                            _ => (null, null, null),
                        };
                        if (hb != null && hp.Parameters.Any(p => p.Type != null && p.Type.ToString().Contains("string", StringComparison.OrdinalIgnoreCase)))
                            helperFacts.Add((hn, hb.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(Callee).Where(c => c != null).ToHashSet(StringComparer.Ordinal)));
                    }
                    if (IsDeclNode(node))
                    {
                        if (harness) continue;
                        var ds = model.GetDeclaredSymbol(node);
                        if (ds != null) Declare(decls, ds, tree, node, inMain);
                        if (ds != null && inMain && ds is not INamedTypeSymbol { DeclaringSyntaxReferences.Length: > 1 })
                        {
                            var did = ds.OriginalDefinition.GetDocumentationCommentId();
                            if (did != null) { if (!fileDecls.TryGetValue(tree.FilePath, out var set)) fileDecls[tree.FilePath] = set = new(); set.Add(did); }
                        }
                        continue;
                    }
                    if (node is not SimpleNameSyntax name) continue;
                    var info = model.GetSymbolInfo(name);
                    var targets = info.Symbol != null ? new[] { info.Symbol } : info.CandidateSymbols.ToArray();
                    if (targets.Length == 0) { var mg = model.GetMemberGroup(name); if (!mg.IsDefaultOrEmpty) targets = mg.ToArray(); }
                    if (targets.Length == 0) { if (harness) unbound.Add(name.Identifier.ValueText); continue; }
                    var owners = rootFile ? null : Owners(model, name);
                    foreach (var t0 in targets)
                        foreach (var id in TargetIds(t0))
                        {
                            if (rootFile) { roots.Add(id); if (!file.Contains("/tests/")) rootsNoTests.Add(id); continue; }
                            if (owners.Count == 0) { roots.Add(id); rootsNoTests.Add(id); continue; }
                            foreach (var o in owners)
                            {
                                if (!decls.TryGetValue(o, out var od)) decls[o] = od = new Decl { Id = o };
                                od.Edges.Add(id);
                            }
                        }
                }
                if (lits.Count > 0) litFiles.Add((lits, tokenRx.Matches(root.ToFullString()).Select(m => m.Value).ToHashSet(StringComparer.Ordinal)));
            }
        }

        Compilation mainComp = null;
        foreach (var project in all)
        {
            var comp = await project.GetCompilationAsync();
            if (project == mainProject) mainComp = comp;
            var errors = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Console.Error.WriteLine($"{project.Name}: {comp.SyntaxTrees.Count()} trees, {errors.Count} errors");
            foreach (var d in errors.Take(3)) Console.Error.WriteLine("   " + d);
            await Walk(comp, false);
        }

        // Unity harness and editor scripts are compiled by Unity, not by any csproj. Bind them against the mod
        // (under an InternalsVisibleTo name) so their real references count; names that do not bind fall back to text.
        // The harness sources that the test csproj does list are wrapped in #if UNITY_EDITOR, so dotnet sees them empty:
        // every file with a conditional region is bound again here with the Unity (and public-release) symbols on.
        var condRx = new Regex(@"^\s*#(if|elif)\b.*(\bUNITY_|(?<!!)\bPUBLIC_RELEASE)", RegexOptions.Multiline);
        var loose = Git(repo, "ls-files *.cs").Select(f => Path.Combine(repo, f))
            .Where(f => File.Exists(f) && (!compiled.Contains(Norm(f)) || condRx.IsMatch(File.ReadAllText(f)))).ToList();
        var baseParse = (CSharpParseOptions)mainProject.ParseOptions;
        foreach (var extra in new[] { new[] { "UNITY_EDITOR", "UNITY_2022_3_OR_NEWER", "UNITY_5_3_OR_NEWER", "PUBLIC_RELEASE" }, new[] { "UNITY_EDITOR", "UNITY_2022_3_OR_NEWER", "UNITY_5_3_OR_NEWER" }, Array.Empty<string>() })
        {
            var parse = baseParse.WithPreprocessorSymbols(baseParse.PreprocessorSymbolNames.Concat(extra));
            var trees = loose.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, f));
            var harness = CSharpCompilation.Create("BoscaliSupportPreview", trees, mainComp.References.Append(mainComp.ToMetadataReference()),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            await Walk(harness, true);
        }
        Console.Error.WriteLine($"harness: {loose.Count} loose files, {unbound.Count} unbound names");

        // A test-side literal is a reflection key when it reaches a reflection API, directly or through a helper
        // that forwards its string argument there; literals outside any call (tables, constants) count too.
        var reflNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "GetField","GetProperty","GetMethod","GetMember","GetNestedType","GetEvent","GetType","InvokeMember","Field","Property","Method",
            "PropertyGetter","PropertySetter","DeclaredField","DeclaredMethod","DeclaredProperty","FieldRefAccess","StaticFieldRefAccess",
            "TypeByName","Inner","CreateInstance","GetRuntimeField","GetRuntimeProperty","GetRuntimeMethod","FindProperty",
        };
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var (name, callees) in helperFacts)
                if (!reflNames.Contains(name) && callees.Overlaps(reflNames)) { reflNames.Add(name); grew = true; }
        }
        reflNames.UnionWith(new[] { "Invoke", "InvokeRepeating", "CancelInvoke", "StartCoroutine", "StopCoroutine", "SendMessage", "BroadcastMessage", "SendMessageUpwards", "GetComponent", "AddComponent" });
        foreach (var (callee, toks) in pendingLits)
            if (callee == "" || callee != null && reflNames.Contains(callee)) testLits.UnionWith(toks);
        Console.Error.WriteLine($"reflection helpers: {string.Join(" ", reflNames.OrderBy(x => x))}");

        // Data files: scenarios call "Type.Method"; scripts may build such names, so a member needs its type named in the same file.
        var dataFiles = new List<HashSet<string>>();
        var jsonTokens = new HashSet<string>(StringComparer.Ordinal);   // scenario prose is noisy: bare tokens count for types only
        var pairs = new HashSet<string>(StringComparer.Ordinal);        // "Type.Member" as scenarios write a call
        var chainRx = new Regex(@"[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+", RegexOptions.Compiled);
        void Feed(string path, string ext)
        {
            string text = File.ReadAllText(path);
            if (ext != ".json") { dataFiles.Add(tokenRx.Matches(text).Select(m => m.Value).ToHashSet(StringComparer.Ordinal)); return; }
            foreach (Match m in tokenRx.Matches(text)) jsonTokens.Add(m.Value);
            foreach (Match m in chainRx.Matches(text))
            {
                var parts = m.Value.Split('.');
                for (int i = 0; i + 1 < parts.Length; i++) pairs.Add(parts[i] + "." + parts[i + 1]);
            }
        }
        foreach (var f in Git(repo, "ls-files"))
        {
            string full = Path.Combine(repo, f), ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is not (".json" or ".ps1" or ".py" or ".txt" or ".asmdef" or ".chatter" or ".avss" or ".props")) continue;
            if (!File.Exists(full) || new FileInfo(full).Length > 4_000_000) continue;
            Feed(full, ext);
        }
        foreach (var dir in args.Skip(3))
            foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is not (".cs" or ".py" or ".json" or ".ps1" or ".md")) continue;
                if (f.Contains("\\bin\\") || f.Contains("\\obj\\") || f.Contains("\\.venv\\") || f.Contains("\\node_modules\\") || f.Contains("\\.git\\")) continue;
                if (new FileInfo(f).Length > 2_000_000) continue;
                Feed(f, ext);
            }

        var live = Reach(decls, roots);
        var liveNoTests = Reach(decls, rootsNoTests);

        // Reflection can name a member through a subtype, so a member's "type names" include those.
        var subNames = new Dictionary<string, HashSet<string>>();
        foreach (var t in decls.Values.Where(t => t.IsType && t.Name != null))
            foreach (var b in t.Bases) { if (!subNames.TryGetValue(b, out var s)) subNames[b] = s = new(); s.Add(t.Name); }
        bool NamesType(Decl d, HashSet<string> words) =>
            d.IsType || d.ContainerName == null || words.Contains(d.ContainerName) || subNames.TryGetValue(d.ContainerId, out var subs) && subs.Overlaps(words);
        bool Str(Decl d) => testLits.Contains(d.Name) || litFiles.Any(f => f.lits.Contains(d.Name) && NamesType(d, f.words));
        bool Ext(Decl d) => unbound.Contains(d.Name) || dataFiles.Any(f => f.Contains(d.Name) && NamesType(d, f))
            || (d.IsType ? jsonTokens.Contains(d.Name) : d.ContainerName != null && pairs.Contains(d.ContainerName + "." + d.Name));

        // Anything a string, scenario or script may name is kept, with everything it calls.
        var flagsOf = new Dictionary<string, (bool str, bool ext)>();
        var keep = new HashSet<string>(roots);
        // Types first: a member can only be reached by name through a type that is itself compiled against or named.
        foreach (var d in decls.Values.Where(d => d.InMain && d.Name != null && !live.Contains(d.Id)).OrderBy(d => d.IsType ? 0 : 1))
        {
            var fl = (Str(d), Ext(d));
            flagsOf[d.Id] = fl;
            if (!fl.Item1 && !fl.Item2 && !d.UnityObject) continue;
            if (!d.IsType && d.ContainerId != null && !live.Contains(d.ContainerId) && !keep.Contains(d.ContainerId)) continue;
            keep.Add(d.Id);
            // a type nothing compiles against but something names is a reflection surface: all of it stays
            if (d.IsType) foreach (var m in d.Members) keep.Add(m);
        }
        // A type nothing compiles against is reported as one row, so it is kept or removed whole:
        // once any part of it survives, all of its members (and what they call) survive.
        HashSet<string> liveSafe;
        while (true)
        {
            liveSafe = Reach(decls, keep);
            int before = keep.Count;
            foreach (var t in decls.Values.Where(t => t.InMain && t.IsType && !live.Contains(t.Id) && liveSafe.Contains(t.Id)))
                keep.UnionWith(t.Members);
            if (keep.Count == before) break;
        }

        var referrers = new Dictionary<string, List<string>>();
        foreach (var d in decls.Values) foreach (var e in d.Edges) { if (!referrers.TryGetValue(e, out var l)) referrers[e] = l = new(); l.Add(d.Id); }

        var bucketOf = new Dictionary<string, string>();
        var sb = new StringBuilder("bucket\tflags\tkind\taccess\tlines\tfile\tstart\tend\tid\trefby\n");
        int deadLines = 0, n = 0;
        foreach (var d in decls.Values.Where(d => d.InMain && d.File != null).OrderBy(d => d.File).ThenBy(d => d.Start))
        {
            bool dead = !live.Contains(d.Id), testOnly = !dead && !liveNoTests.Contains(d.Id);
            if (!dead && !testOnly) continue;
            // a member of a type that is itself reported is covered by the type's row
            if (d.ContainerId != null && !(dead ? live : liveNoTests).Contains(d.ContainerId)) continue;
            var fl = flagsOf.TryGetValue(d.Id, out var f0) ? f0 : (false, false);
            var flags = new List<string> { dead ? "DEAD" : "TESTONLY" };
            if (fl.Item1) flags.Add("str");
            if (fl.Item2) flags.Add("ext");
            if (d.UnityObject) flags.Add("unity");
            if (d.CondTargets.Count > 0) flags.Add("impl");
            int lines = d.End - d.Start + 1;
            if (dead) { deadLines += lines; n++; }
            // Y: unreachable even from reflection-named code. F: named by a string/scenario/harness. S: only F code calls it. T: only tests.
            string bucket = !dead ? "T" : !liveSafe.Contains(d.Id) ? "Y" : fl.Item1 || fl.Item2 || d.UnityObject ? "F" : "S";
            bucketOf[d.Id] = bucket;
            var by = referrers.TryGetValue(d.Id, out var rl) ? string.Join(" ", rl.Where(r => r != d.Id).Take(3).Select(Short)) : "";
            sb.Append(bucket).Append('\t').Append(string.Join(",", flags)).Append('\t').Append(d.Kind).Append('\t').Append(d.Access).Append('\t').Append(lines).Append('\t')
              .Append(Path.GetRelativePath(repo, d.File).Replace('\\', '/')).Append('\t').Append(d.Start).Append('\t').Append(d.End).Append('\t').Append(d.Id).Append('\t').Append(by).Append('\n');
        }
        File.WriteAllText(args[2], sb.ToString());
        // files in which nothing at all is reachable from compiled code
        var fs = new StringBuilder("buckets\tdecls\tfile\n");
        foreach (var kv in fileDecls.OrderBy(k => k.Key))
        {
            if (kv.Value.Any(live.Contains)) continue;
            string BucketFor(string id)
            {
                for (var x = id; x != null; x = decls.TryGetValue(x, out var dd) ? dd.ContainerId : null)
                    if (bucketOf.TryGetValue(x, out var b)) return b;
                return "?";
            }
            fs.Append(string.Concat(kv.Value.Select(BucketFor).Distinct().OrderBy(b => b))).Append('\t').Append(kv.Value.Count).Append('\t')
              .Append(Path.GetRelativePath(repo, kv.Key).Replace('\\', '/')).Append('\n');
        }
        File.WriteAllText(args[2] + ".files.tsv", fs.ToString());
        Console.Error.WriteLine($"declared {decls.Values.Count(d => d.InMain)}, dead {n} ({deadLines} lines)");
        return 0;
    }

    static HashSet<string> Reach(Dictionary<string, Decl> decls, HashSet<string> roots)
    {
        var live = new HashSet<string>();
        var pending = new Dictionary<string, List<string>>();
        var queue = new Queue<string>();
        void Mark(string id) { if (id != null && live.Add(id)) queue.Enqueue(id); }
        foreach (var r in roots) Mark(r);
        foreach (var d in decls.Values) if (d.Root) Mark(d.Id);
        while (queue.Count > 0)
        {
            string id = queue.Dequeue();
            if (pending.TryGetValue(id, out var waiting)) foreach (var w in waiting) Mark(w);
            if (!decls.TryGetValue(id, out var d)) continue;
            foreach (var e in d.Edges) Mark(e);
            Mark(d.ContainerId);
            if (!d.IsType) continue;
            foreach (var mid in d.Members)
            {
                var m = decls[mid];
                if (m.AutoLive) { Mark(mid); continue; }
                foreach (var t in m.CondTargets)
                {
                    if (live.Contains(t)) Mark(mid);
                    else { if (!pending.TryGetValue(t, out var l)) pending[t] = l = new(); l.Add(mid); }
                }
            }
        }
        return live;
    }

    static string Callee(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        IdentifierNameSyntax i => i.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax b => b.Name.Identifier.ValueText,
        _ => null,
    };

    // The call a literal is an argument of; "" when it sits in no call (initializer, table, return); null when it cannot be a key.
    static string LiteralContext(SyntaxNode lit)
    {
        for (var p = lit.Parent; p != null; p = p.Parent)
        {
            if (p is InvocationExpressionSyntax inv) return Callee(inv);
            if (p is ElementAccessExpressionSyntax or ElementBindingExpressionSyntax or CaseSwitchLabelSyntax or ConstantPatternSyntax) return null;
            if (p is BinaryExpressionSyntax b && (b.IsKind(SyntaxKind.EqualsExpression) || b.IsKind(SyntaxKind.NotEqualsExpression))) return null;
            if (p is StatementSyntax or MemberDeclarationSyntax or LambdaExpressionSyntax) return "";
        }
        return "";
    }

    static bool IsDeclNode(SyntaxNode n) => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or MethodDeclarationSyntax
        or PropertyDeclarationSyntax or EventDeclarationSyntax or IndexerDeclarationSyntax or ConstructorDeclarationSyntax
        or DestructorDeclarationSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax
        or VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax } or EnumMemberDeclarationSyntax;

    // The member(s) whose declaration textually contains this reference.
    static List<string> Owners(SemanticModel model, SyntaxNode node)
    {
        var result = new List<string>();
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            if (p is VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax } vd) { Add(model.GetDeclaredSymbol(vd)); break; }
            if (p is BaseFieldDeclarationSyntax fd) { foreach (var v in fd.Declaration.Variables) Add(model.GetDeclaredSymbol(v)); break; }
            if (p is GlobalStatementSyntax) break;
            if (p is MemberDeclarationSyntax and not BaseNamespaceDeclarationSyntax) { Add(model.GetDeclaredSymbol(p)); break; }
        }
        return result;
        void Add(ISymbol s) { var id = s?.OriginalDefinition.GetDocumentationCommentId(); if (id != null) result.Add(id); }
    }

    static IEnumerable<string> TargetIds(ISymbol s)
    {
        if (s is IMethodSymbol m)
        {
            m = m.ReducedFrom ?? m;
            if (m.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction) yield break;
            s = m.AssociatedSymbol ?? m.PartialDefinitionPart ?? (ISymbol)m;
        }
        if (s is IAliasSymbol a) s = a.Target;
        if (s is ILocalSymbol or IParameterSymbol or INamespaceSymbol or ITypeParameterSymbol or ILabelSymbol or IRangeVariableSymbol) yield break;
        s = s.OriginalDefinition;
        // source symbols, or the mod's own symbols seen through a compilation reference (the harness)
        if (s.Locations.All(l => !l.IsInSource) && s.ContainingAssembly?.Name != "BoscaliSummer") yield break;
        var id = s.GetDocumentationCommentId();
        if (id != null) yield return id;
    }

    static void Declare(Dictionary<string, Decl> decls, ISymbol sym, SyntaxTree tree, SyntaxNode node, bool inMain)
    {
        string id = sym.OriginalDefinition.GetDocumentationCommentId();
        if (id == null) return;
        if (!decls.TryGetValue(id, out var d)) decls[id] = d = new Decl { Id = id };
        var span = (node is VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax f } && f.Declaration.Variables.Count == 1 ? f : node).GetLocation().GetLineSpan();
        if (d.File == null || inMain && !d.InMain) { d.File = tree.FilePath; d.Start = span.StartLinePosition.Line + 1; d.End = span.EndLinePosition.Line + 1; }
        d.InMain |= inMain;
        d.Name = sym.Name; d.Kind = sym is INamedTypeSymbol k ? k.TypeKind.ToString() : sym.Kind.ToString();
        d.Access = sym.DeclaredAccessibility.ToString();
        d.IsType = sym is INamedTypeSymbol;
        d.ContainerId = sym.ContainingType?.OriginalDefinition.GetDocumentationCommentId();
        d.ContainerName = sym.ContainingType?.Name;
        if (d.ContainerId != null)
        {
            if (!decls.TryGetValue(d.ContainerId, out var c)) decls[d.ContainerId] = c = new Decl { Id = d.ContainerId };
            if (!c.Members.Contains(id)) c.Members.Add(id);
        }
        if (sym is INamedTypeSymbol nt)
        {
            d.UnityObject = Inherits(nt, "UnityEngine.Object");
            if (Inherits(nt, "BepInEx.BaseUnityPlugin") || nt.GetAttributes().Any(a => a.AttributeClass?.Name == "BepInPlugin")) d.Root = true;
            for (var b = nt.BaseType; b != null; b = b.BaseType) AddBase(b);
            foreach (var i in nt.AllInterfaces) AddBase(i);
            return;
            void AddBase(INamedTypeSymbol b)
            {
                var bid = b.OriginalDefinition.GetDocumentationCommentId();
                if (bid != null && b.Locations.Any(l => l.IsInSource) && !d.Bases.Contains(bid)) d.Bases.Add(bid);
            }
        }
        var type = sym.ContainingType;
        if (type == null) return;
        bool harmonyType = type.GetAttributes().Any(IsHarmony) || type.GetMembers().Any(mm => mm.GetAttributes().Any(IsHarmony));
        bool serial = type.IsSerializable || type.GetAttributes().Any(a => a.AttributeClass?.Name == "StructLayoutAttribute");
        d.AutoLive |= sym.GetAttributes().Length > 0
            || type.TypeKind == TypeKind.Enum
            || sym is IMethodSymbol { MethodKind: not MethodKind.Ordinary and not MethodKind.ExplicitInterfaceImplementation }
            || sym is IPropertySymbol { IsIndexer: true }
            || harmonyType && sym is IMethodSymbol
            || serial && sym is IFieldSymbol { IsStatic: false }
            || DuckNames.Contains(sym.Name)
            || sym is IMethodSymbol && UnityMessages.Contains(sym.Name) && (Inherits(type, "UnityEngine.Object") || sym.Name == "Main");
        // overrides / interface implementations
        foreach (var target in Implemented(sym, type))
        {
            if (target.Locations.Any(l => l.IsInSource)) { var tid = target.OriginalDefinition.GetDocumentationCommentId(); if (tid != null && !d.CondTargets.Contains(tid)) d.CondTargets.Add(tid); }
            else d.AutoLive = true;
        }
    }

    static IEnumerable<ISymbol> Implemented(ISymbol sym, INamedTypeSymbol type)
    {
        if (sym is IMethodSymbol { OverriddenMethod: { } om }) yield return om;
        if (sym is IPropertySymbol { OverriddenProperty: { } op }) yield return op;
        if (sym is IEventSymbol { OverriddenEvent: { } oe }) yield return oe;
        if (sym is IMethodSymbol ms) foreach (var e in ms.ExplicitInterfaceImplementations) yield return e;
        if (sym is IPropertySymbol ps) foreach (var e in ps.ExplicitInterfaceImplementations) yield return e;
        if (sym is IEventSymbol es) foreach (var e in es.ExplicitInterfaceImplementations) yield return e;
        if (sym.IsStatic || sym is IFieldSymbol) yield break;
        foreach (var iface in type.AllInterfaces)
            foreach (var im in iface.GetMembers(sym.Name))
            {
                var impl = type.FindImplementationForInterfaceMember(im);
                if (impl != null && SymbolEqualityComparer.Default.Equals(impl.OriginalDefinition, sym.OriginalDefinition)) yield return im;
            }
    }

    static bool IsHarmony(AttributeData a) => a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("HarmonyLib") == true;

    static bool Inherits(INamedTypeSymbol t, string full)
    {
        for (var b = t.BaseType; b != null; b = b.BaseType) if (b.ToDisplayString() == full) return true;
        return false;
    }

    static List<string> Git(string repo, string argLine)
    {
        var psi = new ProcessStartInfo("git", $"-C \"{repo}\" {argLine}") { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi);
        var lines = p.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
        p.WaitForExit();
        return lines;
    }

    static async Task<int> Remove(Project main, HashSet<string> ids)
    {
        var comp = await main.GetCompilationAsync();
        int removed = 0, filesDeleted = 0, filesEdited = 0;
        foreach (var tree in comp.SyntaxTrees)
        {
            var model = comp.GetSemanticModel(tree);
            var root = await tree.GetRootAsync();
            var kill = new List<SyntaxNode>();
            foreach (var node in root.DescendantNodes().Where(IsDeclNode))
            {
                var id = model.GetDeclaredSymbol(node)?.OriginalDefinition.GetDocumentationCommentId();
                if (id == null || !ids.Contains(id)) continue;
                SyntaxNode target = node;
                if (node is VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax f })
                {
                    bool allDead = f.Declaration.Variables.All(v => ids.Contains(model.GetDeclaredSymbol(v)?.OriginalDefinition.GetDocumentationCommentId() ?? ""));
                    if (allDead) target = f;   // otherwise only this declarator leaves the shared declaration
                }
                if (kill.Any(k => k.Span.Contains(target.Span))) continue;
                kill.RemoveAll(k => target.Span.Contains(k.Span));
                kill.Add(target);
            }
            if (kill.Count == 0) continue;
            removed += kill.Count;
            var newRoot = root.RemoveNodes(kill, SyntaxRemoveOptions.KeepUnbalancedDirectives);
            bool anyDecl = newRoot.DescendantNodes().Any(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or GlobalStatementSyntax or AttributeListSyntax { Parent: CompilationUnitSyntax });
            if (!anyDecl) { File.Delete(tree.FilePath); filesDeleted++; Console.Error.WriteLine("deleted " + tree.FilePath); continue; }
            byte[] old = File.ReadAllBytes(tree.FilePath);
            bool bom = old.Length >= 3 && old[0] == 0xEF && old[1] == 0xBB && old[2] == 0xBF;
            File.WriteAllText(tree.FilePath, newRoot.ToFullString(), new UTF8Encoding(bom));
            filesEdited++;
        }
        Console.Error.WriteLine($"removed {removed} declarations, edited {filesEdited} files, deleted {filesDeleted} files");
        return 0;
    }
}
