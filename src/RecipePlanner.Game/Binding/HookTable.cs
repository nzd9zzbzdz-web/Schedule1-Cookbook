using System.Collections.Generic;

namespace RecipePlanner.Game.Binding
{
    /// <summary>
    /// One game type we depend on, plus the exact members we touch.
    ///
    /// Every entry was confirmed to exist in Schedule I 0.4.5f2 by dumping global-metadata.dat —
    /// see docs/00-PHASE-0-AUDIT.md. This table is the machine-readable form of that audit, and
    /// SymbolGuard checks it against the running game before a single patch is applied.
    /// </summary>
    public sealed class HookDefinition
    {
        public string TypeName { get; set; }
        public string Purpose { get; set; }
        public string[] Methods { get; set; } = new string[0];
        public string[] Members { get; set; } = new string[0];

        /// <summary>
        /// Optional entries do not fail verification when absent — used for types that may not
        /// exist on every branch or version (station MkN variants, future drug types).
        /// </summary>
        public bool Optional { get; set; }

        public override string ToString() => TypeName;
    }

    public static class HookTable
    {
        public const string VerifiedAgainstGameVersion = "0.4.5f2";

        // ---- namespaces ----
        public const string NsObjects = "ScheduleOne.ObjectScripts.";
        public const string NsProduct = "ScheduleOne.Product.";
        public const string NsPlayer = "ScheduleOne.PlayerScripts.";
        public const string NsPersist = "ScheduleOne.Persistence.";
        public const string NsNet = "ScheduleOne.Networking.";

        // ---- the primary production hook ----
        public const string MixingStation = NsObjects + "MixingStation";
        public const string MixingStationMk2 = NsObjects + "MixingStationMk2";
        public const string MixOperation = NsObjects + "MixOperation";
        public const string MixingDone = "MixingDone";

        public static IReadOnlyList<HookDefinition> All => Definitions;

