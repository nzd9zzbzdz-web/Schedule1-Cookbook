using System;
using System.Collections.Generic;
using System.Linq;

namespace RecipePlanner.Core.Recipes
{
    /// <summary>One row of ProductManager.mixRecipes, exactly as the game stores it.</summary>
    public sealed class MixRecipeRow
    {
        public string Product { get; set; }
        public string Mixer { get; set; }
        public string Output { get; set; }

        public MixRecipeRow() { }
        public MixRecipeRow(string product, string mixer, string output)
        {
            Product = product; Mixer = mixer; Output = output;
        }
    }

    /// <summary>A single mixing step with its sides correctly identified.</summary>
    public sealed class ResolvedStep
    {
        public string BaseProductId { get; set; }
        public string AdditiveId { get; set; }
        public string OutputProductId { get; set; }

        public override string ToString() => $"{BaseProductId} + {AdditiveId} -> {OutputProductId}";
    }

    /// <summary>The full path from a base product to one result.</summary>
    public sealed class RecipeLineage
    {
        public string ProductId { get; set; }
        public string RootProductId { get; set; }
        public List<ResolvedStep> Steps { get; set; } = new List<ResolvedStep>();

        /// <summary>False when no path back to a known base exists — the recipe was never discovered.</summary>
        public bool IsComplete { get; set; }

        public int Depth => Steps.Count;
        public IEnumerable<string> Additives => Steps.Select(s => s.AdditiveId);

        /// <summary>e.g. "ogkush + cuke + viagor" — the shape the cookbook lists.</summary>
        public string Describe() =>
            IsComplete && Steps.Count > 0
                ? RootProductId + " + " + string.Join(" + ", Additives)
                : "(origin unknown)";
    }

    /// <summary>A node in the progression tree hanging off one base product.</summary>
    public sealed class LineageNode
    {
        public string ProductId { get; set; }
        public string AdditiveId { get; set; }
        public int Depth { get; set; }
        public List<LineageNode> Children { get; set; } = new List<LineageNode>();
    }

    /// <summary>
    /// Turns the game's flat recipe rows into a navigable tree rooted at base products.
    ///
    /// Two things make this harder than it looks, both established against real save data
    /// (see audit §2.7):
    ///
    ///   1. <b>The field names cannot be trusted.</b> Most rows put the base in <c>Product</c>, but
    ///      roughly one in five is stored the other way round. Sides are therefore identified by
    ///      membership in the known-product set, which classifies real data with zero ambiguity.
    ///   2. <b>The graph contains cycles.</b> <c>thickdick + paracetamol -> thickdick</c> is a real
    ///      row, so every walk is depth- and visit-guarded.
    /// </summary>
    public sealed class RecipeGraph
    {
        /// <summary>
        /// Last-resort base products, used ONLY when the game's own list is unavailable.
        ///
        /// Everything downstream of a base is player-generated: a discovered mix is named by the
        /// player and its id is derived from that name (<c>FinishAndNameMix</c> →
        /// <c>MakeIDFileSafe</c>), so ids like "thickmonkey" exist only in the save that created
        /// them. Never hard-code those. The bases below are seeds and base drugs, which are fixed
        /// game content — but prefer <c>ProductManager.DefaultKnownProducts</c> even for these,
        /// so the mod keeps working if the game adds a strain.
        /// </summary>
        public static readonly string[] FallbackBaseProducts =
        {
            "ogkush", "sourdiesel", "greencrack", "granddaddypurple",
            "meth", "cocaine", "shroom"
        };

        /// <summary>Guards against a pathological chain; real lineages are only a few steps deep.</summary>
        private const int MaxDepth = 32;

        private readonly List<ResolvedStep> _steps = new List<ResolvedStep>();
        private readonly Dictionary<string, ResolvedStep> _producedBy =
            new Dictionary<string, ResolvedStep>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _bases;
        private readonly HashSet<string> _products;

        private RecipeGraph(HashSet<string> bases, HashSet<string> products)
        {
            _bases = bases;
            _products = products;
        }

        public IReadOnlyList<ResolvedStep> Steps => _steps;
        public IEnumerable<string> BaseProducts => _bases.OrderBy(b => b, StringComparer.Ordinal);

        /// <summary>Rows whose sides could not be told apart — both or neither were known products.</summary>
        public List<MixRecipeRow> Ambiguous { get; } = new List<MixRecipeRow>();

