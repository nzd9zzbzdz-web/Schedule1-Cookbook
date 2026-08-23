using System;
using System.Collections.Generic;
using RecipePlanner.Core.Production;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// The seen-key set is a mirror of the event log, and a mirror that keeps things the log has
    /// dropped is worse than no mirror at all.
    ///
    /// It used to accumulate across save loads within one game session. That defeated the rollback
    /// path entirely: quit to menu without saving and reload, RollbackReconciler correctly deletes
    /// the abandoned batch from disk, the station replays and genuinely produces it again — and the
    /// tracker rejected it as a duplicate against a key that only existed in memory.
    ///
    /// The batch was then lost outright, which is a worse outcome than the double-counting the
    /// reconciler was written to prevent.
    /// </summary>
    public class SeenKeyLifetimeTests
    {
        private static ProductionEvent Event(string profile, string key) =>
            new ProductionEvent { ProfileId = profile, EventKey = key };

        [Fact]
        public void Seeding_replaces_rather_than_accumulates()
        {
            var seen = new InMemorySeenEventKeys();

            seen.Seed(new[] { Event("p", "abandoned") });
            Assert.True(seen.Contains("p", "abandoned"));

            // The reload: the log no longer holds that event, so neither should this.
            seen.Seed(new List<ProductionEvent>());

            Assert.False(seen.Contains("p", "abandoned"));
            Assert.Equal(0, seen.Count);
        }

        /// <summary>The whole point: a replayed batch must be recordable again.</summary>
        [Fact]
        public void A_rolled_back_batch_can_be_recorded_again_after_a_reload()
        {
            var seen = new InMemorySeenEventKeys();
            var batch = Event("p", "station|og+banana|d40-1600");

            seen.Seed(new[] { batch });

            // RollbackReconciler drops it, so the reload seeds a log without it.
            seen.Seed(new List<ProductionEvent>());

            Assert.False(seen.Contains("p", batch.EventKey));
        }

        [Fact]
        public void Keys_stay_separated_by_profile()
        {
            var seen = new InMemorySeenEventKeys();
            seen.Seed(new[] { Event("alice", "same-key"), Event("bob", "same-key") });

            Assert.True(seen.Contains("alice", "same-key"));
            Assert.True(seen.Contains("bob", "same-key"));
            Assert.False(seen.Contains("carol", "same-key"));
        }

        [Fact]
        public void Clearing_and_seeding_nothing_do_not_throw()
        {
            var seen = new InMemorySeenEventKeys();

            seen.Clear();
            seen.Seed(null);
            seen.Seed(new ProductionEvent[] { null });

            Assert.Equal(0, seen.Count);
        }
    }
}
