using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RecipePlanner.Game.Binding
{
    public enum SymbolStatus { Ok, TypeMissing, MemberMissing }

    public sealed class SymbolFinding
    {
        public string TypeName { get; set; }
        public SymbolStatus Status { get; set; }
        public List<string> MissingMembers { get; set; } = new List<string>();
        public bool Optional { get; set; }
        public string Purpose { get; set; }

        public override string ToString()
        {
            if (Status == SymbolStatus.TypeMissing) return $"{TypeName}: type not found";
            if (Status == SymbolStatus.MemberMissing)
                return $"{TypeName}: missing {string.Join(", ", MissingMembers)}";
            return $"{TypeName}: ok";
        }
    }

    public sealed class GuardReport
    {
        public List<SymbolFinding> Findings { get; } = new List<SymbolFinding>();

        public IEnumerable<SymbolFinding> Failures =>
            Findings.Where(f => f.Status != SymbolStatus.Ok);

        /// <summary>Only non-optional failures block. Optional gaps degrade features, not the mod.</summary>
        public IEnumerable<SymbolFinding> BlockingFailures =>
            Failures.Where(f => !f.Optional);

        public IEnumerable<SymbolFinding> Warnings =>
            Failures.Where(f => f.Optional);

        /// <summary>
        /// The gate. When false the tracker must disable itself: recording garbage statistics is
        /// worse than recording none (audit §5).
        /// </summary>
        public bool SafeToTrack => !BlockingFailures.Any();

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine(SafeToTrack
                ? $"Symbol check PASSED ({Findings.Count(f => f.Status == SymbolStatus.Ok)}/{Findings.Count} hooks resolved)"
                : "Symbol check FAILED — tracking disabled to avoid recording incorrect statistics.");

            foreach (var f in BlockingFailures) sb.AppendLine("  [BLOCKING] " + f + "  (" + f.Purpose + ")");
            foreach (var f in Warnings) sb.AppendLine("  [degraded] " + f + "  (" + f.Purpose + ")");

            if (!SafeToTrack)
                sb.AppendLine($"  Hook table was verified against game version {HookTable.VerifiedAgainstGameVersion}. " +
                              "If the game updated, the hook table needs re-auditing: " +
                              "node tools/il2cpp-dump/dump.js '<type-regex>'");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Verifies, by reflection and before any patch is applied, that every symbol the tracker
    /// depends on still exists in the running game.
    ///
    /// This is the Phase 18 "update protection" requirement, and it is deliberately cheap: it costs
    /// a few milliseconds at startup and converts a whole class of silent-wrong-data bugs into one
    /// loud, actionable log line.
    /// </summary>
    public static class SymbolGuard
    {
        private const BindingFlags AnyMember =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        public static GuardReport Verify(IEnumerable<Assembly> assemblies, IEnumerable<HookDefinition> table = null)
        {
            var list = assemblies?.Where(a => a != null).ToList() ?? new List<Assembly>();
            var report = new GuardReport();

            foreach (var def in table ?? HookTable.All)
                report.Findings.Add(Check(list, def));

            return report;
        }

        private static SymbolFinding Check(List<Assembly> assemblies, HookDefinition def)
        {
            var finding = new SymbolFinding
            {
                TypeName = def.TypeName,
                Optional = def.Optional,
                Purpose = def.Purpose
            };

            var type = ResolveType(assemblies, def.TypeName);
            if (type == null)
            {
                finding.Status = SymbolStatus.TypeMissing;
                return finding;
            }

            foreach (var method in def.Methods ?? new string[0])
                if (!HasMethod(type, method)) finding.MissingMembers.Add(method + "()");

            foreach (var member in def.Members ?? new string[0])
                if (!HasMember(type, member)) finding.MissingMembers.Add(member);

            finding.Status = finding.MissingMembers.Count == 0 ? SymbolStatus.Ok : SymbolStatus.MemberMissing;
            return finding;
        }

        /// <summary>
        /// Namespace prefix Il2CppInterop puts on every generated proxy type. On the IL2CPP branch
        /// <c>ScheduleOne.ObjectScripts.MixingStation</c> is emitted as
        /// <c>Il2CppScheduleOne.ObjectScripts.MixingStation</c>, so the hook table — which records
        /// the game's real names — has to be tried both ways.
        /// </summary>
        public const string Il2CppPrefix = "Il2Cpp";

        /// <summary>
        /// Resolves a game type by its Mono-branch name, transparently falling back to the
        /// IL2CPP proxy name. This is what lets one mod assembly serve both Steam branches.
        /// </summary>
        public static Type ResolveType(IEnumerable<Assembly> assemblies, string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;

            var list = assemblies as IList<Assembly> ?? assemblies?.ToList();
            if (list == null) return null;

            return Find(list, fullName)
                ?? Find(list, Il2CppPrefix + fullName);
        }

        private static Type Find(IList<Assembly> assemblies, string fullName)
        {
            foreach (var asm in assemblies)
            {
                Type t;
                try { t = asm.GetType(fullName, false); }
                catch (ReflectionTypeLoadException) { continue; }
                catch (TypeLoadException) { continue; }
                catch (FileNotFoundException) { continue; }
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>
        /// Matches on name only, ignoring overloads. The audit records parameter counts, but a hook
        /// that gains an optional parameter should degrade to a warning at patch time rather than
        /// failing the whole startup check.
        /// </summary>
        public static bool HasMethod(Type type, string name)
        {
            foreach (var t in Hierarchy(type))
            {
                try { if (t.GetMethods(AnyMember).Any(m => m.Name == name)) return true; }
                catch (FileNotFoundException) { /* unresolvable reference — keep walking */ }
                catch (TypeLoadException) { }
            }
            return false;
        }

        public static bool HasMember(Type type, string name)
        {
            foreach (var t in Hierarchy(type))
            {
                try
                {
                    if (t.GetField(name, AnyMember) != null) return true;
                    if (t.GetProperty(name, AnyMember) != null) return true;
                }
                catch (FileNotFoundException) { }
                catch (TypeLoadException) { }
            }
            return false;
        }

        /// <summary>
        /// Walks a type and its base types, tolerating a base that cannot be resolved.
        ///
        /// Reading <c>BaseType</c> throws when the declaring assembly is not loadable — which
        /// happens whenever the Il2Cpp proxies are inspected without Il2CppInterop.Runtime present.
        /// The symbol check must degrade to "member not found" rather than throwing out of mod
        /// startup, because an exception here would take the whole mod down instead of just
        /// disabling tracking.
        /// </summary>
        private static IEnumerable<Type> Hierarchy(Type type)
        {
            var t = type;
            while (t != null)
            {
                yield return t;
                try { t = t.BaseType; }
                catch (FileNotFoundException) { yield break; }
                catch (TypeLoadException) { yield break; }
            }
        }

        /// <summary>
        /// Assemblies worth searching. On the Mono branch the game lives in Assembly-CSharp; the
        /// filter keeps the check off the several hundred framework assemblies.
        /// </summary>
        public static IEnumerable<Assembly> GameAssemblies(IEnumerable<Assembly> loaded)
        {
            foreach (var asm in loaded ?? Enumerable.Empty<Assembly>())
            {
                string name;
                try { name = asm.GetName().Name; }
                catch { continue; }

                if (name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf("ScheduleOne", StringComparison.OrdinalIgnoreCase) >= 0)
                    yield return asm;
            }
        }
    }
}
