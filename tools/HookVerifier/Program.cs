using System.Reflection;
using RecipePlanner.Game.Binding;

// Verifies the hook table against a real Schedule I build, offline.
//
// Assemblies are loaded through MetadataLoadContext, so nothing from the game is ever executed —
// this only reads type metadata. Point it at either:
//   * Cpp2IL stub output from the IL2CPP branch, or
//   * Schedule I_Data/Managed from the Mono ("alternate") branch.
//
// Usage: HookVerifier <assembly-directory> [--list <type-regex>]

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: HookVerifier <assembly-directory> [--list <type-regex>]");
    return 2;
}

const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

var dir = args[0];
if (!Directory.Exists(dir))
{
    Console.Error.WriteLine($"Not a directory: {dir}");
    return 2;
}

var dlls = Directory.GetFiles(dir, "*.dll");
if (dlls.Length == 0)
{
    Console.Error.WriteLine($"No assemblies found in {dir}");
    return 2;
}

// MelonLoader's Il2CppAssemblies folder renames the core assembly to Il2Cppmscorlib, so the
// default "mscorlib" lookup fails. Include the running runtime's assemblies as a fallback and try
// each plausible core assembly name in turn.
var searchPaths = new List<string>(dlls);
var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
if (!string.IsNullOrEmpty(runtimeDir) && Directory.Exists(runtimeDir))
    searchPaths.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));

// Il2Cpp proxies reference Il2CppInterop.Runtime, which lives one level up in MelonLoader\net6.
// Pull in sibling folders so base-type resolution succeeds when pointed at Il2CppAssemblies.
var parent = Directory.GetParent(dir)?.FullName;
foreach (var sibling in new[] { "net6", "net472", "net35", "Managed" })
{
    var candidate = parent is null ? null : Path.Combine(parent, sibling);
    if (candidate is not null && Directory.Exists(candidate))
        searchPaths.AddRange(Directory.GetFiles(candidate, "*.dll"));
}

// PathAssemblyResolver rejects duplicate simple names; keep the first of each.
searchPaths = searchPaths
    .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
    .Select(g => g.First())
    .ToList();

MetadataLoadContext mlc = null;
foreach (var core in new[] { "mscorlib", "Il2Cppmscorlib", "System.Private.CoreLib", "netstandard" })
{
    try
    {
        mlc = new MetadataLoadContext(new PathAssemblyResolver(searchPaths), core);
        break;
    }
    catch (FileNotFoundException) { /* try the next candidate */ }
}

if (mlc is null)
{
    Console.Error.WriteLine("Could not establish a core assembly for metadata loading.");
    return 2;
}

using (mlc)
{

var loaded = new List<Assembly>();
foreach (var path in dlls)
{
    try { loaded.Add(mlc.LoadFromAssemblyPath(path)); }
    catch (Exception ex) { Console.Error.WriteLine($"  (skipped {Path.GetFileName(path)}: {ex.GetType().Name})"); }
}

Console.WriteLine($"Loaded {loaded.Count} assemblies from {dir}");

var game = SymbolGuard.GameAssemblies(loaded).ToList();
Console.WriteLine($"Game assemblies: {string.Join(", ", game.Select(a => a.GetName().Name))}");
Console.WriteLine();

// --list dumps a type's real members, for updating the hook table after a game update.
var listIndex = Array.IndexOf(args, "--list");
if (listIndex >= 0 && listIndex + 1 < args.Length)
{
    var pattern = new System.Text.RegularExpressions.Regex(args[listIndex + 1]);
    foreach (var asm in game)
    foreach (var type in SafeTypes(asm).Where(t => pattern.IsMatch(t.FullName ?? "")))
    {
        Console.WriteLine($"=== {type.FullName} ===");
        if (type.BaseType is not null) Console.WriteLine($"  base: {type.BaseType.FullName}");

        foreach (var f in type.GetFields(All))
            Console.WriteLine($"  field  {Short(f.FieldType)} {f.Name}");

        foreach (var p in type.GetProperties(All))
            Console.WriteLine($"  prop   {Short(p.PropertyType)} {p.Name}");

        // Parameter NAMES survive into the metadata, which is often the only way to settle what an
        // ambiguously-named field actually means.
        // Modifiers matter when subclassing: you cannot override what is not virtual.
        foreach (var m in type.GetMethods(All).Where(m => !m.IsSpecialName))
            Console.WriteLine($"  method {Modifiers(m)}{Short(m.ReturnType)} {m.Name}(" +
                string.Join(", ", m.GetParameters().Select(p => $"{Short(p.ParameterType)} {p.Name}")) + ")");

        Console.WriteLine();
    }
    return 0;
}

var report = SymbolGuard.Verify(game);
Console.WriteLine(report.Describe());
Console.WriteLine();

foreach (var finding in report.Findings.Where(f => f.Status == SymbolStatus.Ok))
    Console.WriteLine($"  [ok]       {finding.TypeName}");

Console.WriteLine();
Console.WriteLine(report.SafeToTrack
    ? "RESULT: hook table matches this build — safe to track."
    : "RESULT: hook table does NOT match this build — tracking would be disabled at runtime.");

return report.SafeToTrack ? 0 : 1;
} // end using (mlc)

static IEnumerable<Type> SafeTypes(Assembly asm)
{
    try { return asm.GetTypes(); }
    catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
}

static string Modifiers(System.Reflection.MethodInfo m)
{
    var parts = new List<string>();
    if (m.IsStatic) parts.Add("static");
    if (m.IsAbstract) parts.Add("abstract");
    else if (m.IsVirtual)
    {
        // GetBaseDefinition is not always supported in a metadata-only context.
        bool isOverride;
        try { isOverride = m.GetBaseDefinition() != m; } catch { isOverride = false; }
        parts.Add(isOverride ? "override" : "virtual");
    }
    return parts.Count == 0 ? "" : string.Join(" ", parts) + " ";
}

static string Short(Type t)
{
    var n = t.Name;
    var i = n.IndexOf("`");
    return i > 0 ? n.Substring(0, i) : n;
}

static string Names(IEnumerable<string> items)
{
    var list = items.ToList();
    return list.Count == 0 ? "(none)" : string.Join(", ", list);
}

