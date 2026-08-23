using System;
using System.Linq;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Stats;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Migration for events written before unnamed mixes were handled. The precision matters:
    /// genuine identity mixes exist in this game and must survive untouched.
    /// </summary>
    public class LegacyEventRepairTests
    {
        private static ProductionEvent Event(
            string @base, string ingredient, string output, bool newDiscovery,
            ProductionKind kind = ProductionKind.Mixed)
        {
            var e = new ProductionEvent
            {
                Kind = kind,
                Attribution = Attribution.Local,
                ProfileId = "p",
                BaseProductId = @base,
                IngredientId = ingredient,
                OutputProductId = output,
                OutputProductName = output,
                WasNewDiscovery = newDiscovery,
                Quantity = 20,
                RealTimeUtc = DateTime.UtcNow
            };
            e.RecipeId = e.ComputeRecipeId();
            return e;
        }

        [Fact]
        public void The_defect_is_repaired_back_to_awaiting_name()
        {
            // Exactly what the old build wrote: a NEW mix recorded as producing its own input.
            var broken = Event("megasmegma", "banana", "megasmegma", newDiscovery: true);

            Assert.Equal(1, LegacyEventRepair.Apply(new[] { broken }));
            Assert.True(broken.IsAwaitingName);
            Assert.Null(broken.OutputProductId);
        }

        [Fact]
        public void A_genuine_identity_mix_is_left_alone()
        {
            // thickdick + paracetamol -> thickdick is real recipe data. The output is KNOWN, which
            // is what separates it from the defect.
            var real = Event("thickdick", "paracetamol", "thickdick", newDiscovery: false);

            Assert.Equal(0, LegacyEventRepair.Apply(new[] { real }));
            Assert.Equal("thickdick", real.OutputProductId);
        }

        [Fact]
        public void A_normal_event_is_untouched()
        {
            var fine = Event("shroom", "chili", "megasmegma", newDiscovery: false);

            Assert.Equal(0, LegacyEventRepair.Apply(new[] { fine }));
            Assert.Equal("megasmegma", fine.OutputProductId);
        }

        [Fact]
        public void Already_repaired_events_are_not_touched_again()
        {
            var pending = Event("megasmegma", "banana", null, newDiscovery: true);

            Assert.Equal(0, LegacyEventRepair.Apply(new[] { pending }));
            Assert.True(pending.IsAwaitingName);
        }

        [Fact]
        public void Only_mixes_are_considered()
        {
            var harvested = Event("ogkush", "", "ogkush", newDiscovery: true, kind: ProductionKind.Harvested);

            Assert.Equal(0, LegacyEventRepair.Apply(new[] { harvested }));
        }

        [Fact]
        public void Repair_is_idempotent()
        {
            var events = new[] { Event("megasmegma", "banana", "megasmegma", newDiscovery: true) };

            Assert.Equal(1, LegacyEventRepair.Apply(events));
            Assert.Equal(0, LegacyEventRepair.Apply(events));
        }

        [Fact]
        public void Repaired_units_stop_being_credited_to_the_input_product()
        {
            var events = new[]
            {
                Event("shroom", "chili", "megasmegma", newDiscovery: false),        // legitimate
                Event("megasmegma", "banana", "megasmegma", newDiscovery: true)     // the defect
            };

            var before = StatisticsService.Build("p", events, DateTime.UtcNow);
            Assert.Equal(40, before.ByProduct["megasmegma"].Units);   // 20 of these are wrong

            LegacyEventRepair.Apply(events);
            var after = StatisticsService.Build("p", events, DateTime.UtcNow);

            Assert.Equal(20, after.ByProduct["megasmegma"].Units);    // only the real one
            Assert.Equal(40, after.Personal.UnitsProduced);           // both batches still count
        }

        [Fact]
        public void Repaired_events_can_then_be_named_normally()
        {
            var events = new[] { Event("megasmegma", "banana", "megasmegma", newDiscovery: true) };
            LegacyEventRepair.Apply(events);

            var applied = PendingNameResolver.Apply(events, "megasmegma", "banana", "purplehaze", "Purple Haze");

            Assert.Equal(1, applied);
            Assert.Equal("purplehaze", events[0].OutputProductId);
        }

        [Fact]
        public void Counting_reports_without_mutating()
        {
            var events = new[] { Event("megasmegma", "banana", "megasmegma", newDiscovery: true) };

            Assert.Equal(1, LegacyEventRepair.Count(events));
            Assert.Equal("megasmegma", events[0].OutputProductId);   // unchanged
        }
    }
}