        private static readonly HookDefinition[] Definitions =
        {
            // ================= identity =================
            new HookDefinition
            {
                TypeName = NsPlayer + "Player",
                Purpose = "Local player identity and attribution (audit §1.2)",
                Members = new[] { "Local", "PlayerList", "PlayerCode", "IsLocalPlayer" }
            },
            new HookDefinition
            {
                TypeName = NsPersist + "LoadManager",
                Purpose = "Save load lifecycle; gates the whole tracker (audit §1.2)",
                Members = new[] { "ActiveSaveInfo", "LoadedGameFolderPath", "IsGameLoaded" }
            },
            new HookDefinition
            {
                TypeName = NsPersist + "SaveInfo",
                Purpose = "Profile key components (audit §1.3)",
                Members = new[] { "SavePath", "SaveSlotNumber", "OrganisationName", "DateCreated" }
            },
            new HookDefinition
            {
                TypeName = NsNet + "Lobby",
                Purpose = "Host detection; ServerRpc bodies only run on the host (audit §4)",
                Members = new[] { "IsHost" },
                Optional = true
            },

            // ================= production: mixing =================
            new HookDefinition
            {
                TypeName = MixingStation,
                Purpose = "PRIMARY production hook (audit §2.1)",
                Methods = new[] { MixingDone, "GetMixQuantity", "GetProduct", "GetMixer" },
                Members = new[] { "CurrentMixOperation", "PlayerUserObject", "NPCUserObject" }
            },
            new HookDefinition
            {
                TypeName = MixingStationMk2,
                Purpose = "Overrides MixingDone — must be patched separately (audit §2.1 caveat 2)",
                Methods = new[] { MixingDone },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = MixOperation,
                Purpose = "The batch itself, and how its product is resolved (audit §2.1)",
                Methods = new[] { "IsOutputKnown", "GetOutput" },
                Members = new[] { "ProductID", "ProductQuality", "IngredientID", "Quantity" }
            },

            // ================= production: cooking =================
            // The oven has NO single completion method. Both the player path
            // (SmashLabOvenTask.Shatter) and the employee path (FinishLabOvenBehaviour) add the
            // product to OutputSlot themselves and then call SendCookOperation(null) to clear the
            // operation. Those are the only two sites in the assembly that pass null, and neither
            // cancel nor start does — which makes it the completion signal. CurrentOperation must
            // be read in a PREFIX, because the call is what clears it.
            //
            // Earlier revisions of this table claimed CreateStationItems was the production hook.
            // It is not: it instantiates the cosmetic tray/liquid props and creates no inventory.
            new HookDefinition
            {
                TypeName = NsObjects + "LabOven",
                Purpose = "Meth cook completion — SendCookOperation(null) (audit §2.2)",
                Methods = new[] { "SendCookOperation", "IsReadyForHarvest" },
                Members = new[] { "CurrentOperation", "OutputSlot", "PlayerUserObject", "NPCUserObject" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = NsObjects + "OvenCookOperation",
                Purpose = "Oven batch; units = Cookable.ProductQuantity * IngredientQuantity (audit §2.2)",
                Members = new[] { "IngredientID", "IngredientQuality", "IngredientQuantity", "ProductID", "Cookable" },
                Optional = true
            },
            // FinalizeOperation is the Observers RPC *writer*; the item is created in the generated
            // RpcLogic___ body, which runs exactly once per machine per completion. Patching the
            // writer would miss clients entirely and the logic nulls CurrentCookOperation, so the
            // operation has to be read before it runs.
            new HookDefinition
            {
                TypeName = NsObjects + "ChemistryStation",
                Purpose = "Chemistry completion — RpcLogic___FinalizeOperation (audit §2.3)",
                Methods = new[] { "FinalizeOperation", "RpcLogic___FinalizeOperation_2166136261" },
                Members = new[] { "CurrentCookOperation", "OutputSlot" },
                Optional = true
            },
            // Same shape as chemistry. Output is fixed: CocaineBaseDefinition x10 at InputQuality.
            new HookDefinition
            {
                TypeName = NsObjects + "Cauldron",
                Purpose = "Cocaine base completion — RpcLogic___FinishCookOperation (audit §2.4)",
                Methods = new[] { "FinishCookOperation", "RpcLogic___FinishCookOperation_2166136261" },
                Members = new[] { "InputQuality", "OutputSlot" },
                Optional = true
            },

            // ================= product data & discovery =================
            new HookDefinition
            {
                TypeName = NsProduct + "ProductManager",
                Purpose = "Discovery events, mix maps, valuation (audit §2.6/§2.7)",
                Methods = new[] { "GetMixerMap", "CalculateProductValue", "DiscoverProduct" },
                Members = new[]
                {
                    "onMixRecipeAdded", "onNewProductCreated", "onProductDiscovered",
                    "DiscoveredProducts", "WeedMixMap", "MethMixMap", "CokeMixMap", "ShroomMixMap"
                }
            },
            new HookDefinition
            {
                TypeName = NsProduct + "ProductDefinition",
                Purpose = "Pricing inputs (audit §2.9)",
                Members = new[] { "BasePrice", "MarketValue", "DrugType" },
                Optional = true
            },

            // ================= pricing =================
            // These were reached by reflection but never verified, which left the mod's headline
            // safety property with a hole: SymbolGuard could report 13/13 PASSED while every money
            // figure silently read $0, because GamePriceSource swallows its own failures and logs
            // the result at Info level.
            //
            // All three are Optional on purpose. Prices are a display concern; production tracking
            // does not need them, and disabling the tracker over a renamed price field would be a
            // far worse failure than showing no money. Optional means these degrade to a warning
            // and the operator is told, which is the whole point.
            new HookDefinition
            {
                TypeName = NsProduct + "ProductManager",
                Purpose = "Product prices — absence means every value reads $0 (release roadmap R5)",
                Members = new[] { "ProductPrices", "AllProducts" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = "ScheduleOne.Registry",
                Purpose = "Ingredient costs — absence means every cost reads $0, so profit == revenue",
                Members = new[] { "Instance", "ItemDictionary", "ItemRegistry" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = "ScheduleOne.ItemFramework.StorableItemDefinition",
                Purpose = "Per-ingredient shop price (audit §2.9)",
                Members = new[] { "BasePurchasePrice" },
                Optional = true
            },

            // ================= mixing guide =================
            // Everything the "what does this ingredient do?" chart reads. Optional throughout: the
            // guide is a reference screen, and losing it should cost the player a screen rather
            // than their production tracking.
            //
            // Listed here rather than reached quietly, which is the lesson from the pricing hole —
            // reflection the symbol check cannot see fails silently, and a mixing chart that has
            // gone silently wrong is worse than one that is missing.
            new HookDefinition
            {
                TypeName = NsProduct + "ProductManager",
                Purpose = "Mixable ingredient list for the mixing guide",
                Members = new[] { "ValidMixIngredients" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = "ScheduleOne.Registry",
                Purpose = "Ingredient lookup by id for the mixing guide",
                Methods = new[] { "GetItem" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = "ScheduleOne.Product.PropertyItemDefinition",
                Purpose = "The effects an item carries — both products and mixers (audit §2.7)",
                Members = new[] { "Properties" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = "ScheduleOne.Effects.Effect",
                Purpose = "Effect identity, value, colour, and how it shifts the mix map",
                Members = new[]
                {
                    "ID", "Name", "Tier", "Addictiveness", "LabelColor",
                    "ValueChange", "ValueMultiplier", "MixDirection", "MixMagnitude"
                },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = "ScheduleOne.Effects.MixMaps.MixerMap",
                Purpose = "The per-drug-type effect map the guide is derived from",
                Methods = new[] { "GetEffectAtPoint" },
                Members = new[] { "MapRadius", "Effects" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = "ScheduleOne.Effects.MixMaps.MixerMapEffect",
                Purpose = "One effect's region on the map",
                Members = new[] { "Position", "Radius", "Property" },
                Optional = true
            },

            // ================= reflection that had no entry =================
            // These were all reached by reflection and none were verified — the same hole the
            // pricing path had, found by auditing every Reflect.Get(x, "Member") literal in
            // RecipePlanner.Game against the shipped assemblies rather than against memory.
            //
            // All Optional, deliberately, including the two that feed the idempotency key. A
            // Required entry disables tracking outright when it fails, and these were verified
            // statically but never on a running game — getting one wrong would take the mod down
            // for everyone. Optional still surfaces the break in the symbol check, which is the
            // whole point. Promote them once someone has confirmed them live.
            new HookDefinition
            {
                TypeName = "ScheduleOne.GameTime.TimeManager",
                Purpose = "Game clock — feeds the idempotency key that stops batches double-counting",
                Members = new[] { "CurrentTime", "ElapsedDays" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = "ScheduleOne.EntityFramework.BuildableItem",
                Purpose = "Station identity — the other half of the idempotency key (audit §5)",
                Members = new[] { "GUID", "ItemInstance" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = NsNet + "Lobby",
                Purpose = "Player count — decides whether an unobserved batch may be attributed locally",
                Members = new[] { "PlayerCount" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = NsPlayer + "Player",
                Purpose = "Identity match when resolving a station's user (inherited from NetworkBehaviour)",
                Members = new[] { "NetworkObject" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = NsProduct + "ProductManager",
                Purpose = "Learned mix recipes; empty at runtime until learned, hence the save-file fallback",
                Members = new[] { "mixRecipes" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = NsProduct + "MixRecipeData",
                Purpose = "One recorded recipe row: product + mixer -> output",
                Members = new[] { "Product", "Mixer", "Output" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = NsProduct + "ProductDefinition",
                Purpose = "Computed addictiveness, preferred over the base value",
                Methods = new[] { "GetAddictiveness" },
                Optional = true
            },
            new HookDefinition
            {
                TypeName = "ScheduleOne.Effects.Effect",
                Purpose = "Effect blurb shown in the mixing guide",
                Members = new[] { "Description" },
                Optional = true
            }
        };

        /// <summary>Definitions whose absence must disable the tracker outright.</summary>
        public static IEnumerable<HookDefinition> Required
        {
            get
            {
                foreach (var d in Definitions)
                    if (!d.Optional) yield return d;
            }
        }
    }
}
