using System;
using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Stats;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// A mix the player has not named yet has no product. Observed live: falling back to the base
    /// product recorded "megasmegma + banana -> megasmegma" — a recipe that appears to do nothing,
    /// credits units to the input, and self-loops the lineage tree.
    /// </summary>
    public class UnnamedMixTests
    {
        private static ProductionEvent Unnamed(string @base, string ingredient, int qty = 20, int time = 900)
        {
            var evt = new ProductionEvent
            {
                Kind = ProductionKind.Mixed,
                Attribution = Attribution.Local,
                ProfileId = "p",
                BaseProductId = @base,
                IngredientId = ingredient,
                OutputProductId = null,          // not named yet
                Quantity = qty,
                ElapsedDays = 40,
                TimeOfDay = time,
                RealTimeUtc = new DateTime(2026, 8, 22, 0, 0, time / 60, DateTimeKind.Utc)
            };
            evt.RecipeId = evt.ComputeRecipeId();
            return evt;
        }

        [Fact]
        public void An_unnamed_mix_is_flagged_and_keeps_no_product_identity()
        {
            var evt = Unnamed("megasmegma", "banana");

            Assert.True(evt.IsAwaitingName);
            Assert.Null(evt.OutputProductId);
            Assert.NotEqual(evt.BaseProductId, evt.OutputProductId);
        }

        [Fact]
        public void Unnamed_units_are_never_credited_to_the_input_product()
        {
            // The actual defect: 20 units of a NEW product were recorded against megasmegma.
            var stats = StatisticsService.Build("p", new[] { Unnamed("megasmegma", "banana") }, DateTime.UtcNow);

            Assert.False(stats.ByProduct.ContainsKey("megasmegma"));
            Assert.Equal(20, stats.Personal.UnitsProduced);   // the batch still counts
        }

        [Fact]
        public void Two_different_unnamed_mixes_do_not_merge()
        {
            var stats = StatisticsService.Build("p", new[]
            {
                Unnamed("megasmegma", "banana", time: 900),
                Unnamed("strawberrypunch", "megabean", time: 901)
            }, DateTime.UtcNow);

            Assert.Equal(2, stats.ByProduct.Count);
        }

        [Fact]
        public void Repeat_batches_of_the_same_unnamed_mix_do_merge()
        {
            var stats = StatisticsService.Build("p", new[]
            {
                Unnamed("megasmegma", "banana", time: 900),
                Unnamed("megasmegma", "banana", time: 1000)
            }, DateTime.UtcNow);

            Assert.Single(stats.ByProduct);
            Assert.Equal(40, stats.ByProduct.Values.Single().Units);
        }

        [Fact]
        public void An_unnamed_mix_reads_understandably_until_it_is_named()
        {
            var stats = StatisticsService.Build("p", new[] { Unnamed("megasmegma", "banana") }, DateTime.UtcNow);

            Assert.Equal("Unnamed mix (megasmegma + banana)", stats.ByProduct.Values.Single().DisplayName);
        }

        [Fact]
        public void Naming_the_mix_gives_every_earlier_batch_its_identity()
        {
            // The player cooks twice, then names it. Both batches are that product.
            var events = new List<ProductionEvent>
            {
                Unnamed("megasmegma", "banana", time: 900),
                Unnamed("megasmegma", "banana", time: 1000)
            };

            var applied = PendingNameResolver.Apply(events, "megasmegma", "banana", "purplehaze", "Purple Haze");

            Assert.Equal(2, applied);
            Assert.All(events, e => Assert.False(e.IsAwaitingName));
            Assert.All(events, e => Assert.Equal("purplehaze", e.OutputProductId));

            var stats = StatisticsService.Build("p", events, DateTime.UtcNow);
            Assert.Equal(40, stats.ByProduct["purplehaze"].Units);
            Assert.Equal("Purple Haze", stats.ByProduct["purplehaze"].DisplayName);
        }

        [Fact]
        public void Naming_does_not_touch_an_unrelated_pending_mix()
        {
            var events = new List<ProductionEvent>
            {
                Unnamed("megasmegma", "banana"),
                Unnamed("strawberrypunch", "megabean")
            };

            PendingNameResolver.Apply(events, "megasmegma", "banana", "purplehaze", "Purple Haze");

            Assert.False(events[0].IsAwaitingName);
            Assert.True(events[1].IsAwaitingName);
        }

        [Fact]
        public void Naming_never_rewrites_an_already_named_batch()
        {
            var named = Unnamed("megasmegma", "banana");
            named.OutputProductId = "alreadynamed";
            named.OutputProductName = "Already Named";

            var applied = PendingNameResolver.Apply(
                new[] { named }, "megasmegma", "banana", "somethingelse", "Something Else");

            Assert.Equal(0, applied);
            Assert.Equal("alreadynamed", named.OutputProductId);
        }

        [Fact]
        public void Pending_batches_are_detectable()
        {
            Assert.True(PendingNameResolver.HasPending(new[] { Unnamed("a", "b") }));

            var named = Unnamed("a", "b");
            named.OutputProductId = "x";
            Assert.False(PendingNameResolver.HasPending(new[] { named }));
        }

        [Fact]
        public void A_named_mix_still_records_its_product_normally()
        {
            var evt = Unnamed("shroom", "chili");
            evt.OutputProductId = "megasmegma";
            evt.OutputProductName = "Mega Smegma";

            var stats = StatisticsService.Build("p", new[] { evt }, DateTime.UtcNow);

            Assert.Equal(20, stats.ByProduct["megasmegma"].Units);
            Assert.False(evt.IsAwaitingName);
        }
    }
}
