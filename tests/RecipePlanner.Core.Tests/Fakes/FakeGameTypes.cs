// Stand-ins mirroring the REAL Schedule I types, verified against the shipped Assembly-CSharp with
// tools/HookVerifier. Shapes are copied exactly — including the counter-intuitive ones — because a
// fake that is "tidier" than the game proves nothing:
//
//   * MixingStation.PlayerUserObject is a FishNet NetworkObject, NOT a Player.
//   * BuildableItem.GUID is a System.Guid, not a string.
//   * MixOperation.GetOutput(List) returns an EDrugType, not a product.
//   * MixOperation.IsOutputKnown(out ProductDefinition) is how the output product is obtained.
//   * MixingStationMk2 OVERRIDES MixingDone.
//
// If the game renames or reshapes a member, fix it in HookTable AND here.

using System;
using System.Collections.Generic;

namespace FishNet.Object
{
    /// <summary>Stand-in for the FishNet NetworkObject that stations hold as their user.</summary>
    public class NetworkObject { }

    public class NetworkBehaviour
    {
        public NetworkObject NetworkObject { get; set; }
    }
}

namespace ScheduleOne.ItemFramework
{
    public class ItemInstance
    {
        public string ID { get; set; }
    }

    public enum EQuality { Trash, Poor, Standard, Premium, Heavenly }
}

namespace ScheduleOne.Product
{
    public enum EDrugType { Marijuana, Methamphetamine, Cocaine, MDMA, Shrooms, Heroin }

    public class PropertyItemDefinition
    {
        public string ID { get; set; }
        public string Name { get; set; }
    }

    public class ProductDefinition
    {
        public string ID { get; set; }
        public string Name { get; set; }
        public EDrugType DrugType { get; set; }
        public List<PropertyItemDefinition> Properties { get; set; } = new List<PropertyItemDefinition>();
    }

    public class ProductManager
    {
        public static ProductManager Instance { get; set; }

        public List<ProductDefinition> AllProducts { get; set; } = new List<ProductDefinition>();
        public List<string> DiscoveredProducts { get; set; } = new List<string>();
        public List<MixRecipeData> mixRecipes { get; set; } = new List<MixRecipeData>();

        public object WeedMixMap, MethMixMap, CokeMixMap, ShroomMixMap;
        public object onMixRecipeAdded, onNewProductCreated, onProductDiscovered;

        public object GetMixerMap(EDrugType type) => null;
        public float CalculateProductValue(object a, object b) => 0f;
        public void DiscoverProduct(string productID) { }
    }

    public class MixRecipeData
    {
        public string Product;
        public string Mixer;
        public string Output;
    }
}

namespace ScheduleOne.GameTime
{
    public class TimeManager
    {
        public static TimeManager Instance { get; set; }
        public int ElapsedDays { get; set; }

        // The runtime property is CurrentTime (924 = 09:24). There is deliberately NO TimeOfDay
        // member here — that name exists only as a save-file JSON key, and reading it returned
        // null in the live game, collapsing the idempotency key. The fake must not be kinder
        // than the real thing.
        public int CurrentTime { get; set; }
        public float NormalizedTimeOfDay => CurrentTime / 2400f;
    }
}

namespace ScheduleOne.EntityFramework
{
    public class BuildableItem : FishNet.Object.NetworkBehaviour
    {
        public Guid GUID { get; set; }
        public ScheduleOne.ItemFramework.ItemInstance ItemInstance { get; set; }
    }

    public class GridItem : BuildableItem { }
}

namespace ScheduleOne.ObjectScripts
{
    using ScheduleOne.ItemFramework;
    using ScheduleOne.Product;

    public class MixOperation
    {
        public string ProductID;
        public EQuality ProductQuality;
        public string IngredientID;
        public int Quantity;

        /// <summary>Set by the test to control what the mix resolves to.</summary>
        public ProductDefinition KnownOutput;

        public EDrugType GetOutput(List<PropertyItemDefinition> properties) => EDrugType.Marijuana;

        public bool IsOutputKnown(out ProductDefinition knownProduct)
        {
            knownProduct = KnownOutput;
            return KnownOutput != null;
        }
    }

    public class MixingStation : ScheduleOne.EntityFramework.GridItem
    {
        public MixOperation CurrentMixOperation { get; set; }
        public float CurrentMixTime { get; set; }
        public bool IsMixingDone => CurrentMixTime <= 0f;

        public FishNet.Object.NetworkObject PlayerUserObject { get; set; }
        public FishNet.Object.NetworkObject NPCUserObject { get; set; }

        public virtual void MixingDone() { }
        public int GetMixQuantity() => CurrentMixOperation?.Quantity ?? 0;
        public ProductDefinition GetProduct() => null;
        public PropertyItemDefinition GetMixer() => null;
    }

    /// <summary>The Mk2 trap: overrides MixingDone (audit §2.1 caveat 2).</summary>
    public class MixingStationMk2 : MixingStation
    {
        public override void MixingDone() => base.MixingDone();
    }
}

namespace ScheduleOne.PlayerScripts
{
    public class Player : FishNet.Object.NetworkBehaviour
    {
        public static Player Local;
        public static List<Player> PlayerList = new List<Player>();
        public string PlayerCode { get; set; }
        public bool IsLocalPlayer => ReferenceEquals(this, Local);
    }
}

namespace ScheduleOne.Persistence
{
    public class SaveInfo
    {
        public string SavePath;
        public int SaveSlotNumber;
        public string OrganisationName;
        public DateTime DateCreated;
    }

    public class LoadManager
    {
        public SaveInfo ActiveSaveInfo { get; set; }
        public string LoadedGameFolderPath { get; set; }
        public bool IsGameLoaded { get; set; }
    }
}

namespace ScheduleOne.Networking
{
    public class Lobby
    {
        public static Lobby Instance { get; set; }
        public bool IsHost { get; set; }
        public int PlayerCount { get; set; } = 1;
        public bool IsInLobby { get; set; }
    }
}

// ---- IL2CPP proxy shape: Il2CppInterop prefixes every namespace with "Il2Cpp" and its
// collections do not reliably implement System.Collections.IEnumerable. ----
namespace Il2CppScheduleOne.ObjectScripts
{
    public class MixingStation
    {
        public object CurrentMixOperation { get; set; }
        public object PlayerUserObject { get; set; }
        public object NPCUserObject { get; set; }
        public void MixingDone() { }
        public int GetMixQuantity() => 0;
        public object GetProduct() => null;
        public object GetMixer() => null;
    }
}

namespace RecipePlanner.Core.Tests.Fakes
{
    /// <summary>
    /// Indexable collection that deliberately does NOT implement IEnumerable, matching the
    /// Il2CppSystem.Collections.Generic.List proxy that broke the first draft of the product lookup.
    /// </summary>
    public sealed class Il2CppStyleList
    {
        private readonly List<object> _items = new List<object>();
        public int Count => _items.Count;
        public object get_Item(int index) => _items[index];
        public void Add(object item) => _items.Add(item);
    }
}