        public static RecipeGraph Build(
            IEnumerable<MixRecipeRow> rows,
            IEnumerable<string> knownProducts,
            IEnumerable<string> baseProducts = null)
        {
            var products = new HashSet<string>(
                knownProducts ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            var supplied = baseProducts?.Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
            var bases = new HashSet<string>(
                supplied != null && supplied.Count > 0 ? supplied : FallbackBaseProducts,
                StringComparer.OrdinalIgnoreCase);

            // A base is a product even if the player has not "discovered" it in the game's sense.
            foreach (var b in bases) products.Add(b);

            var graph = new RecipeGraph(bases, products);

            foreach (var row in rows ?? Enumerable.Empty<MixRecipeRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Output)) continue;

                var step = Classify(row, products);
                if (step == null) { graph.Ambiguous.Add(row); continue; }

                graph._steps.Add(step);

                // Self-loops (thickdick + paracetamol -> thickdick) carry no lineage information
                // and would make the product its own ancestor.
                if (!string.Equals(step.OutputProductId, step.BaseProductId, StringComparison.OrdinalIgnoreCase) &&
                    !graph._producedBy.ContainsKey(step.OutputProductId))
                {
                    graph._producedBy[step.OutputProductId] = step;
                }
            }

            return graph;
        }

        /// <summary>
        /// Identifies which side is the base by membership rather than by field name — the only
        /// approach that survives the game storing some rows reversed.
        /// </summary>
        private static ResolvedStep Classify(MixRecipeRow row, HashSet<string> products)
        {
            var productIsBase = row.Product != null && products.Contains(row.Product);
            var mixerIsBase = row.Mixer != null && products.Contains(row.Mixer);

            if (productIsBase && !mixerIsBase)
                return new ResolvedStep { BaseProductId = row.Product, AdditiveId = row.Mixer, OutputProductId = row.Output };

            if (mixerIsBase && !productIsBase)
                return new ResolvedStep { BaseProductId = row.Mixer, AdditiveId = row.Product, OutputProductId = row.Output };

            return null;   // both or neither — cannot tell, and guessing would invert the recipe
        }

        public bool IsBaseProduct(string productId) =>
            productId != null && _bases.Contains(productId);

        /// <summary>Walks back to a base product, guarding against the cycles the data contains.</summary>
        public RecipeLineage GetLineage(string productId)
        {
            var lineage = new RecipeLineage { ProductId = productId };
            if (string.IsNullOrWhiteSpace(productId)) return lineage;

            if (IsBaseProduct(productId))
            {
                lineage.RootProductId = productId;
                lineage.IsComplete = true;
                return lineage;
            }

            var chain = new List<ResolvedStep>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { productId };
            var current = productId;

            while (chain.Count < MaxDepth)
            {
                if (!_producedBy.TryGetValue(current, out var step)) break;

                chain.Add(step);
                current = step.BaseProductId;

                if (IsBaseProduct(current))
                {
                    chain.Reverse();
                    lineage.Steps = chain;
                    lineage.RootProductId = current;
                    lineage.IsComplete = true;
                    return lineage;
                }

                if (!visited.Add(current)) break;   // cycle
            }

            return lineage;   // incomplete: origin unknown
        }

        /// <summary>Products with no derivable origin — the recipe was never discovered.</summary>
        public IEnumerable<string> Orphans(IEnumerable<string> allProducts) =>
            (allProducts ?? _products)
                .Where(p => !IsBaseProduct(p) && !GetLineage(p).IsComplete)
                .OrderBy(p => p, StringComparer.Ordinal);

        /// <summary>
        /// The progression tree under one base product — what the UI renders when the player expands
        /// "OG Kush" and sees everything descended from it.
        /// </summary>
        public LineageNode BuildTree(string baseProductId)
        {
            var root = new LineageNode { ProductId = baseProductId, Depth = 0 };
            var childrenOf = _steps
                .Where(s => !string.Equals(s.OutputProductId, s.BaseProductId, StringComparison.OrdinalIgnoreCase))
                .ToLookup(s => s.BaseProductId, StringComparer.OrdinalIgnoreCase);

            Expand(root, childrenOf, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { baseProductId });
            return root;
        }

        private static void Expand(
            LineageNode node, ILookup<string, ResolvedStep> childrenOf, HashSet<string> onPath)
        {
            if (node.Depth >= MaxDepth) return;

            foreach (var step in childrenOf[node.ProductId])
            {
                // onPath, not a global visited set: the same product can legitimately appear under
                // two different branches, but must never appear inside its own ancestry.
                if (onPath.Contains(step.OutputProductId)) continue;

                var child = new LineageNode
                {
                    ProductId = step.OutputProductId,
                    AdditiveId = step.AdditiveId,
                    Depth = node.Depth + 1
                };
                node.Children.Add(child);

                onPath.Add(step.OutputProductId);
                Expand(child, childrenOf, onPath);
                onPath.Remove(step.OutputProductId);
            }
        }

        /// <summary>Every product grouped under the base it descends from — the section layout.</summary>
        public Dictionary<string, List<string>> GroupByBase(IEnumerable<string> products)
        {
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in _bases) groups[b] = new List<string>();
            groups["(unknown)"] = new List<string>();

            foreach (var product in products ?? _products)
            {
                if (IsBaseProduct(product)) continue;
                var lineage = GetLineage(product);
                var key = lineage.IsComplete ? lineage.RootProductId : "(unknown)";
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<string>();
                list.Add(product);
            }

            foreach (var list in groups.Values) list.Sort(StringComparer.Ordinal);
            return groups;
        }
    }
}
