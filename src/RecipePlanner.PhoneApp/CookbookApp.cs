using System;
using UnityEngine;
using ScheduleOne.UI;
using RecipePlanner.UI;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// The Cookbook app on the in-game phone.
    ///
    /// Deliberately a NEW app rather than a replacement for the game's ProductManagerApp: that one
    /// owns product listing and pricing — real functionality the player depends on — and overriding
    /// a live game UI is the most update-fragile thing this mod could do. A new app cannot break
    /// anything, and it inherits the native look by cloning an existing app's prefab rather than
    /// being styled from scratch.
    ///
    /// <see cref="App{T}"/> is generic (CRTP), which is exactly the shape Il2CppInterop handles
    /// worst — hence the Mono branch.
    ///
    /// Contract confirmed against the shipped Assembly-CSharp:
    /// <c>CookbookApp : App&lt;T&gt; : PlayerSingleton&lt;T&gt;</c>. Start is <b>protected</b> virtual,
    /// SetOpen is public virtual, and Awake is inherited from PlayerSingleton rather than declared
    /// on App — so the singleton registration in the base must not be skipped.
    /// </summary>
    public class CookbookApp : App<CookbookApp>
    {
        public const string Title = "Cookbook";

        private CookbookScreen _cookbookScreen;
        private bool _configured;

        /// <summary>
        /// Awake comes from PlayerSingleton<T>, two levels up — App<T> itself declares none. The
        /// singleton registration in the base MUST still run, so base.Awake() is called after our
        /// fields are set, since Start builds the home-screen icon from them.
        /// </summary>
        protected override void Awake()
        {
            Configure();
            base.Awake();
        }

        private void Configure()
        {
            if (_configured) return;
            _configured = true;

            AppName = Title;
            IconLabel = Title;
            AvailableInTutorial = false;

            // The wiring restored from the template carries the Products sprite; replace it so the
            // phone does not end up with two identical icons.
            AppIcon = AppIconFactory.Cookbook();
        }

        protected override void Start()
        {
            Configure();
            base.Start();
        }

        public override void SetOpen(bool open)
        {
            base.SetOpen(open);
            if (!open) return;

            try
            {
                EnsureScreen();
                _cookbookScreen?.Refresh();
            }
            catch (Exception ex)
            {
                // A UI failure must never take the phone — or the game — down with it.
                RecipePlannerUI.Log?.Error("Cookbook refresh failed: " + ex);
            }
        }

        private void EnsureScreen()
        {
            if (_cookbookScreen != null) return;

            // Diagnostics rather than another guess: three rounds of "fix the rendering" failed
            // because the actual state of the container was never established.
            if (appContainer == null)
            {
                RecipePlannerUI.Log?.Error(
                    "Cookbook: appContainer is NULL — the template wiring did not transfer, so " +
                    "there is nowhere to build the screen.");
                DumpHierarchy();
                return;
            }

            RecipePlannerUI.Log?.Info(
                $"Cookbook: container '{appContainer.name}' size={appContainer.rect.width}x{appContainer.rect.height} " +
                $"children={appContainer.childCount} activeInHierarchy={appContainer.gameObject.activeInHierarchy} " +
                $"scale={appContainer.lossyScale.x:0.###}");

            // Deactivate the template's content rather than destroying it. Destroying took the
            // panel background with it and the app opened see-through onto the game world.
            // Deactivating also keeps the originals around if a later version wants to reuse one.
            for (var i = 0; i < appContainer.childCount; i++)
            {
                var child = appContainer.GetChild(i);
                if (child != null) child.gameObject.SetActive(false);
            }

            _cookbookScreen = CookbookScreen.CreateInto(appContainer);

            RecipePlannerUI.Log?.Info(
                $"Cookbook: screen built, container now has {appContainer.childCount} children.");
        }

        /// <summary>
        /// Prints the app's hierarchy so a missing container can be traced to the actual object
        /// names, rather than being guessed at from the outside.
        /// </summary>
        private void DumpHierarchy()
        {
            try
            {
                var sb = new System.Text.StringBuilder("Cookbook hierarchy:\n");
                Walk(transform, 0, sb, 3);
                RecipePlannerUI.Log?.Info(sb.ToString().TrimEnd());
            }
            catch { /* diagnostics must never make things worse */ }
        }

        private static void Walk(Transform t, int depth, System.Text.StringBuilder sb, int maxDepth)
        {
            if (t == null || depth > maxDepth) return;

            var rect = t as RectTransform;
            sb.Append(' ', depth * 2)
              .Append(t.name)
              .Append(rect != null ? $"  [{rect.rect.width:0}x{rect.rect.height:0}]" : "")
              .Append(t.gameObject.activeSelf ? "" : "  (inactive)")
              .Append('\n');

            for (var i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb, maxDepth);
        }
    }
}
