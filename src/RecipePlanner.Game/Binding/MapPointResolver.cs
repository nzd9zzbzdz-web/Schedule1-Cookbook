using System;
using System.Reflection;

namespace RecipePlanner.Game.Binding
{
    /// <summary>
    /// Calls a live <c>MixerMap.GetEffectAtPoint</c> so the guide reports the game's own answer
    /// rather than our reading of the geometry.
    ///
    /// The awkward part is the argument. This assembly deliberately has no UnityEngine reference —
    /// that is what lets it load on either Steam branch — so <c>Vector2</c> cannot be named here.
    /// It is instead recovered from the method's own parameter list and constructed reflectively.
    /// A struct with a two-float constructor is a modest thing to build blind, and doing so keeps
    /// the zero-game-reference property that the whole branch-agnostic design rests on.
    ///
    /// Returns null from <see cref="For"/> when anything cannot be resolved; the caller then falls
    /// back to <c>MixMapSolver</c> and marks the guide approximate.
    /// </summary>
    internal sealed class MapPointResolver
    {
        private readonly object _map;
        private readonly MethodInfo _getEffectAtPoint;
        private readonly ConstructorInfo _vectorConstructor;

        private MapPointResolver(object map, MethodInfo method, ConstructorInfo constructor)
        {
            _map = map;
            _getEffectAtPoint = method;
            _vectorConstructor = constructor;
        }

        public static MapPointResolver For(object map, ILog log)
        {
            if (map == null) return null;

            try
            {
                var method = map.GetType().GetMethod(
                    "GetEffectAtPoint",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                if (method == null) return null;

                var parameters = method.GetParameters();
                if (parameters.Length != 1) return null;

                var vectorType = parameters[0].ParameterType;
                var constructor = vectorType.GetConstructor(new[] { typeof(float), typeof(float) });
                if (constructor == null) return null;

                return new MapPointResolver(map, method, constructor);
            }
            catch (Exception ex)
            {
                log?.Warn("Falling back to a derived mix map; the game's own resolver was unreachable. "
                          + ex.Message);
                return null;
            }
        }

        /// <summary>The effect id at a point, or null if the point is in open space.</summary>
        public string EffectIdAt(float x, float y)
        {
            try
            {
                var point = _vectorConstructor.Invoke(new object[] { x, y });
                var hit = _getEffectAtPoint.Invoke(_map, new[] { point });
                if (hit == null) return null;

                // GetEffectAtPoint returns the MixerMapEffect wrapper, not the Effect itself.
                var property = Reflect.Get(hit, "Property") ?? hit;
                return Reflect.GetString(property, "ID") ?? Reflect.GetString(property, "Name");
            }
            catch
            {
                // One bad point must not abandon the whole table; the caller treats null as
                // "nothing there", which is also what an out-of-bounds point means.
                return null;
            }
        }
    }
}
