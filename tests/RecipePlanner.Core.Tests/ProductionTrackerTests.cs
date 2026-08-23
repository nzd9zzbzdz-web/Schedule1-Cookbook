using System.Collections.Generic;
using RecipePlanner.Core.Production;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Phase 9 exit tests. The positive case is one line; the negative cases are the whole point —
    /// each of these is a way the statistics could silently become wrong.
    /// </summary>
    public class ProductionTrackerTests
    {
        [Fact]
        public void One_cook_produces_exactly_one_event()
        {
            var recorded = new List<ProductionEvent>();
            var tracker = TestKit.Tracker();
            tracker.ProductionRecorded += recorded.Add;

            var result = tracker.Track(TestKit.Mix(), TestKit.Context());

            Assert.True(result.Accepted);
            Assert.Single(recorded);

            var e = recorded[0];
            Assert.Equal("greencrack", e.BaseProductId);
            Assert.Equal("mouthwash", e.IngredientId);
            Assert.Equal(20, e.Quantity);
            Assert.Equal(Attribution.Local, e.Attribution);
            Assert.True(e.CountsTowardPersonalTotals);
        }

        // ---------- the Mk2 double-fire ----------

        [Fact]
        public void Mk2_override_calling_base_does_not_double_count()
        {
            // MixingStationMk2.MixingDone() may call base.MixingDone(), firing both patches for one
            // batch. The idempotency key must absorb it — audit §2.1 caveat 2.
            var tracker = TestKit.Tracker();
            var ctx = TestKit.Context();

            var first = tracker.Track(TestKit.Mix(), ctx);
            var second = tracker.Track(TestKit.Mix(), ctx);

            Assert.True(first.Accepted);
            Assert.False(second.Accepted);
            Assert.Equal(RejectionReason.DuplicateEvent, second.Reason);
        }

        [Fact]
        public void Different_stations_at_the_same_moment_are_separate_batches()
        {
            var tracker = TestKit.Tracker();
            var ctx = TestKit.Context();

            Assert.True(tracker.Track(TestKit.Mix(station: "station-a"), ctx).Accepted);
            Assert.True(tracker.Track(TestKit.Mix(station: "station-b"), ctx).Accepted);
        }

        [Fact]
        public void The_same_station_cooking_again_later_is_a_new_batch()
        {
            var tracker = TestKit.Tracker();
            var ctx = TestKit.Context();

            Assert.True(tracker.Track(TestKit.Mix(day: 40, time: 924), ctx).Accepted);
            Assert.True(tracker.Track(TestKit.Mix(day: 40, time: 1030), ctx).Accepted);
        }

        // ---------- reload / load-settle ----------

        [Fact]
        public void Nothing_is_recorded_before_the_game_finishes_loading()
        {
            var tracker = TestKit.Tracker(FakeLoadState.NotLoaded());
            var result = tracker.Track(TestKit.Mix(), TestKit.Context());

            Assert.False(result.Accepted);
            Assert.Equal(RejectionReason.GameNotLoaded, result.Reason);
        }

        [Fact]
        public void Stations_resuming_mid_operation_on_load_are_ignored()
        {
            // Saved stations resume with CurrentMixTime already set and can replay their completion
            // path. Recording those would recount existing inventory on every reload — audit §5.
            var tracker = TestKit.Tracker(FakeLoadState.JustLoaded());
            var result = tracker.Track(TestKit.Mix(), TestKit.Context());

            Assert.False(result.Accepted);
            Assert.Equal(RejectionReason.WithinLoadSettleWindow, result.Reason);
        }

        [Fact]
        public void Reloading_a_save_does_not_recount_history()
        {
            // Session 1 records a batch; session 2 replays the log into the dedupe set and sees the
            // same completion again.
            var seen = new InMemorySeenEventKeys();
            var ctx = TestKit.Context();

            var session1 = TestKit.Tracker(seen: seen);
            var recorded = new List<ProductionEvent>();
            session1.ProductionRecorded += recorded.Add;
            session1.Track(TestKit.Mix(), ctx);

            var replayed = new InMemorySeenEventKeys();
            replayed.Seed(recorded);

            var session2 = TestKit.Tracker(seen: replayed);
            var result = session2.Track(TestKit.Mix(), ctx);

            Assert.False(result.Accepted);
            Assert.Equal(RejectionReason.DuplicateEvent, result.Reason);
        }

        // ---------- identity ----------

        [Fact]
        public void Nothing_is_recorded_without_an_active_profile()
        {
            var tracker = TestKit.Tracker();
            var result = tracker.Track(TestKit.Mix(), null);

            Assert.False(result.Accepted);
            Assert.Equal(RejectionReason.NoActiveProfile, result.Reason);
        }

        [Fact]
        public void Two_characters_do_not_share_a_dedupe_namespace()
        {
            // An identical batch on a different character is a different batch.
            var seen = new InMemorySeenEventKeys();
            var tracker = TestKit.Tracker(seen: seen);

            var echo = TestKit.Context(TestKit.Identity(org: "Echo"));
            var delta = TestKit.Context(TestKit.Identity(org: "Delta"));

            Assert.True(tracker.Track(TestKit.Mix(), echo).Accepted);
            Assert.True(tracker.Track(TestKit.Mix(), delta).Accepted);
        }

        // ---------- attribution ----------

        [Fact]
        public void Employee_batches_are_recorded_but_excluded_from_personal_totals()
        {
            var tracker = TestKit.Tracker();
            var result = tracker.Track(
                TestKit.Mix(local: false, npc: true), TestKit.Context());

            Assert.True(result.Accepted);
            Assert.Equal(Attribution.Employee, result.Event.Attribution);
            Assert.False(result.Event.CountsTowardPersonalTotals);
        }

        [Fact]
        public void Another_players_batch_is_recorded_but_excluded()
        {
            var candidate = TestKit.Mix(local: false);
            candidate.ProducedByPlayerCode = "76561190000000002";

            var result = TestKit.Tracker().Track(candidate, TestKit.Context());

            Assert.True(result.Accepted);
            Assert.Equal(Attribution.Remote, result.Event.Attribution);
            Assert.False(result.Event.CountsTowardPersonalTotals);
            Assert.Equal("76561190000000002", result.Event.ProducedByPlayerCode);
        }

        [Fact]
        public void Unattributed_batches_are_recorded_but_excluded()
        {
            var result = TestKit.Tracker().Track(TestKit.Mix(local: false), TestKit.Context());

            Assert.True(result.Accepted);
            Assert.Equal(Attribution.Unattributed, result.Event.Attribution);
            Assert.False(result.Event.CountsTowardPersonalTotals);
        }

        // ---------- transforms ----------

        [Theory]
        [InlineData(ProductionKind.Dried)]
        [InlineData(ProductionKind.Bricked)]
        [InlineData(ProductionKind.Packaged)]
        public void Transforms_are_recorded_but_never_count_as_new_units(ProductionKind kind)
        {
            var result = TestKit.Tracker().Track(TestKit.Mix(kind: kind), TestKit.Context());

            Assert.True(result.Accepted);
            Assert.False(result.Event.CountsTowardPersonalTotals);
        }

        [Theory]
        [InlineData(ProductionKind.Mixed)]
        [InlineData(ProductionKind.Cooked)]
        [InlineData(ProductionKind.Harvested)]
        public void Real_production_kinds_do_count(ProductionKind kind)
        {
            var result = TestKit.Tracker().Track(TestKit.Mix(kind: kind), TestKit.Context());

            Assert.True(result.Accepted);
            Assert.True(result.Event.CountsTowardPersonalTotals);
        }

        // ---------- malformed input ----------

        [Theory]
        [InlineData(null, 20)]      // no station guid -> no idempotency possible
        [InlineData("station-a", 0)] // zero quantity -> not a batch
        public void Malformed_candidates_are_refused(string station, int quantity)
        {
            var candidate = TestKit.Mix(station: station, quantity: quantity);
            var result = TestKit.Tracker().Track(candidate, TestKit.Context());

            Assert.False(result.Accepted);
            Assert.Equal(RejectionReason.MalformedCandidate, result.Reason);
        }

        [Fact]
        public void Rejections_are_reported_so_they_can_be_logged()
        {
            var reasons = new List<RejectionReason>();
            var tracker = TestKit.Tracker(FakeLoadState.NotLoaded());
            tracker.ProductionRejected += (_, reason) => reasons.Add(reason);

            tracker.Track(TestKit.Mix(), TestKit.Context());

            Assert.Equal(new[] { RejectionReason.GameNotLoaded }, reasons);
        }

        [Fact]
        public void Trust_flags_are_stamped_onto_every_event()
        {
            var ctx = TestKit.Context();
            ctx.ConsoleEnabled = true;
            ctx.UseRandomizedMixMaps = true;

            var result = TestKit.Tracker().Track(TestKit.Mix(), ctx);

            Assert.True(result.Event.ConsoleEnabled);
            Assert.True(result.Event.RandomizedMixMaps);
            Assert.Equal("0.4.5f2", result.Event.GameVersion);
        }
    }
}
