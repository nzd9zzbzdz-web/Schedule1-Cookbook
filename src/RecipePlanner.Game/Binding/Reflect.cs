using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RecipePlanner.Game.Binding
{
    /// <summary>
    /// Late-bound member access.
    ///
    /// The binding layer reads the game entirely by reflection rather than compiling against
    /// Assembly-CSharp. That buys three things: the project builds without the game installed,
    /// the same assembly works on the Mono and IL2CPP branches, and a renamed member degrades to a
    /// null read caught by SymbolGuard instead of a TypeLoadException at startup.
    /// </summary>
    public static class Reflect
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        public static object Get(object instance, string member)
        {
            if (instance == null || string.IsNullOrEmpty(member)) return null;
            return GetFrom(instance.GetType(), instance, member);
        }

        public static object GetStatic(Type type, string member) => GetFrom(type, null, member);

        private static object GetFrom(Type type, object instance, string member)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(member, Any);
                if (field != null) return Safe(() => field.GetValue(instance));

                var prop = t.GetProperty(member, Any);
                if (prop != null && prop.CanRead) return Safe(() => prop.GetValue(instance, null));
            }
            return null;
        }

        public static object Call(object instance, string method)
        {
            if (instance == null) return null;
            for (var t = instance.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod(method, Any, null, Type.EmptyTypes, null);
                if (m != null) return Safe(() => m.Invoke(instance, null));
            }
            return null;
        }

        /// <summary>
        /// Invokes a single-out-parameter method, e.g.
        /// <c>MixOperation.IsOutputKnown(out ProductDefinition knownProduct)</c>.
        ///
        /// Returns the method's own return value; <paramref name="outValue"/> receives the out
        /// argument. Returns null (and a null out value) if the method is absent or throws, so a
        /// signature change degrades instead of crashing the game's call stack.
        /// </summary>
        public static object CallOut(object instance, string method, out object outValue)
        {
            outValue = null;
            if (instance == null) return null;

            for (var t = instance.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethods(Any).FirstOrDefault(x =>
                    x.Name == method &&
                    x.GetParameters().Length == 1 &&
                    x.GetParameters()[0].IsOut);

                if (m == null) continue;

                var args = new object[1];
                var result = Safe(() => m.Invoke(instance, args));
                outValue = args[0];
                return result;
            }
            return null;
        }

        /// <summary>
        /// Enumerates a collection that may be a plain BCL type OR an Il2CppSystem proxy.
        ///
        /// Il2CppInterop's List&lt;T&gt; does not reliably implement System.Collections.IEnumerable,
        /// so a bare <c>is IEnumerable</c> cast silently yields nothing on the IL2CPP branch and
        /// every product lookup comes back empty. Falling back to Count + get_Item covers both.
        /// </summary>
        public static IEnumerable<object> Enumerate(object collection)
        {
            if (collection == null) yield break;

            if (collection is IEnumerable plain && !(collection is string))
            {
                foreach (var item in plain) yield return item;
                yield break;
            }

            var type = collection.GetType();
            var countProperty = type.GetProperty("Count", Any);
            var indexer = type.GetMethod("get_Item", Any, null, new[] { typeof(int) }, null);
            if (countProperty == null || indexer == null) yield break;

            int count;
            try { count = Convert.ToInt32(countProperty.GetValue(collection, null)); }
            catch { yield break; }

            for (var i = 0; i < count; i++)
            {
                object item;
                try { item = indexer.Invoke(collection, new object[] { i }); }
                catch { yield break; }
                yield return item;
            }
        }

        public static string GetString(object instance, string member) => AsString(Get(instance, member));

        public static int GetInt(object instance, string member, int fallback = 0)
        {
            var v = Get(instance, member);
            if (v == null) return fallback;
            try { return Convert.ToInt32(v); } catch { return fallback; }
        }

        public static bool GetBool(object instance, string member, bool fallback = false)
        {
            var v = Get(instance, member);
            if (v is bool b) return b;
            return fallback;
        }

        /// <summary>
        /// Unity overloads == against null for destroyed objects, so a plain null check on a
        /// UnityEngine.Object reference is not enough. ToString() on a destroyed object yields
        /// "null", which is the cheapest reliable signal available through pure reflection.
        /// </summary>
        public static bool IsAlive(object unityObject)
        {
            if (unityObject == null) return false;
            try { return unityObject.ToString() != "null"; }
            catch { return false; }
        }

        /// <summary>Enum values arrive boxed; the game's own name is what we want to persist.</summary>
        public static string AsString(object value)
        {
            if (value == null) return null;
            if (value is string s) return s;
            try { return value.ToString(); } catch { return null; }
        }

        private static object Safe(Func<object> f)
        {
            try { return f(); }
            catch (TargetInvocationException) { return null; }
            catch (MemberAccessException) { return null; }
            catch (InvalidOperationException) { return null; }
        }
    }
}
