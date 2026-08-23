using System.Collections.Generic;
using RecipePlanner.Core.Production;

namespace RecipePlanner.Core.Pricing
{
    /// <summary>
    /// Fills cost / value / profit on a production event.
    ///
    /// Deliberately an interface: the real implementation defers to the game's own
    /// ProductManager.CalculateProductValue rather than reimplementing its maths, but the domain
    /// must stay testable without the game present.
    /// </summary>
    public interface IPricingEngine
    {
        void Price(ProductionEvent evt);
    }

    /// <summary>Leaves all monetary fields at zero. Used until the Pricing phase lands.</summary>
    public sealed class NullPricingEngine : IPricingEngine
    {
        public static readonly NullPricingEngine Instance = new NullPricingEngine();
        private NullPricingEngine() { }
        public void Price(ProductionEvent evt) { }
    }

    /// <summary>Price source: product sale value and ingredient purchase cost, both per unit.</summary>
    public interface IPriceSource
    {
        bool TryGetProductValue(string productId, string quality, out double unitValue);
        bool TryGetIngredientCost(string ingredientId, out double unitCost);
    }

    /// <summary>
    /// Computes per-batch economics from a price source.
    ///
    /// Cost model: one unit of each ingredient in the chain is consumed per unit produced, which is
    /// how the mixing station actually works (ingredient quantity matches mix quantity).
    /// </summary>
    public sealed class PricingEngine : IPricingEngine
    {
        private readonly IPriceSource _prices;

        public PricingEngine(IPriceSource prices)
        {
            _prices = prices;
        }

        public void Price(ProductionEvent evt)
        {
            if (evt == null) return;

            double unitValue;
            if (_prices != null && _prices.TryGetProductValue(evt.OutputProductId, evt.Quality, out unitValue))
                evt.UnitValue = unitValue;

            evt.UnitCost = SumIngredientCost(evt);
            evt.TotalValue = evt.UnitValue * evt.Quantity;
            evt.TotalCost = evt.UnitCost * evt.Quantity;
            evt.EstimatedProfit = evt.TotalValue - evt.TotalCost;
        }

        private double SumIngredientCost(ProductionEvent evt)
        {
            if (_prices == null) return 0d;

            var chain = (evt.IngredientChain != null && evt.IngredientChain.Count > 0)
                ? evt.IngredientChain
                : Single(evt.IngredientId);

            double total = 0d;
            foreach (var ingredient in chain)
            {
                if (string.IsNullOrWhiteSpace(ingredient)) continue;
                double cost;
                if (_prices.TryGetIngredientCost(ingredient, out cost)) total += cost;
            }
            return total;
        }

        private static List<string> Single(string id)
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(id)) list.Add(id);
            return list;
        }
    }
}
