using System;
using RecipePlanner.UI;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// Makes this assembly's MonoBehaviours visible to the IL2CPP runtime.
    ///
    /// On Mono, <c>AddComponent&lt;T&gt;</c> works for any managed type because the runtime already
    /// knows about it. On IL2CPP the game's type system is native and knows only what was compiled
    /// into it, so a type invented by a mod has to be injected before a component of it can exist.
    /// Skipping this does not degrade anything gracefully — <c>AddComponent</c> simply fails.
    ///
    /// Only three types need it, and only because they are components:
    /// <see cref="CookbookApp"/>, <see cref="SmoothScroll"/> and <c>HoverGlow</c>. Everything else
    /// here is a plain class that builds UI out of the game's own components, which is why the port
    /// is as small as it is.
    ///
    /// <b>The one genuinely uncertain part of the IL2CPP port.</b> <see cref="CookbookApp"/> derives
    /// from <c>App&lt;CookbookApp&gt;</c> — a managed type whose base is an IL2CPP generic
    /// instantiation, which is the case Il2CppInterop handles least well. That it compiles proves
    /// the shapes line up and says nothing about whether injection succeeds. If this is going to
    /// fail, it fails here, which is exactly why the failure is caught and reported rather than
    /// left to surface as a confusing <c>AddComponent</c> null further down.
    /// </summary>
    internal static class Il2CppTypes
    {
        private static bool _done;
        private static bool _succeeded;

        /// <summary>
        /// Registers the component types, once per session. Returns false when the UI cannot work
        /// on this runtime, so the caller can decline to build rather than fail halfway through and
        /// leave a half-made app on the phone.
        /// </summary>
        internal static bool EnsureRegistered()
        {
            if (_done) return _succeeded;
            _done = true;

#if IL2CPP
            // CookbookApp is deliberately NOT registered. It cannot be: its base is
            // App<CookbookApp>, and IL2CPP would need that class to exist before CookbookApp can be
            // made, while that class cannot be made until CookbookApp exists. Confirmed on a
            // running game — the two types below registered without complaint in the same pass that
            // CookbookApp failed, which is what proved the problem is the generic base and not the
            // mechanism. This branch uses CookbookEmbed instead, which subclasses nothing.
            // One question, asked once, acting on nothing. See CookbookAppProbe.
            CookbookAppProbe.Ask();

            var scroll = Register<SmoothScroll>("SmoothScroll");
            var glow = Register<HoverGlow>("HoverGlow");

            _succeeded = scroll && glow;

            if (_succeeded)
            {
                RecipePlannerUI.Log?.Info("IL2CPP type injection succeeded — the Cookbook panel can be built.");
            }
            else
            {
                RecipePlannerUI.Log?.Error(
                    $"IL2CPP type injection failed (SmoothScroll={scroll}, HoverGlow={glow}), so the " +
                    "Cookbook cannot be built on this branch. Production tracking is unaffected " +
                    "and keeps running.");
            }
#else
            // Mono needs nothing: the runtime is already the one these types were compiled for.
            _succeeded = true;
#endif
            return _succeeded;
        }

#if IL2CPP
        /// <summary>
        /// Idempotent by design. Registering a type twice throws, and this can be reached again
        /// after a save reload, so an already-registered type is a success rather than a failure.
        /// </summary>
        private static bool Register<T>(string label) where T : Il2CppSystem.Object
        {
            try
            {
                if (Il2CppInterop.Runtime.Il2CppType.From(typeof(T), throwOnFailure: false) != null)
                    return true;

                Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<T>();
                return true;
            }
            catch (Exception ex)
            {
                RecipePlannerUI.Log?.Error($"Could not inject {label}: {Explain(ex)}");
                return false;
            }
        }

        /// <summary>
        /// Digs the real failure out of the reflection wrapper.
        ///
        /// ClassInjector is invoked through a dynamic method, so everything it throws arrives as
        /// "Exception has been thrown by the target of an invocation" — which says nothing at all.
        /// The whole chain is reported because the useful sentence is usually two levels down.
        /// </summary>
        private static string Explain(Exception ex)
        {
            var text = new System.Text.StringBuilder();
            var depth = 0;

            for (var e = ex; e != null && depth < 6; e = e.InnerException, depth++)
            {
                if (depth > 0) text.Append("\n  caused by: ");
                text.Append(e.GetType().Name).Append(": ").Append(e.Message);
            }

            // Kept last and only once: the innermost frame is where the answer is, and repeating a
            // stack per nesting level buries it.
            var deepest = ex;
            while (deepest.InnerException != null) deepest = deepest.InnerException;
            if (!string.IsNullOrEmpty(deepest.StackTrace))
                text.Append("\n  at: ").Append(deepest.StackTrace);

            return text.ToString();
        }
#endif
    }
}
