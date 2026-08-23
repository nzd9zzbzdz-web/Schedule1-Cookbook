using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// Rounded, softly-lit chrome for the Cookbook, built without shipping a single asset.
    ///
    /// Every sprite here is generated once into a texture at runtime and cached. That matters for
    /// a mod: no AssetBundle to version against the game, nothing to load from disk, nothing to go
    /// missing in someone's install. The cost is a handful of small textures created on first use.
    ///
    /// The shapes are drawn from a signed distance field rather than by plotting corners, which is
    /// what gives clean anti-aliased edges at any size — and lets the same maths produce both the
    /// solid body and its glow just by changing how distance maps to alpha.
    /// </summary>
    internal static class UiSkin
    {
        // 9-slicing means one texture serves every button size, so these are the only two we make.
        private const int BodySize = 48;
        private const int BodyRadius = 12;

        private const int GlowSize = 64;
        private const int GlowRadius = 12;
        private const int GlowSpread = 12;

        private static Sprite _body;
        private static Sprite _glow;

        /// <summary>A rounded rectangle, 9-sliced so it never distorts however it is stretched.</summary>
        public static Sprite Body => _body ?? (_body = BuildBody());

        /// <summary>The same shape with a soft falloff outside it, used as an underglow.</summary>
        public static Sprite Glow => _glow ?? (_glow = BuildGlow());

        /// <summary>
        /// Dropped when the app is torn down. Sprites and textures are unmanaged Unity objects; if
        /// a save unload destroys the UI while these still point at freed textures, the next save
        /// would draw garbage or throw.
        /// </summary>
        public static void Clear()
        {
            Destroy(ref _body);
            Destroy(ref _glow);
        }

        private static void Destroy(ref Sprite sprite)
        {
            if (sprite == null) return;
            try
            {
                var texture = sprite.texture;
                UnityEngine.Object.Destroy(sprite);
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
            catch { /* already gone */ }
            sprite = null;
        }

        // ---- shape generation ----

        private static Sprite BuildBody()
        {
            var pixels = new Color[BodySize * BodySize];
            var half = BodySize * 0.5f;

            for (var y = 0; y < BodySize; y++)
            for (var x = 0; x < BodySize; x++)
            {
                var d = RoundedBoxDistance(x, y, half, half, BodyRadius);
                // Half a pixel either side of the edge gives one pixel of anti-aliasing.
                var a = Mathf.Clamp01(0.5f - d);
                pixels[y * BodySize + x] = new Color(1f, 1f, 1f, a);
            }

            return ToSprite(pixels, BodySize, BodyRadius);
        }

        private static Sprite BuildGlow()
        {
            var pixels = new Color[GlowSize * GlowSize];
            var half = GlowSize * 0.5f;
            var inner = half - GlowSpread;

            for (var y = 0; y < GlowSize; y++)
            for (var x = 0; x < GlowSize; x++)
            {
                var d = RoundedBoxDistance(x, y, half, inner, GlowRadius);

                float a;
                if (d <= 0f)
                {
                    a = 1f;
                }
                else
                {
                    // Squared falloff: linear reads as a hard-edged halo rather than a glow.
                    var t = Mathf.Clamp01(1f - d / GlowSpread);
                    a = t * t;
                }

                pixels[y * GlowSize + x] = new Color(1f, 1f, 1f, a);
            }

            return ToSprite(pixels, GlowSize, GlowRadius + GlowSpread);
        }

        /// <summary>
        /// Signed distance to a rounded box centred in the texture. Negative inside, positive
        /// outside, and the magnitude is in pixels — which is what makes clean anti-aliasing and a
        /// smooth glow falloff fall straight out of the same number.
        /// </summary>
        private static float RoundedBoxDistance(int x, int y, float centre, float extent, float radius)
        {
            var px = Mathf.Abs(x + 0.5f - centre) - extent + radius;
            var py = Mathf.Abs(y + 0.5f - centre) - extent + radius;

            var qx = Mathf.Max(px, 0f);
            var qy = Mathf.Max(py, 0f);

            return Mathf.Sqrt(qx * qx + qy * qy) + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
        }

        private static Sprite ToSprite(Color[] pixels, int size, int border)
        {
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);

            // The border is what 9-slicing protects: corners are drawn at native size and only the
            // middle stretches, so a 300px-wide button keeps the same corner radius as a 40px one.
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));

            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }

    /// <summary>
    /// Lights a button and its underglow while the pointer is over it, and dims them again when it
    /// leaves.
    ///
    /// uGUI's built-in ColorTint transition already handles the body, but it can only drive the one
    /// graphic a Button targets — and the glow is a separate Image sitting behind it. This drives
    /// both together so they brighten as one object rather than the body popping while the glow
    /// stays flat.
    ///
    /// Lerped rather than snapped: an instant colour change reads as a flicker, especially when the
    /// pointer crosses several buttons on the way somewhere else.
    /// </summary>
    internal sealed class HoverGlow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float FadeSpeed = 12f;

        private Image _glow;
        private Color _restColour;
        private Color _hotColour;
        private bool _hot;

        public Action<bool> HoverChanged;

        public static HoverGlow Attach(GameObject target, Image glow, Color rest, Color hot)
        {
            var self = target.AddComponent<HoverGlow>();
            self._glow = glow;
            self._restColour = rest;
            self._hotColour = hot;
            if (glow != null) glow.color = rest;
            return self;
        }

        public void OnPointerEnter(PointerEventData eventData) => Set(true);
        public void OnPointerExit(PointerEventData eventData) => Set(false);

        private void Set(bool hot)
        {
            if (_hot == hot) return;
            _hot = hot;
            try { HoverChanged?.Invoke(hot); }
            catch { /* a hover handler must never take the phone down */ }
        }

        private void OnDisable()
        {
            // Pointer exit is not delivered to a disabled object, so a row recycled by the list
            // while hovered would otherwise stay lit forever.
            _hot = false;
            if (_glow != null) _glow.color = _restColour;
        }

        private void Update()
        {
            if (_glow == null) return;

            var target = _hot ? _hotColour : _restColour;
            var current = _glow.color;
            if (current == target) return;

            _glow.color = Color.Lerp(current, target, Mathf.Clamp01(Time.unscaledDeltaTime * FadeSpeed));
        }
    }
}
