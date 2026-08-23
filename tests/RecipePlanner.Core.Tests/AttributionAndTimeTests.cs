using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RecipePlanner.Core.Production;
using RecipePlanner.Game.Binding;
using Xunit;
using FishNetObject = FishNet.Object.NetworkObject;
using GameLobby = ScheduleOne.Networking.Lobby;
using GameMixOp = ScheduleOne.ObjectScripts.MixOperation;
using GamePlayer = ScheduleOne.PlayerScripts.Player;
using GameProductDef = ScheduleOne.Product.ProductDefinition;
using GameProductManager = ScheduleOne.Product.ProductManager;
using GameStationMk2 = ScheduleOne.ObjectScripts.MixingStationMk2;
using GameTimeManager = ScheduleOne.GameTime.TimeManager;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Regressions for the two bugs the first live run exposed. Both were invisible to the earlier
    /// tests because the fakes were more forgiving than the real game.
    /// </summary>
    [Collection("GameStatics")]
    public class LiveRunRegressionTests : IDisposable
    {
        private static readonly Assembly[] Fakes = { typeof(GameStationMk2).Assembly };

        private readonly FishNetObject _localNetObj = new FishNetObject();
        private readonly GamePlayer _local;
        private readonly GameProductDef _output = new GameProductDef { ID = "slimycrack", Name = "Slimy Crack" };

        public LiveRunRegressionTests()
        {
            GameProductManager.Instance = new GameProductManager();
            GameProductManager.Instance.AllProducts.Add(_output);
            GameTimeManager.Instance = new GameTimeManager { ElapsedDays = 40, CurrentTime = 924 };
            GameLobby.Instance = new GameLobby { PlayerCount = 1 };

            _local = new GamePlayer { PlayerCode = TestKit.SteamId, NetworkObject = _localNetObj };
            GamePlayer.PlayerList = new List<GamePlayer> { _local };
            GamePlayer.Local = _local;
        }

        public void Dispose()
        {
            GameProductManager.Instance = null;
            GameTimeManager.Instance = null;
            GameLobby.Instance = null;
            GamePlayer.PlayerList = new List<GamePlayer>();
            GamePlayer.Local = null;
        }

        private static ReflectionGameFacts Facts(StationUserRegistry registry = null) =>
            new ReflectionGameFacts(Fakes.ToList(), NullLog.Instance, registry);

        private GameStationMk2 Station(bool playerAtStation = false, bool npcUser = false, string guid = null) =>
            new GameStationMk2
            {
                GUID = Guid.Parse(guid ?? "53bbabb0-dad2-409b-a9e9-f120309a7588"),
                ItemInstance = new ScheduleOne.ItemFramework.ItemInstance { ID = "mixingstationmk2" },
                CurrentMixOperation = new GameMixOp
                {
                    ProductID = "fatcum",
                    IngredientID = "chili",
                    Quantity = 20,
                    ProductQuality = ScheduleOne.ItemFramework.EQuality.Standard,
                    KnownOutput = _output
                },
                PlayerUserObject = playerAtStation ? _localNetObj : null,
                NPCUserObject = npcUser ? new FishNetObject() : null
            };

        // ---------------- bug 1: the clock ----------------

        [Fact]
        public void Time_of_day_comes_from_CurrentTime_not_TimeOfDay()
        {
            // The live run read "TimeOfDay" — a save-file JSON key that does not exist on the
            // runtime class — and silently got 0 for every batch.
            Assert.Equal(924, Facts().TimeOfDay);
            Assert.Equal(40, Facts().ElapsedDays);
        }

        [Fact]
        public void The_runtime_class_really_has_no_TimeOfDay_member()
        {
            // Guards against someone "fixing" the fake to make the old code pass.
            Assert.Null(typeof(GameTimeManager).GetProperty("TimeOfDay"));
            Assert.Null(typeof(GameTimeManager).GetField("TimeOfDay"));
        }

        [Fact]
        public void Two_identical_mixes_at_different_times_are_separate_batches()
        {
            // The exact data loss seen live: with time stuck at 0, the second genuine batch of 20
            // units was discarded as a duplicate.
            var tracker = TestKit.Tracker();
            var recorded = new List<ProductionEvent>();
            tracker.ProductionRecorded += recorded.Add;
            var ctx = TestKit.Context();

            GameTimeManager.Instance.CurrentTime = 924;
            tracker.Track(MixingStationReader.Read(Station(), Facts()), ctx);

            GameTimeManager.Instance.CurrentTime = 1130;
            tracker.Track(MixingStationReader.Read(Station(), Facts()), ctx);

            Assert.Equal(2, recorded.Count);
            Assert.Equal(40, recorded.Sum(e => e.Quantity));
            Assert.NotEqual(recorded[0].EventKey, recorded[1].EventKey);
        }

        [Fact]
        public void The_same_batch_reported_twice_at_one_instant_is_still_one_batch()
        {
            // The Mk2 base-call double fire must still collapse.
            var tracker = TestKit.Tracker();
            var recorded = new List<ProductionEvent>();
            tracker.ProductionRecorded += recorded.Add;
            var ctx = TestKit.Context();

            tracker.Track(MixingStationReader.Read(Station(), Facts()), ctx);
            tracker.Track(MixingStationReader.Read(Station(), Facts()), ctx);

            Assert.Single(recorded);
        }

        // ---------------- bug 2: attribution ----------------

        [Fact]
        public void A_batch_finishing_with_nobody_at_the_station_is_still_mine_in_single_player()
        {
            // PlayerUserObject tracks who is at the UI right now; the player always walks away
            // while the mix runs, so it is null at completion. Live, this made every batch
            // Unattributed and worth zero in the statistics.
            var candidate = MixingStationReader.Read(Station(playerAtStation: false), Facts());

            Assert.Equal(Attribution.Local, candidate.ResolveAttribution());
            Assert.Equal(TestKit.SteamId, candidate.ProducedByPlayerCode);
        }

        [Fact]
        public void The_recorded_starter_attributes_a_batch_nobody_is_standing_at()
        {
            var registry = new StationUserRegistry();
            var facts = Facts(registry);

            // Player presses Start...
            facts.CaptureStarter(Station(playerAtStation: true));
            // ...then walks away before it finishes.
            var candidate = MixingStationReader.Read(Station(playerAtStation: false), facts);

            Assert.Equal(Attribution.Local, candidate.ResolveAttribution());
            Assert.Equal(TestKit.SteamId, candidate.ProducedByPlayerCode);
        }

        [Fact]
        public void An_employee_batch_is_never_credited_to_the_player()
        {
            var candidate = MixingStationReader.Read(Station(npcUser: true), Facts());

            Assert.Equal(Attribution.Employee, candidate.ResolveAttribution());
            Assert.False(candidate.IsLocalPlayerUser);
        }

        [Fact]
        public void A_recorded_employee_start_survives_to_completion()
        {
            var registry = new StationUserRegistry();
            var facts = Facts(registry);

            facts.CaptureStarter(Station(npcUser: true));
            var candidate = MixingStationReader.Read(Station(), facts);

            Assert.Equal(Attribution.Employee, candidate.ResolveAttribution());
        }

        [Fact]
        public void In_multiplayer_an_unobserved_batch_is_not_assumed_to_be_mine()
        {
            // Guessing here would credit you with another player's production.
            GameLobby.Instance.PlayerCount = 2;
            GamePlayer.PlayerList.Add(new GamePlayer { PlayerCode = "76561190000000002", NetworkObject = new FishNetObject() });

            var candidate = MixingStationReader.Read(Station(playerAtStation: false), Facts());

            Assert.Equal(Attribution.Unattributed, candidate.ResolveAttribution());
            Assert.False(candidate.IsLocalPlayerUser);
        }

        [Fact]
        public void Multiplayer_is_judged_by_player_count_not_by_being_in_a_lobby()
        {
            // A solo-hosted session with an open lobby is still single-player for attribution.
            GameLobby.Instance.IsInLobby = true;
            GameLobby.Instance.PlayerCount = 1;

            Assert.False(Facts().IsMultiplayerSession);
        }

        [Fact]
        public void A_stale_starter_does_not_leak_onto_the_next_batch()
        {
            var registry = new StationUserRegistry();
            var facts = Facts(registry);
            const string guid = "53bbabb0-dad2-409b-a9e9-f120309a7588";

            facts.CaptureStarter(Station(playerAtStation: true, guid: guid));
            Assert.NotNull(facts.GetStarter(guid));

            facts.ForgetStation(guid);
            Assert.Null(facts.GetStarter(guid));
        }

        [Fact]
        public void Station_users_are_dropped_when_the_save_unloads()
        {
            var registry = new StationUserRegistry();
            var facts = Facts(registry);

            facts.CaptureStarter(Station(playerAtStation: true));
            Assert.Equal(1, registry.Count);

            facts.ResetStationUsers();
            Assert.Equal(0, registry.Count);
        }

        [Fact]
        public void Starters_are_kept_per_station()
        {
            var registry = new StationUserRegistry();
            var facts = Facts(registry);

            facts.CaptureStarter(Station(playerAtStation: true, guid: "53bbabb0-dad2-409b-a9e9-f120309a7588"));
            facts.CaptureStarter(Station(npcUser: true, guid: "3059421d-3982-47e6-9984-b4b32e892489"));

            Assert.True(facts.GetStarter("53bbabb0-dad2-409b-a9e9-f120309a7588").IsLocalPlayer);
            Assert.True(facts.GetStarter("3059421d-3982-47e6-9984-b4b32e892489").WasNpc);
        }

        [Fact]
        public void An_attributed_batch_actually_counts_toward_the_totals()
        {
            // The end-to-end consequence: live, this was false and every batch was worth nothing.
            var tracker = TestKit.Tracker();
            ProductionEvent recorded = null;
            tracker.ProductionRecorded += e => recorded = e;

            tracker.Track(MixingStationReader.Read(Station(), Facts()), TestKit.Context());

            Assert.NotNull(recorded);
            Assert.True(recorded.CountsTowardPersonalTotals);
            Assert.Equal(924, recorded.TimeOfDay);
        }
    }
}
