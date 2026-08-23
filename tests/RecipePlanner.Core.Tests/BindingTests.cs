using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Tests.Fakes;
using RecipePlanner.Game.Binding;
using Xunit;
using FishNetObject = FishNet.Object.NetworkObject;
using GameMixOp = ScheduleOne.ObjectScripts.MixOperation;
using GamePlayer = ScheduleOne.PlayerScripts.Player;
using GameProductDef = ScheduleOne.Product.ProductDefinition;
using GameProductManager = ScheduleOne.Product.ProductManager;
using GameStation = ScheduleOne.ObjectScripts.MixingStation;
using GameStationMk2 = ScheduleOne.ObjectScripts.MixingStationMk2;
using GameTimeManager = ScheduleOne.GameTime.TimeManager;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// The fake game types mirror the real ones, which means they carry static singletons
    /// (Player.Local, ProductManager.Instance, TimeManager.Instance). xunit parallelises test
    /// CLASSES, so without this they clobber each other and fail intermittently.
    /// </summary>
    [CollectionDefinition("GameStatics", DisableParallelization = true)]
    public class GameStaticsCollection { }

    [Collection("GameStatics")]
    public class SymbolGuardTests
    {
        private static Assembly[] Fakes => new[] { typeof(GameStation).Assembly };

        [Fact]
        public void Passes_against_a_surface_matching_the_audit()
        {
            var report = SymbolGuard.Verify(Fakes, MixingHooks());

            Assert.True(report.SafeToTrack, report.Describe());
            Assert.All(report.Findings, f => Assert.Equal(SymbolStatus.Ok, f.Status));
        }

        [Fact]
        public void A_renamed_method_blocks_tracking_rather_than_recording_garbage()
        {
            var table = new[]
            {
                new HookDefinition
                {
                    TypeName = HookTable.MixingStation,
                    Purpose = "primary hook",
                    Methods = new[] { "MixingCompletedRenamedByAnUpdate" }
                }
            };

            var report = SymbolGuard.Verify(Fakes, table);

            Assert.False(report.SafeToTrack);
            Assert.Single(report.BlockingFailures);
            Assert.Contains("MixingCompletedRenamedByAnUpdate", report.Describe());
            Assert.Contains("il2cpp-dump", report.Describe());  // tells the operator how to fix it
        }

        [Fact]
        public void A_missing_type_blocks_tracking()
        {
            var report = SymbolGuard.Verify(Fakes, new[]
            {
                new HookDefinition { TypeName = "ScheduleOne.ObjectScripts.DeletedStation", Purpose = "x" }
            });

            Assert.False(report.SafeToTrack);
            Assert.Equal(SymbolStatus.TypeMissing, report.Findings.Single().Status);
        }

        [Fact]
        public void Optional_gaps_degrade_features_without_disabling_the_mod()
        {
            var report = SymbolGuard.Verify(Fakes, new[]
            {
                new HookDefinition { TypeName = HookTable.MixingStation, Purpose = "core", Methods = new[] { "MixingDone" } },
                new HookDefinition { TypeName = "ScheduleOne.ObjectScripts.FutureStation", Purpose = "nice to have", Optional = true }
            });

            Assert.True(report.SafeToTrack);
            Assert.Single(report.Warnings);
            Assert.Contains("degraded", report.Describe());
        }

        [Fact]
        public void Types_resolve_through_the_Il2Cpp_namespace_prefix()
        {
            // On the IL2CPP branch Il2CppInterop emits Il2CppScheduleOne.*, so a hook table written
            // against the game's real names must still resolve. One assembly, both Steam branches.
            var resolved = SymbolGuard.ResolveType(Fakes, HookTable.MixingStation);
            Assert.NotNull(resolved);

            var il2cppOnly = SymbolGuard.ResolveType(Fakes, "ScheduleOne.ObjectScripts.MixingStation");
            Assert.NotNull(il2cppOnly);

            // Proving the prefixed variant is genuinely present and reachable.
            var prefixed = Fakes[0].GetType("Il2CppScheduleOne.ObjectScripts.MixingStation");
            Assert.NotNull(prefixed);
            Assert.Equal(SymbolStatus.Ok, SymbolGuard
                .Verify(Fakes, new[]
                {
                    new HookDefinition
                    {
                        TypeName = "ScheduleOne.ObjectScripts.MixingStation",
                        Purpose = "resolves either way",
                        Methods = new[] { "MixingDone" }
                    }
                }).Findings.Single().Status);
        }

        [Fact]
        public void The_Mk2_override_is_a_separate_patch_target()
        {
            var baseMethod = typeof(GameStation).GetMethod("MixingDone");
            var mk2Method = typeof(GameStationMk2).GetMethod("MixingDone");

            Assert.NotNull(mk2Method);
            Assert.NotEqual(baseMethod.MethodHandle.Value, mk2Method.MethodHandle.Value);
            Assert.Equal(typeof(GameStationMk2), mk2Method.DeclaringType);
        }

        [Fact]
        public void Game_assemblies_filter_keeps_the_check_off_the_framework()
        {
            var picked = SymbolGuard.GameAssemblies(new[] { typeof(GameStation).Assembly, typeof(string).Assembly }).ToList();
            Assert.DoesNotContain(typeof(string).Assembly, picked);
        }

        [Fact]
        public void The_shipped_hook_table_is_internally_coherent()
        {
            Assert.NotEmpty(HookTable.Required);
            Assert.All(HookTable.All, d =>
            {
                Assert.False(string.IsNullOrWhiteSpace(d.TypeName));
                Assert.False(string.IsNullOrWhiteSpace(d.Purpose));
                Assert.StartsWith("ScheduleOne.", d.TypeName);
            });
            Assert.DoesNotContain(HookTable.All.Where(d => d.Optional), d => d.TypeName == HookTable.MixingStation);
        }

        private static HookDefinition[] MixingHooks() => new[]
        {
            new HookDefinition
            {
                TypeName = HookTable.MixingStation,
                Purpose = "primary production hook",
                Methods = new[] { "MixingDone", "GetMixQuantity", "GetProduct", "GetMixer" },
                Members = new[] { "CurrentMixOperation", "PlayerUserObject", "NPCUserObject" }
            },
            new HookDefinition
            {
                TypeName = HookTable.MixOperation,
                Purpose = "the batch",
                Members = new[] { "ProductID", "ProductQuality", "IngredientID", "Quantity" }
            },
            new HookDefinition
            {
                TypeName = HookTable.MixingStationMk2,
                Purpose = "override target",
                Methods = new[] { "MixingDone" }
            }
        };
    }

    public class ReflectTests
    {
        [Fact]
        public void CallOut_invokes_a_method_with_an_out_parameter()
        {
            var expected = new GameProductDef { ID = "bluelightning" };
            var op = new GameMixOp { KnownOutput = expected };

            var result = Reflect.CallOut(op, "IsOutputKnown", out var outValue);

            Assert.True((bool)result);
            Assert.Same(expected, outValue);
        }

        [Fact]
        public void CallOut_reports_an_unknown_output()
        {
            var result = Reflect.CallOut(new GameMixOp { KnownOutput = null }, "IsOutputKnown", out var outValue);

            Assert.False((bool)result);
            Assert.Null(outValue);
        }

        [Fact]
        public void CallOut_on_a_missing_method_degrades_instead_of_throwing()
        {
            var result = Reflect.CallOut(new GameMixOp(), "NoSuchMethod", out var outValue);

            Assert.Null(result);
            Assert.Null(outValue);
        }

        [Fact]
        public void Enumerate_handles_a_normal_collection()
        {
            var items = Reflect.Enumerate(new List<string> { "a", "b" }).ToList();
            Assert.Equal(new object[] { "a", "b" }, items);
        }

        [Fact]
        public void Enumerate_handles_an_Il2Cpp_style_list_that_is_not_IEnumerable()
        {
            // The exact shape that made every product lookup silently return empty on IL2CPP.
            var list = new Il2CppStyleList();
            list.Add("a");
            list.Add("b");

            Assert.IsNotAssignableFrom<System.Collections.IEnumerable>(list);
            Assert.Equal(new object[] { "a", "b" }, Reflect.Enumerate(list).ToList());
        }

        [Fact]
        public void Enumerate_of_null_is_empty_not_a_crash()
        {
            Assert.Empty(Reflect.Enumerate(null));
        }

        [Fact]
        public void A_Guid_member_stringifies_to_the_save_file_form()
        {
            var guid = Guid.Parse("3059421d-3982-47e6-9984-b4b32e892489");
            var station = new GameStation { GUID = guid };

            Assert.Equal("3059421d-3982-47e6-9984-b4b32e892489", Reflect.AsString(Reflect.Get(station, "GUID")));
        }
    }

    [Collection("GameStatics")]
    public class ReflectionGameFactsTests : IDisposable
    {
        private static readonly Assembly[] Fakes = { typeof(GameStation).Assembly };

        private readonly GameProductDef _blueLightning = new GameProductDef
        {
            ID = "bluelightning",
            Name = "Blue Lightning",
            DrugType = ScheduleOne.Product.EDrugType.Marijuana,
            Properties =
            {
                new ScheduleOne.Product.PropertyItemDefinition { Name = "Energizing" },
                new ScheduleOne.Product.PropertyItemDefinition { Name = "Euphoric" }
            }
        };

        public ReflectionGameFactsTests()
        {
            GameProductManager.Instance = new GameProductManager();
            GameProductManager.Instance.AllProducts.Add(_blueLightning);
            GameTimeManager.Instance = new GameTimeManager { ElapsedDays = 40, CurrentTime = 924 };
            GamePlayer.PlayerList = new List<GamePlayer>();
            GamePlayer.Local = null;
        }

        public void Dispose()
        {
            GameProductManager.Instance = null;
            GameTimeManager.Instance = null;
            GamePlayer.PlayerList = new List<GamePlayer>();
            GamePlayer.Local = null;
        }

        private static ReflectionGameFacts Facts() => new ReflectionGameFacts(Fakes.ToList(), NullLog.Instance);

        [Fact]
        public void Reads_the_clock_for_the_idempotency_key()
        {
            var facts = Facts();
            Assert.Equal(40, facts.ElapsedDays);
            Assert.Equal(924, facts.TimeOfDay);
        }

        [Fact]
        public void Resolves_the_local_player_through_the_stations_NetworkObject()
        {
            // PlayerUserObject is a NetworkObject, not a Player — the first draft read PlayerCode
            // straight off it and always got null.
            var netObj = new FishNetObject();
            var local = new GamePlayer { PlayerCode = TestKit.SteamId, NetworkObject = netObj };
            GamePlayer.PlayerList.Add(local);
            GamePlayer.Local = local;

            var facts = Facts();

            Assert.True(facts.IsLocalPlayer(netObj));
            Assert.Equal(TestKit.SteamId, facts.GetPlayerCode(netObj));
        }

        [Fact]
        public void Another_players_NetworkObject_is_not_the_local_player()
        {
            var mine = new FishNetObject();
            var theirs = new FishNetObject();
            GamePlayer.Local = new GamePlayer { PlayerCode = TestKit.SteamId, NetworkObject = mine };
            var other = new GamePlayer { PlayerCode = "76561190000000002", NetworkObject = theirs };
            GamePlayer.PlayerList.Add(GamePlayer.Local);
            GamePlayer.PlayerList.Add(other);

            var facts = Facts();

            Assert.False(facts.IsLocalPlayer(theirs));
            Assert.Equal("76561190000000002", facts.GetPlayerCode(theirs));
        }

        [Fact]
        public void An_unknown_NetworkObject_resolves_to_nobody()
        {
            var facts = Facts();
            Assert.False(facts.IsLocalPlayer(new FishNetObject()));
            Assert.Null(facts.GetPlayerCode(new FishNetObject()));
        }

        [Fact]
        public void Resolves_a_known_output_product_via_IsOutputKnown()
        {
            var facts = Facts();
            var op = new GameMixOp { KnownOutput = _blueLightning };

            var id = facts.ResolveOutputProductId(op, "greencrack", "mouthwash");

            Assert.Equal("bluelightning", id);
            Assert.True(facts.IsProductDiscovered(id));
        }

        [Fact]
        public void An_undiscovered_mix_has_no_output_product_yet()
        {
            // Until the player names it via FinishAndNameMix the product does not exist. Reporting
            // a made-up id here would put phantom products in the statistics.
            var facts = Facts();
            var op = new GameMixOp { KnownOutput = null };

            var id = facts.ResolveOutputProductId(op, "greencrack", "mouthwash");

            Assert.Null(id);
            Assert.False(facts.IsProductDiscovered(id));
        }

        [Fact]
        public void Looks_up_display_name_drug_type_and_effects()
        {
            var facts = Facts();

            Assert.Equal("Blue Lightning", facts.GetProductDisplayName("bluelightning"));
            Assert.Equal("Marijuana", facts.GetDrugType("bluelightning"));
            Assert.Equal(new[] { "Energizing", "Euphoric" }, facts.ResolveEffects("bluelightning"));
        }

        [Fact]
        public void An_unknown_product_id_falls_back_to_the_id_itself()
        {
            var facts = Facts();
            Assert.Equal("mystery", facts.GetProductDisplayName("mystery"));
            Assert.Null(facts.ResolveEffects("mystery"));
        }

        [Fact]
        public void Station_guid_and_item_id_come_off_the_buildable()
        {
            var station = new GameStation
            {
                GUID = Guid.Parse("3059421d-3982-47e6-9984-b4b32e892489"),
                ItemInstance = new ScheduleOne.ItemFramework.ItemInstance { ID = "mixingstationmk2" }
            };

            var facts = Facts();

            Assert.Equal("3059421d-3982-47e6-9984-b4b32e892489", facts.GetStationGuid(station));
            Assert.Equal("mixingstationmk2", facts.GetStationItemId(station));
        }
    }

    [Collection("GameStatics")]
    public class MixingStationReaderTests : IDisposable
    {
        private static readonly Assembly[] Fakes = { typeof(GameStation).Assembly };

        private readonly GameProductDef _output = new GameProductDef
        {
            ID = "bluelightning",
            Name = "Blue Lightning",
            DrugType = ScheduleOne.Product.EDrugType.Marijuana,
            Properties = { new ScheduleOne.Product.PropertyItemDefinition { Name = "Energizing" } }
        };

        private readonly FishNetObject _localNetObj = new FishNetObject();

        public MixingStationReaderTests()
        {
            GameProductManager.Instance = new GameProductManager();
            GameProductManager.Instance.AllProducts.Add(_output);
            GameProductManager.Instance.AllProducts.Add(new GameProductDef
            {
                ID = "greencrack", Name = "Green Crack",
                DrugType = ScheduleOne.Product.EDrugType.Marijuana
            });
            GameTimeManager.Instance = new GameTimeManager { ElapsedDays = 40, CurrentTime = 924 };

            var local = new GamePlayer { PlayerCode = TestKit.SteamId, NetworkObject = _localNetObj };
            GamePlayer.PlayerList = new List<GamePlayer> { local };
            GamePlayer.Local = local;
        }

        public void Dispose()
        {
            GameProductManager.Instance = null;
            GameTimeManager.Instance = null;
            GamePlayer.PlayerList = new List<GamePlayer>();
            GamePlayer.Local = null;
        }

        private GameStation Station(bool localUser = true, bool npcUser = false, bool knownOutput = true) =>
            new GameStationMk2
            {
                GUID = Guid.Parse("3059421d-3982-47e6-9984-b4b32e892489"),
                ItemInstance = new ScheduleOne.ItemFramework.ItemInstance { ID = "mixingstationmk2" },
                CurrentMixOperation = new GameMixOp
                {
                    ProductID = "greencrack",
                    IngredientID = "mouthwash",
                    Quantity = 20,
                    ProductQuality = ScheduleOne.ItemFramework.EQuality.Premium,
                    KnownOutput = knownOutput ? _output : null
                },
                PlayerUserObject = localUser ? _localNetObj : null,
                NPCUserObject = npcUser ? new FishNetObject() : null
            };

        private static IGameFacts Facts() => new ReflectionGameFacts(Fakes.ToList(), NullLog.Instance);

        [Fact]
        public void Reads_a_complete_batch_off_a_live_station()
        {
            var candidate = MixingStationReader.Read(Station(), Facts());

            Assert.NotNull(candidate);
            Assert.Equal(ProductionKind.Mixed, candidate.Kind);
            Assert.Equal("greencrack", candidate.BaseProductId);
            Assert.Equal("mouthwash", candidate.IngredientId);
            Assert.Equal("bluelightning", candidate.OutputProductId);
            Assert.Equal("Blue Lightning", candidate.OutputProductName);
            Assert.Equal("Marijuana", candidate.DrugType);
            Assert.Equal(20, candidate.Quantity);
            Assert.Equal("Premium", candidate.Quality);   // enum name, not the ordinal
            Assert.Equal("3059421d-3982-47e6-9984-b4b32e892489", candidate.StationGuid);
            Assert.Equal("mixingstationmk2", candidate.StationItemId);
            Assert.Equal("MixingStationMk2", candidate.StationType);
            Assert.Equal(Attribution.Local, candidate.ResolveAttribution());
            Assert.Equal(TestKit.SteamId, candidate.ProducedByPlayerCode);
        }

        [Fact]
        public void An_undiscovered_mix_is_flagged_as_a_new_discovery()
        {
            var candidate = MixingStationReader.Read(Station(knownOutput: false), Facts());

            Assert.False(candidate.OutputWasAlreadyKnown);

            // No product exists until the player names it, so there is no id to record. Falling
            // back to the base product here was a real bug: it credited the units to the input and
            // produced "X + ingredient -> X" recipes that appear to do nothing.
            Assert.Null(candidate.OutputProductId);
            Assert.Null(candidate.OutputProductName);

            // The drug family is still known, because a mix never changes it.
            Assert.Equal("Marijuana", candidate.DrugType);
        }

        [Fact]
        public void An_employee_operated_station_is_attributed_to_the_employee()
        {
            var candidate = MixingStationReader.Read(Station(localUser: false, npcUser: true), Facts());
            Assert.Equal(Attribution.Employee, candidate.ResolveAttribution());
        }

        [Fact]
        public void A_cleared_operation_yields_nothing()
        {
            var station = Station();
            station.CurrentMixOperation = null;
            Assert.Null(MixingStationReader.Read(station, Facts()));
        }

        [Fact]
        public void The_event_key_is_stable_across_repeated_reads()
        {
            var a = MixingStationReader.Read(Station(), Facts()).BuildEventKey();
            var b = MixingStationReader.Read(Station(), Facts()).BuildEventKey();

            Assert.Equal(a, b);
            Assert.Equal("3059421d-3982-47e6-9984-b4b32e892489|greencrack+mouthwash|d40-924", a);
        }

        [Fact]
        public void Reading_a_station_and_tracking_it_produces_exactly_one_event()
        {
            // The full Phase 9 path minus Harmony: station -> candidate -> tracker -> event,
            // including the Mk2 base-call double fire.
            var tracker = TestKit.Tracker();
            var recorded = new List<ProductionEvent>();
            tracker.ProductionRecorded += recorded.Add;

            var ctx = TestKit.Context();
            tracker.Track(MixingStationReader.Read(Station(), Facts()), ctx);
            tracker.Track(MixingStationReader.Read(Station(), Facts()), ctx);

            Assert.Single(recorded);
            Assert.Equal(20, recorded[0].Quantity);
            Assert.Equal("greencrack>mouthwash", recorded[0].RecipeId);
            Assert.Equal("Blue Lightning", recorded[0].OutputProductName);
        }
    }
}
