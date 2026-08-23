using System;
using UnityEngine;
using UnityEngine.Events;
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

        /// <summary>Runs <paramref name="action"/> when the button is clicked.</summary>
        public static void OnClick(Button button, Action action)
        {
            if (button == null || action == null) return;
            button.onClick.AddListener(ToUnityAction(action));
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
