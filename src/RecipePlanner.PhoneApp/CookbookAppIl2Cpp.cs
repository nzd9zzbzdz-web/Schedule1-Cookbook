#if IL2CPP
using System;
using UnityEngine;
using RecipePlanner.UI;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI.Phone.ProductManagerApp;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// A real Cookbook app on the phone, on the IL2CPP branch.
    ///
    /// <b>Why it derives from ProductManagerApp.</b> Every phone app is an <c>App&lt;T&gt;</c>, and
    /// <c>App&lt;CookbookApp&gt;</c> cannot exist here — building that class needs
    /// <c>CookbookApp</c>, and registering <c>CookbookApp</c> needs that class. But
    /// <c>ProductManagerApp</c> is a <b>concrete</b> class the game already created, so deriving
    /// from it is the same shape as <c>SmoothScroll : MonoBehaviour</c>. Proven on a running game
    /// before any of this was written: the injector accepted exactly this subclass.
    ///
    /// Being a ProductManagerApp is inheritance, not identity. It is a base class chosen because it
    /// is the one concrete phone app available to derive from, and everything it does for itself is
    /// covered over.
    ///
    /// <b>The singleton is borrowed and given straight back.</b> <c>PlayerSingleton.Awake</c>
    /// assigns <c>Instance</c>, so this object taking it would leave the player's real Products app
    /// unreachable — the screen they price everything from. It is captured before the base runs and
    /// restored immediately after, in both Awake and Start, because either can assign it.
    ///
    /// If any of that goes wrong the installer falls back to <see cref="CookbookEmbed"/>, which
    /// needs no subclassing at all and is known to work.
    /// </summary>
    internal sealed class CookbookAppIl2Cpp : ProductManagerApp
    {
        internal const string Title = "Cookbook";

        /// <summary>Required by the injector, which constructs from a native pointer.</summary>
        public CookbookAppIl2Cpp(IntPtr pointer) : base(pointer) { }

        private CookbookScreen _screen;

        /// <summary>
        /// The real Products app, captured the moment before a base call can overwrite it.
        ///
        /// Static rather than per-instance: the value being protected is itself static, and this
        /// has to survive our object being destroyed and rebuilt with the phone.
        /// </summary>
        private static ProductManagerApp _realApp;

        public override void Awake()
        {
            Remember();
            try { base.Awake(); }
            finally { Restore(); }

            try
            {
                AppName = Title;
                IconLabel = Title;
                AvailableInTutorial = false;
                AppIcon = AppIconFactory.Cookbook();
            }
            catch (Exception ex)
            {
                RecipePlannerUI.Log?.Warn("Cookbook app configuration failed: " + ex.Message);
            }
        }

        public override void Start()
        {
            Remember();
            try { base.Start(); }
            finally { Restore(); }
        }

        public override void SetOpen(bool open)
        {
            // Restored around this one too. ProductManagerApp.SetOpen reads Instance for its own
            // purposes, and opening our app must not be the moment the player's Products app
            // quietly becomes ours.
            Remember();
            try { base.SetOpen(open); }
            finally { Restore(); }

            if (!open) return;

            try
            {
                EnsureScreen();
                _screen?.Refresh();
            }
            catch (Exception ex)
            {
                // A UI failure must never take the phone, or the game, down with it.
                RecipePlannerUI.Log?.Error("Cookbook refresh failed: " + ex);
            }
        }

        /// <summary>
        /// Notes which object currently owns the singleton, unless that is already us.
        ///
        /// The guard matters on the second and later calls: once our own Awake has run, reading
        /// Instance could return this object, and remembering ourselves as "the real app" would
        /// make every later restore a no-op that quietly leaves the theft in place.
        /// </summary>
        private void Remember()
        {
            try
            {
                var current = PlayerSingleton<ProductManagerApp>.instance;
                if (current != null && current.Pointer != Pointer) _realApp = current;
            }
            catch { /* nothing to remember */ }
        }

        private void Restore()
        {
            try
            {
                if (_realApp != null) PlayerSingleton<ProductManagerApp>.instance = _realApp;
            }
            catch (Exception ex)
            {
                // Worth shouting about: this is the one failure that damages the player's game
                // rather than merely denying them ours.
                RecipePlannerUI.Log?.Error(
                    "Could not hand the Products app singleton back. The in-game Products screen " +
                    "may misbehave until the save is reloaded. " + ex.Message);
            }
        }

        /// <summary>
        /// Builds the cookbook into this app's container, hiding the product UI cloned along with
        /// it. Deactivated rather than destroyed — destroying took the panel background with it and
        /// the app opened see-through onto the game world.
        /// </summary>
        private void EnsureScreen()
        {
            if (_screen != null) return;

            var container = appContainer;
            if (container == null)
            {
                RecipePlannerUI.Log?.Error(
                    "Cookbook: appContainer is null on the cloned app, so there is nowhere to build.");
                return;
            }

            for (var i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (child != null) child.gameObject.SetActive(false);
            }

            _screen = CookbookScreen.CreateInto(container);
            RecipePlannerUI.Log?.Info("Cookbook screen built inside its own app on IL2CPP.");
        }

        /// <summary>Dropped when the phone is rebuilt, so nothing points at a destroyed screen.</summary>
        internal static void Forget() => _realApp = null;
    }
}
#endif
