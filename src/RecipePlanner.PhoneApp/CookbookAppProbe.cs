#if IL2CPP
using System;
using RecipePlanner.UI;
using Il2CppScheduleOne.UI.Phone.ProductManagerApp;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// Asks one question: can a managed phone app be injected on IL2CPP after all?
    ///
    /// <c>CookbookApp : App&lt;CookbookApp&gt;</c> is impossible here, because that generic
    /// instantiation does not exist and cannot be built without the type it is parameterised by.
    /// But <c>ProductManagerApp</c> is a <b>concrete</b> IL2CPP class that the game already
    /// created — deriving from it is the same shape as <c>SmoothScroll : MonoBehaviour</c>, which
    /// injects without complaint. If that works, a real app icon is back on the table and the
    /// button inside the Products app stops being the only option.
    ///
    /// <b>This registers the type and nothing else.</b> No GameObject, no component, no touching
    /// the player's Products app. Registration is the step that either works or does not, so it is
    /// the only step worth taking until the answer is known — and taking only that step means a
    /// failure costs nothing at all.
    ///
    /// The dangerous part comes later, if this succeeds: <c>PlayerSingleton</c> assigns
    /// <c>Instance</c> in Awake, so a live instance of this would take the singleton away from the
    /// real Products app. That is a screen the player depends on for pricing, and it is why none of
    /// this is instantiated on a hunch.
    /// </summary>
    internal sealed class CookbookAppProbe : ProductManagerApp
    {
        /// <summary>Required by the injector, which constructs from a native pointer.</summary>
        public CookbookAppProbe(IntPtr pointer) : base(pointer) { }

        private static bool _asked;

        /// <summary>
        /// Reports whether injecting a phone-app subclass succeeds. Runs once, and is deliberately
        /// only ever asked — never acted upon here.
        /// </summary>
        internal static void Ask()
        {
            if (_asked) return;
            _asked = true;

            try
            {
                Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<CookbookAppProbe>();
                RecipePlannerUI.Log?.Info(
                    "PROBE: a phone-app subclass CAN be injected on IL2CPP (base ProductManagerApp). " +
                    "A standalone Cookbook app icon is achievable on this branch.");
            }
            catch (Exception ex)
            {
                var deepest = ex;
                while (deepest.InnerException != null) deepest = deepest.InnerException;

                RecipePlannerUI.Log?.Info(
                    "PROBE: a phone-app subclass cannot be injected on IL2CPP even over a concrete " +
                    "base, so the in-Products panel stays the only option. " +
                    deepest.GetType().Name + ": " + deepest.Message);
            }
        }
    }
}
#endif
