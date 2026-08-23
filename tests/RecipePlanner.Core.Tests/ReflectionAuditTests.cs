using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Every game member reached by reflection must be visible to <c>SymbolGuard</c>.
    ///
    /// This exists because the same failure happened twice. Pricing was reached through
    /// <c>ProductPrices</c>, <c>Registry</c> and <c>BasePurchasePrice</c>, none of which were in the
    /// hook table — so the symbol check reported a confident 13/13 PASS while every money figure
    /// silently read $0. An audit afterwards found sixteen more members in the same position.
    ///
    /// The mod's central promise is that it refuses to record what it cannot verify. Reflection the
    /// symbol check cannot see breaks that promise quietly, which is the worst way for it to break:
    /// a game update moves a member, the guard still says PASS, and the numbers just go wrong.
    ///
    /// So: adding a new reflective read now fails this test until it is either declared in the hook
    /// table or explicitly excused below. Being forced to make that choice is the entire point.
    /// </summary>
    public class ReflectionAuditTests
    {
        /// <summary>
        /// Literals that are not game members. Each is excused for a stated reason — an allowlist
        /// nobody can explain is just a way of turning the check off.
        /// </summary>
        private static readonly Dictionary<string, string> NotGameMembers = new Dictionary<string, string>
        {
            ["Key"] = "KeyValuePair, walking a dictionary reflectively",
            ["Value"] = "KeyValuePair, walking a dictionary reflectively",
            ["instance"] = "lower-case fallback for the singleton probe; 'Instance' is the declared one",
            ["Definition"] = "a wrapper-or-definition guess with a '?? entry' fallback, not a known member",
            ["ID"] = "declared on the many definition types that carry one; checked via their own entries",
            ["Name"] = "as above",
            ["x"] = "UnityEngine.Vector2 component",
            ["y"] = "UnityEngine.Vector2 component",
            ["r"] = "UnityEngine.Color component",
            ["g"] = "UnityEngine.Color component",
            ["b"] = "UnityEngine.Color component",
        };

        [Fact]
        public void Every_reflective_member_is_declared_or_excused()
        {
            var sourceRoot = Path.Combine(RepoRoot(), "src", "RecipePlanner.Game");
            Assert.True(Directory.Exists(sourceRoot), "Could not find RecipePlanner.Game at " + sourceRoot);

            var hookTable = File.ReadAllText(Path.Combine(sourceRoot, "Binding", "HookTable.cs"));

            var undeclared = new List<string>();

            foreach (var file in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                // The hook table is the declaration, not a use of one.
                if (file.EndsWith("HookTable.cs", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var member in ReflectiveMembers(File.ReadAllText(file)))
                {
                    if (NotGameMembers.ContainsKey(member)) continue;
                    if (hookTable.Contains("\"" + member + "\"")) continue;

                    undeclared.Add($"{member}  ({Path.GetFileName(file)})");
                }
            }

            Assert.True(
                undeclared.Count == 0,
                "These members are read reflectively but are not in HookTable, so a game update that "
                + "moves them would go undetected:\n  "
                + string.Join("\n  ", undeclared.Distinct().OrderBy(s => s))
                + "\n\nAdd them to HookTable (Optional unless their absence should stop tracking), "
                + "or to NotGameMembers with a reason.");
        }

        /// <summary>
        /// The names passed to the <c>Reflect</c> helpers. Deliberately a narrow pattern: it matches
        /// the shape the codebase actually uses, and a literal it cannot see is one this test would
        /// wrongly pass. If a new helper is added, it belongs in here too.
        /// </summary>
        private static IEnumerable<string> ReflectiveMembers(string source)
        {
            var pattern = new Regex(
                @"Reflect\.(?:Get|GetString|GetStatic|GetInt|GetBool|Call|CallOut)\s*\([^,]+,\s*""([A-Za-z_][A-Za-z0-9_]*)""",
                RegexOptions.Compiled);

            foreach (Match match in pattern.Matches(source))
                yield return match.Groups[1].Value;
        }

        /// <summary>
        /// Walks up from the test assembly until the solution file appears, rather than counting
        /// "../" hops — the bin path depends on target framework and configuration, so a fixed
        /// count breaks the moment either changes.
        /// </summary>
        private static string RepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (directory.GetFiles("*.slnx").Length > 0) return directory.FullName;
                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the repository root above " + AppContext.BaseDirectory);
        }
    }
}
