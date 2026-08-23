using System;
using System.Collections.Generic;
using UnityEngine;
using ScheduleOne.UI;
using ScheduleOne.UI.Phone.ProductManagerApp;
using RecipePlanner.UI;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// Puts the Cookbook app onto the phone.
    ///
    /// A mod has no asset bundle, so rather than authoring a prefab the installer clones an
    /// existing app's GameObject: that inherits the phone's real chrome — canvas setup, scaling,
    /// screen wiring, icon prefab — and makes the result look native instead of bolted on.
    ///
    /// The clone is stripped of the original app's own component and given ours instead. Two
    /// things make that delicate, and both are handled below:
    ///
    ///   * <c>App&lt;T&gt;</c> derives from <c>PlayerSingleton&lt;T&gt;</c>, so cloning an ACTIVE
    ///     app would run the original component's Awake on the clone and let it overwrite the real
    ///     app's singleton. The source is therefore deactivated for the single frame of the clone.
    ///   * The clone carries the source app's children. They are cleared before our screen builds,
    ///     or the Cookbook would render on top of the product list.
    /// </summary>
    public static class CookbookAppInstaller
    {
        private static bool _installed;

        public static bool IsInstalled => _installed && CookbookApp.Instance != null;

        /// <summary>Idempotent: safe to call on every save load.</summary>
        public static bool TryInstall()
        {
            if (IsInstalled) return true;

            // Bind the sprite cache to the seam the data builder raises. Done here rather than in a
            // static initialiser so it is set exactly when this assembly is known to have loaded —
            // on the IL2CPP branch it never does, and the builder simply finds nothing bound.
            RecipePlannerUI.CacheInvalidated = IconSource.Clear;

            // Reaching here means IsInstalled was false, so any previous app object — and every
            // Image referencing these sprites — is already destroyed. Dropping the textures now is
            // therefore safe, and skipping it would leak a set per save load, since the phone is
            // rebuilt each time. See UiSkin.
            UiSkin.Clear();

            try
            {
                var source = FindTemplateApp();
                if (source == null)
                {
                    RecipePlannerUI.Log?.Warn("Phone app template not found yet — Cookbook not installed.");
                    return false;
                }

                var clone = CloneInactive(source);
                if (clone == null) return false;

                // AddComponent creates a FRESH component, so none of the template's serialized
                // wiring survives — icon, container, screen and notification refs would all be
                // null. Capture them before the template component is destroyed.
                var wiring = CaptureAppWiring(clone);

                StripSourceComponents(clone);

                clone.name = "CookbookApp";
                var app = clone.AddComponent<CookbookApp>();
                RestoreAppWiring(app, wiring);

                clone.SetActive(true);

                _installed = true;
                RecipePlannerUI.Log?.Info("Cookbook app installed on the phone.");
                return true;
            }
            catch (Exception ex)
            {
                RecipePlannerUI.Log?.Error("Could not install the Cookbook app: " + ex);
                return false;
            }
        }

        /// <summary>
        /// The product app is the closest structural match — a scrolling list with a detail panel —
        /// so its chrome needs the least reshaping.
        /// </summary>
        private static GameObject FindTemplateApp()
        {
            var product = ProductManagerApp.Instance;
            return product != null ? product.gameObject : null;
        }

        /// <summary>
        /// Instantiates while the source is inactive, so no Awake runs on the clone until we have
        /// replaced its components. Restores the source's state immediately either way.
        /// </summary>
        private static GameObject CloneInactive(GameObject source)
        {
            var parent = source.transform.parent;
            var wasActive = source.activeSelf;

            source.SetActive(false);
            GameObject clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(source, parent, false);
                clone.SetActive(false);
            }
            finally
            {
                // Must run even if Instantiate throws, or the player loses their product app.
                source.SetActive(wasActive);
            }

            return clone;
        }

        /// <summary>
        /// Reads the App base-class fields off the template component.
        ///
        /// Reflection by field NAME rather than by FieldInfo: the template is an
        /// <c>App&lt;ProductManagerApp&gt;</c> and ours is an <c>App&lt;CookbookApp&gt;</c>. Those
        /// are different constructed types, so a FieldInfo from one cannot be used on the other —
        /// but the names and value types are identical.
        /// </summary>
        private static Dictionary<string, object> CaptureAppWiring(GameObject clone)
        {
            var captured = new Dictionary<string, object>(StringComparer.Ordinal);

            var template = FindAppComponent(clone);
            if (template == null) return captured;

            var appType = FindAppBaseType(template.GetType());
            if (appType == null) return captured;

            foreach (var field in appType.GetFields(Instance))
            {
                if (field.IsStatic) continue;
                try { captured[field.Name] = field.GetValue(template); }
                catch { /* a field we cannot read is one we simply will not restore */ }
            }
            return captured;
        }

        private static void RestoreAppWiring(CookbookApp app, Dictionary<string, object> captured)
        {
            if (app == null || captured == null || captured.Count == 0)
            {
                RecipePlannerUI.Log?.Error(
                    $"Cookbook: nothing captured from the template ({captured?.Count ?? 0} fields) — " +
                    "the app will have no container or screen.");
                return;
            }

            var appType = FindAppBaseType(app.GetType());
            if (appType == null) return;

            var restored = new List<string>();
            var failed = new List<string>();

            foreach (var field in appType.GetFields(Instance))
            {
                if (field.IsStatic) continue;

                // Apps is the shared static registry; AppName/IconLabel are ours to set.
                if (field.Name == "AppName" || field.Name == "IconLabel") continue;

                if (!captured.TryGetValue(field.Name, out var value)) continue;

                try
                {
                    field.SetValue(app, Coerce(field.FieldType, value));
                    restored.Add(field.Name + "=" + Describe(value));
                }
                catch (Exception ex)
                {
                    failed.Add(field.Name + " (" + ex.GetType().Name + ")");
                }
            }

            RecipePlannerUI.Log?.Info("Cookbook wiring restored: " + string.Join(", ", restored.ToArray()));
            if (failed.Count > 0)
                RecipePlannerUI.Log?.Warn("Cookbook wiring FAILED: " + string.Join(", ", failed.ToArray()));
        }

        /// <summary>
        /// Bridges a value between the two constructed generic types.
        ///
        /// An enum nested inside <c>App&lt;T&gt;</c> is a *different type* per closure, so
        /// <c>App&lt;ProductManagerApp&gt;.EOrientation</c> cannot be assigned to
        /// <c>App&lt;CookbookApp&gt;.EOrientation</c> even though they are declared identically —
        /// SetValue throws ArgumentException. Converting through the underlying integer carries the
        /// value across.
        /// </summary>
        private static object Coerce(Type targetType, object value)
        {
            if (value == null || targetType == null) return value;
            if (targetType.IsInstanceOfType(value)) return value;

            if (targetType.IsEnum)
            {
                try { return Enum.ToObject(targetType, Convert.ToInt64(value)); }
                catch { return value; }
            }

            return value;
        }

        /// <summary>Names the object a reference points at, so a mis-targeted clone is visible.</summary>
        private static string Describe(object value)
        {
            if (value == null) return "null";
            if (value is Component component)
                return component.gameObject.name + "/" + component.GetType().Name;
            if (value is GameObject go) return go.name;
            return value.ToString();
        }

        private static MonoBehaviour FindAppComponent(GameObject clone)
        {
            foreach (var behaviour in clone.GetComponents<MonoBehaviour>())
                if (behaviour != null && FindAppBaseType(behaviour.GetType()) != null) return behaviour;
            return null;
        }

        /// <summary>Walks up to the generic App&lt;T&gt; base, whichever T it was closed over.</summary>
        private static Type FindAppBaseType(Type type)
        {
            for (var t = type; t != null; t = t.BaseType)
                if (t.IsGenericType && t.Name.StartsWith("App`", StringComparison.Ordinal)) return t;
            return null;
        }

        private const System.Reflection.BindingFlags Instance =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly;

        /// <summary>
        /// Removes the template's own behaviours. Only components from the game's product-app
        /// namespace are touched — layout, canvases and images are what we are here for.
        /// </summary>
        private static void StripSourceComponents(GameObject clone)
        {
            var doomed = new List<Component>();

            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                var ns = behaviour.GetType().Namespace ?? string.Empty;
                if (ns.StartsWith("ScheduleOne.UI.Phone.ProductManagerApp", StringComparison.Ordinal))
                    doomed.Add(behaviour);
            }

            foreach (var component in doomed)
                UnityEngine.Object.DestroyImmediate(component);
        }

    }
}
