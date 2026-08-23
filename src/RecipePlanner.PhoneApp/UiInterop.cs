using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// The few places where building UI differs between the Mono and IL2CPP branches.
    ///
    /// Everything else in this assembly is ordinary Unity code that compiles unchanged against
    /// either branch. Only two things actually differ, and both are collected here so the screens
    /// stay readable and there is exactly one place to look when a branch misbehaves:
    ///
    ///   1. <b>Delegates.</b> Il2CppInterop emits <c>UnityAction</c> as a class rather than a
    ///      delegate type, so a lambda cannot be passed to <c>AddListener</c> directly — it has to
    ///      be marshalled across the managed/native boundary first.
    ///
    ///   2. <b>Constructing a GameObject with components.</b> <c>new GameObject(name, typeof(T))</c>
    ///      takes a native type array on IL2CPP. Rather than branch on it, this builds the object
    ///      and adds the component separately, which is identical in effect and compiles on both.
    ///
    /// Keeping the seam this narrow is deliberate: the alternative is <c>#if</c> scattered through
    /// two thousand lines of layout code, where a branch-specific bug could hide indefinitely.
    /// </summary>
    internal static class UiInterop
    {
        /// <summary>
        /// A new GameObject carrying a RectTransform — the starting point for every UI node here.
        ///
        /// Written as construct-then-add rather than <c>new GameObject(name, typeof(RectTransform))</c>
        /// because the constructor overload taking types expects a native array on IL2CPP. The
        /// result is the same object either way.
        /// </summary>
        public static GameObject NewRect(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            return go;
        }

#if IL2CPP
        /// <summary>
        /// Delivers pointer enter/exit to something that cannot implement the handler interfaces.
        ///
        /// On IL2CPP a MonoBehaviour cannot implement <c>IPointerEnterHandler</c> — Il2CppInterop
        /// emits those interfaces as classes — so hover simply never fired, and the effects card
        /// could only be reached by clicking a row.
        ///
        /// <c>EventTrigger</c> is the way out: it is a real Unity component that already implements
        /// every handler interface natively and forwards them to a list of callbacks, so nothing of
        /// ours needs to implement anything. The same trick the whole embed relies on — use the
        /// game's own components instead of inventing types the runtime cannot be told about.
        /// </summary>
        public static void OnHover(GameObject target, Action<bool> onChanged)
        {
            if (target == null || onChanged == null) return;

            try
            {
                var trigger = target.GetComponent<EventTrigger>();
                if (trigger == null) trigger = target.AddComponent<EventTrigger>();

                AddTrigger(trigger, EventTriggerType.PointerEnter, () => onChanged(true));
                AddTrigger(trigger, EventTriggerType.PointerExit, () => onChanged(false));
            }
            catch (Exception ex)
            {
                // Hover is a nicety; losing it must never cost the row itself.
                RecipePlanner.UI.RecipePlannerUI.Log?.Warn("Could not wire hover: " + ex.Message);
            }
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            Action<BaseEventData> handler = _ => action();
            entry.callback.AddListener(
                Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>(handler));
            trigger.triggers.Add(entry);
        }
#endif

        /// <summary>Runs <paramref name="action"/> when the button is clicked.</summary>
        public static void OnClick(Button button, Action action)
        {
            if (button == null || action == null) return;
            button.onClick.AddListener(ToUnityAction(action));
        }

        /// <summary>
        /// Decides which component moves a list, and configures the ScrollRect to match.
        ///
        /// On Mono, <see cref="SmoothScroll"/> owns the wheel and the ScrollRect's own sensitivity
        /// is zeroed so the two do not fight — any value there reintroduces an instant per-notch
        /// jump underneath the easing.
        ///
        /// On IL2CPP, SmoothScroll cannot receive input at all: Il2CppInterop emits Unity's
        /// EventSystems handler interfaces as classes, so a MonoBehaviour cannot implement them.
        /// Zeroing sensitivity there means nothing moves the list whatsoever.
        ///
        /// That combination shipped, and every screen got it wrong in the same way, because the
        /// decision lived at four separate call sites and only one of them was fixed. It lives here
        /// now — a screen asks for a scrollable list and gets whichever mechanism works.
        /// </summary>
        /// <param name="stepPixels">Content pixels per wheel notch, usually one row.</param>
        public static SmoothScroll ConfigureWheel(ScrollRect scroll, float stepPixels)
        {
            if (scroll == null) return null;

#if IL2CPP
            // ScrollRect multiplies by the raw scrollDelta, which is NOT normalised — depending on
            // platform and mouse driver one notch arrives as 1, 3, or a fraction. Feeding it a
            // whole row moved three or four at a time, which is the same trap SmoothScroll was
            // written to avoid: it takes only the direction from the event and never the magnitude.
            // That option is not available here, so the step is divided by the common multiplier
            // instead. A slightly short notch is far easier to live with than one that overshoots.
            scroll.scrollSensitivity = stepPixels / 3f;

            // Off, not eased. ScrollRect's deceleration glides past wherever the notch landed, and
            // an overshoot on every notch reads as the list being slippery rather than smooth.
            scroll.inertia = false;
            return null;
#else
            scroll.scrollSensitivity = 0f;
            scroll.inertia = false;

            var smooth = scroll.gameObject.AddComponent<SmoothScroll>();
            smooth.Target = scroll;
            smooth.StepPixels = stepPixels;
            return smooth;
#endif
        }

        /// <summary>Runs <paramref name="action"/> whenever the scroll position changes.</summary>
        public static void OnScrollChanged(ScrollRect scroll, Action<Vector2> action)
        {
            if (scroll == null || action == null) return;
            scroll.onValueChanged.AddListener(ToUnityAction(action));
        }

#if IL2CPP
        // ConvertDelegate builds the native trampoline the game side needs. Without it the lambda
        // is a managed object the IL2CPP runtime has no way to call back into.
        private static UnityAction ToUnityAction(Action action) =>
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityAction>(action);

        private static UnityAction<Vector2> ToUnityAction(Action<Vector2> action) =>
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityAction<Vector2>>(action);
#else
        // On Mono UnityAction is a plain delegate, so this is just a cast with a name.
        private static UnityAction ToUnityAction(Action action) => new UnityAction(action);

        private static UnityAction<Vector2> ToUnityAction(Action<Vector2> action) =>
            new UnityAction<Vector2>(action);
#endif
    }
}
