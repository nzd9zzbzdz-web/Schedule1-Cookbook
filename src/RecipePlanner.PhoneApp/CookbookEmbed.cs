#if IL2CPP
using System;
using UnityEngine;
using UnityEngine.UI;
using RecipePlanner.UI;
using Il2CppScheduleOne.UI.Phone.ProductManagerApp;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// The Cookbook on the default (IL2CPP) branch: a panel inside the Products app rather than an
    /// app of its own.
    ///
    /// <b>Why this is not the same as the Mono build.</b> Every phone app derives from
    /// <c>App&lt;T&gt;</c>, and on IL2CPP a managed subclass of it cannot exist. Registering
    /// <c>CookbookApp</c> needs the IL2CPP class for <c>App&lt;CookbookApp&gt;</c>, and creating
    /// that class needs <c>CookbookApp</c> — each requires the other, so neither can be made. That
    /// is circular by construction, not a missing feature, and it was confirmed on a running game:
    /// <c>SmoothScroll</c> and <c>HoverGlow</c> inject without complaint because they derive from
    /// plain <c>MonoBehaviour</c>; only the one over a generic base fails.
    ///
    /// So nothing here subclasses or injects anything. The panel is built out of the game's own
    /// components inside an app that already exists, which needs no new type at all.
    ///
    /// The trade is a button instead of a home-screen icon. Everything behind it —
    /// <see cref="CookbookScreen"/>, all of it — is the same code the Mono build runs.
    ///
    /// <b>Nothing the game owns is modified.</b> No component is removed, no object destroyed, no
    /// field overwritten. Two children are added to the app's container and the game's own children
    /// are left untouched and running. Uninstalling the mod leaves the Products app exactly as it
    /// was, which matters more here than in the Mono build: this one lives inside a screen the
    /// player depends on, so it has to be a guest rather than a tenant.
    /// </summary>
    internal static class CookbookEmbed
    {
        private const string PanelName = "CookbookPanel";
        private const string ButtonName = "CookbookButton";

        // Matched to CookbookScreen's palette by value rather than shared with it: those fields are
        // private to the screen and belong to it, and two constants are a smaller price than
        // widening the surface of a two-thousand-line file for a button.
        private static readonly Color Backdrop = new Color(0.024f, 0.035f, 0.028f, 1f);
        private static readonly Color Neon = new Color(0.24f, 0.92f, 0.44f, 1f);
        private static readonly Color OnNeon = new Color(0.02f, 0.06f, 0.03f, 1f);

        private static CookbookScreen _screen;
        private static GameObject _panel;
        private static GameObject _button;

        /// <summary>
        /// True while the panel is built and still attached. Checked against the live objects
        /// rather than a flag, because the phone is rebuilt on every save load and a flag would
        /// claim an installation that no longer exists.
        /// </summary>
        internal static bool IsInstalled =>
            _panel != null && _button != null && _panel.transform.parent != null;

        /// <summary>
        /// Adds the button and builds the panel. Safe to call repeatedly; returns false while the
        /// Products app does not exist yet, which is normal before a save has finished loading.
        /// </summary>
        internal static bool TryInstall()
        {
            if (IsInstalled) return true;

            var container = FindProductsContainer();
            if (container == null) return false;

            // A reload leaves stale managed references pointing at destroyed objects, and a rebuilt
            // phone can also carry our own children if the app object itself survived. Both are
            // cleared by name before anything is built, so a second save cannot end up with two
            // buttons stacked on top of each other.
            Forget();
            RemoveExisting(container, PanelName);
            RemoveExisting(container, ButtonName);

            try
            {
                _panel = BuildPanel(container);
                _button = BuildButton(container);
                RecipePlannerUI.Log?.Info(
                    "Cookbook panel added to the Products app. On this branch it opens from a " +
                    "button rather than its own icon — see docs/08-IL2CPP-PLAN.md.");
                return true;
            }
            catch (Exception ex)
            {
                // Leave nothing half-built inside an app the player uses for pricing.
                RecipePlannerUI.Log?.Error("Could not add the Cookbook panel: " + ex);
                Cleanup();
                return false;
            }
        }

        /// <summary>
        /// The Products app's own content root — the same <c>appContainer</c> the Mono build draws
        /// into, reached through the live component rather than by name.
        /// </summary>
        private static RectTransform FindProductsContainer()
        {
            try
            {
                var app = ProductManagerApp.Instance;
                if (app == null) return null;

                // appContainer is declared on App<T>, which this cannot name as a type — but the
                // instance is a real object and the property is public, so it reads normally.
                return app.appContainer;
            }
            catch (Exception ex)
            {
                RecipePlannerUI.Log?.Warn("Products app not readable yet: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The full-bleed panel the cookbook is drawn into, added last so it paints over the
        /// game's own content, and opaque so that content cannot show through.
        ///
        /// Covering rather than hiding is deliberate. Deactivating the game's children would work
        /// and would be a change to objects we do not own — and the Products app has to come back
        /// intact the moment the panel closes.
        /// </summary>
        private static GameObject BuildPanel(RectTransform container)
        {
            var go = UiInterop.NewRect(PanelName);
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(container, false);

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // Opaque, and a raycast target so clicks cannot reach the product list underneath.
            // Without this the panel is visually on top but input still lands on whatever the
            // game drew below it, which reads as the app responding to phantom clicks.
            var backdrop = go.AddComponent<Image>();
            backdrop.color = Backdrop;
            backdrop.raycastTarget = true;

            go.SetActive(false);
            _screen = CookbookScreen.CreateInto(rect);
            return go;
        }

        /// <summary>
        /// The button that opens the panel. Pinned to the container's top-right and kept as the
        /// last sibling so the Products app's own layout cannot push it off or draw over it.
        /// </summary>
        private static GameObject BuildButton(RectTransform container)
        {
            var go = UiInterop.NewRect(ButtonName);
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(container, false);

            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-16f, -16f);
            rect.sizeDelta = new Vector2(150f, 44f);

            var body = go.AddComponent<Image>();
            body.sprite = UiSkin.Pill;
            body.type = Image.Type.Sliced;
            body.color = Neon;

            var button = go.AddComponent<Button>();
            button.targetGraphic = body;
            UiInterop.OnClick(button, Toggle);

            var label = UiInterop.NewRect("Label");
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = label.AddComponent<Text>();
            text.text = "COOKBOOK";
            text.alignment = TextAnchor.MiddleCenter;
            text.color = OnNeon;
            text.raycastTarget = false;
            text.font = CookbookScreen.ResolveFont(container);
            text.fontSize = 15;
            text.fontStyle = FontStyle.Bold;

            return go;
        }

        /// <summary>Opens or closes the panel, refreshing it from the live data on the way in.</summary>
        private static void Toggle()
        {
            if (_panel == null) return;

            var opening = !_panel.activeSelf;
            _panel.SetActive(opening);

            // Kept above the panel: the panel is opaque and both are children of the same
            // container, so without this the button is painted over the moment it is used.
            if (_button != null) _button.transform.SetAsLastSibling();

            if (!opening) return;

            try { _screen?.Refresh(); }
            catch (Exception ex)
            {
                // A refresh failure must never leave the player stuck on a dead panel inside an app
                // they were using for something else.
                RecipePlannerUI.Log?.Error("Cookbook refresh failed: " + ex);
                _panel.SetActive(false);
            }
        }

        /// <summary>Called when a save unloads, so nothing points at a destroyed phone.</summary>
        internal static void Forget()
        {
            _screen = null;
            _panel = null;
            _button = null;
        }

        private static void Cleanup()
        {
            try
            {
                if (_panel != null) UnityEngine.Object.Destroy(_panel);
                if (_button != null) UnityEngine.Object.Destroy(_button);
            }
            catch { /* already gone */ }
            Forget();
        }

        /// <summary>
        /// Drops a previous run's children by name. Only ever removes objects this file created —
        /// the names are ours and nothing in the game uses them.
        /// </summary>
        private static void RemoveExisting(RectTransform container, string name)
        {
            try
            {
                var existing = container.Find(name);
                if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
            }
            catch { /* nothing there, which is the normal case */ }
        }
    }
}
#endif
