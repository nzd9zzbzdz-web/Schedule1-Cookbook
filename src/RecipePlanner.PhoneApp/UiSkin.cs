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

        private const int RingSize = 48;
        private const int RingRadius = 12;
        private const float RingThickness = 1.6f;

        private const int PillSize = 64;
        private const int LeafSize = 96;

        private static Sprite _body;
        private static Sprite _glow;
        private static Sprite _ring;
        private static Sprite _pill;
        private static Sprite _leaf;
        private static Sprite _barChart;

        /// <summary>A rounded rectangle, 9-sliced so it never distorts however it is stretched.</summary>
        public static Sprite Body => _body ?? (_body = BuildBody());

        /// <summary>The same shape with a soft falloff outside it, used as an underglow.</summary>
        public static Sprite Glow => _glow ?? (_glow = BuildGlow());

        /// <summary>
        /// The outline of that rectangle and nothing else.
        ///
        /// A separate sprite rather than a body behind a slightly smaller body: two stacked fills
        /// cannot produce a border over a background that is not flat, and the app's is not — it
        /// sits over the game world. A real hollow ring keeps the interior genuinely transparent.
        /// </summary>
        public static Sprite Ring => _ring ?? (_ring = BuildRing());

        /// <summary>
        /// A fully-rounded capsule for toolbar pills. Distinct from <see cref="Body"/> because a
        /// pill's radius is half its height, and a 9-slice cannot grow a corner radius — a body
        /// sprite stretched to pill height keeps its original, much smaller, corners.
        /// </summary>
        public static Sprite Pill => _pill ?? (_pill = BuildPill());

        /// <summary>
        /// A cannabis leaf silhouette, white so it can be tinted wherever it is used.
        ///
        /// Not 9-sliced: a shape with meaning cannot be stretched, only scaled, so it is drawn once
        /// at a size large enough for the biggest place it appears.
        /// </summary>
        public static Sprite PotLeaf => _leaf ?? (_leaf = BuildLeaf());

        /// <summary>
        /// Three rising bars, for the statistics screen.
        ///
        /// Rising rather than equal height: a chart glyph made of identical bars reads as a list or
        /// a menu, and the whole job of an icon here is to say "numbers" before the label is read.
        /// </summary>
        public static Sprite BarChart => _barChart ?? (_barChart = BuildBarChart());

        /// <summary>
        /// How far inside the leaf a point is, 0 outside and rising to 1 at a leaflet's spine.
        ///
        /// Shared with <see cref="AppIconFactory"/> so the home-screen icon and the in-app mark are
        /// literally the same shape — two hand-tuned leaves that nearly match would read as a
        /// mistake rather than as a family.
        ///
        /// Each leaflet is a capsule from the origin to its tip whose width tapers to nothing at the
        /// end, which is what gives the pointed lobes. The result is the union, so overlapping
        /// leaflets merge instead of showing a seam.
        /// </summary>
        internal static float LeafCoverage(float px, float py)
        {
            var best = 0f;

            for (var i = 0; i < LeafletAngles.Length; i++)
            {
                var radians = LeafletAngles[i] * Mathf.Deg2Rad;
                var tipX = Mathf.Cos(radians) * LeafletLengths[i];
                var tipY = Mathf.Sin(radians) * LeafletLengths[i];

                var lengthSquared = tipX * tipX + tipY * tipY;
                if (lengthSquared <= 0f) continue;

                var t = Mathf.Clamp01((px * tipX + py * tipY) / lengthSquared);

                var dx = px - tipX * t;
                var dy = py - tipY * t;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);

                // Widest a third of the way along and tapering to a point, so the leaflets meet in
                // a stalk rather than a blob.
                var width = LeafletWidths[i] * Mathf.Sin(Mathf.Clamp01(t * 1.15f) * Mathf.PI * 0.85f + 0.12f);
                if (width <= 0f) continue;

                var coverage = 1f - distance / width;
                if (coverage > best) best = coverage;
            }

            if (py < 0f && py > -0.16f && Mathf.Abs(px) < 0.016f)
                best = Mathf.Max(best, 1f - Mathf.Abs(px) / 0.016f);

            return best;
        }

        /// <summary>Seven leaflets, the count a cannabis leaf is normally drawn with.</summary>
        private static readonly float[] LeafletAngles = { 90f, 55f, 125f, 25f, 155f, 0f, 180f };
        private static readonly float[] LeafletLengths = { 0.46f, 0.42f, 0.42f, 0.34f, 0.34f, 0.24f, 0.24f };
        private static readonly float[] LeafletWidths = { 0.085f, 0.078f, 0.078f, 0.066f, 0.066f, 0.052f, 0.052f };

        /// <summary>
        /// Dropped when the app is torn down. Sprites and textures are unmanaged Unity objects; if
        /// a save unload destroys the UI while these still point at freed textures, the next save
        /// would draw garbage or throw.
        /// </summary>
        public static void Clear()
        {
            Destroy(ref _body);
            Destroy(ref _glow);
            Destroy(ref _ring);
            Destroy(ref _pill);
            Destroy(ref _leaf);
            Destroy(ref _barChart);
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

        private static Sprite BuildRing()
        {
            var pixels = new Color[RingSize * RingSize];
            var half = RingSize * 0.5f;

            for (var y = 0; y < RingSize; y++)
            for (var x = 0; x < RingSize; x++)
            {
                var d = RoundedBoxDistance(x, y, half, half, RingRadius);

                // Distance from the outline itself rather than from the shape: |d| is zero on the
                // edge and grows either side, so a band around zero is the stroke. Falling off over
                // one pixel at each end keeps it smooth at any scale.
                var a = Mathf.Clamp01(RingThickness * 0.5f - Mathf.Abs(d) + 0.5f);
                pixels[y * RingSize + x] = new Color(1f, 1f, 1f, a);
            }

            return ToSprite(pixels, RingSize, RingRadius);
        }

        private static Sprite BuildLeaf()
        {
            var pixels = new Color[LeafSize * LeafSize];

            for (var y = 0; y < LeafSize; y++)
            for (var x = 0; x < LeafSize; x++)
            {
                // The origin sits below centre so the fan has room to spread upward without
                // clipping, and the stem has somewhere to go.
                var px = (x + 0.5f) / LeafSize - 0.5f;
                var py = (y + 0.5f) / LeafSize - 0.30f;

                // Scaled up so the coverage edge spans about a pixel at this resolution, which is
                // what anti-aliases it.
                var a = Mathf.Clamp01(LeafCoverage(px, py) * LeafSize * 0.05f);
                pixels[y * LeafSize + x] = new Color(1f, 1f, 1f, a);
            }

            var texture = new Texture2D(LeafSize, LeafSize, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, LeafSize, LeafSize), new Vector2(0.5f, 0.5f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite BuildBarChart()
        {
            const int size = 64;
            var pixels = new Color[size * size];

            // Left edge, width and height of each bar, as fractions of the sprite.
            var lefts = new[] { 0.10f, 0.40f, 0.70f };
            var heights = new[] { 0.38f, 0.66f, 0.94f };
            const float barWidth = 0.20f;
            const float baseline = 0.06f;

            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var u = (x + 0.5f) / size;
                var v = (y + 0.5f) / size;

                var a = 0f;
                for (var i = 0; i < lefts.Length; i++)
                {
                    if (u < lefts[i] || u > lefts[i] + barWidth) continue;
                    if (v < baseline || v > heights[i]) continue;

                    // Softened at the vertical edges only; the tops and the baseline are meant to
                    // line up crisply with each other.
                    var edge = Mathf.Min(u - lefts[i], lefts[i] + barWidth - u) * size;
                    a = Mathf.Max(a, Mathf.Clamp01(edge));
                }

                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }

            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite BuildPill()
        {
            var pixels = new Color[PillSize * PillSize];
            var half = PillSize * 0.5f;

            for (var y = 0; y < PillSize; y++)
            for (var x = 0; x < PillSize; x++)
            {
                // Radius equal to the half-extent makes the rounded box a circle, which is exactly
                // the cap a capsule needs at each end.
                var d = RoundedBoxDistance(x, y, half, half, half);
                var a = Mathf.Clamp01(0.5f - d);
                pixels[y * PillSize + x] = new Color(1f, 1f, 1f, a);
            }

            // One pixel short of half, so the 9-slice keeps a sliver of middle to stretch. A border
            // of exactly half leaves no stretchable centre and the sprite refuses to widen.
            return ToSprite(pixels, PillSize, PillSize / 2 - 1);
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
    //
    // Mono-only interfaces; see SmoothScroll for why. Without them the glow never lights,
    // which costs a hover effect and nothing else.
#if IL2CPP
    internal sealed class HoverGlow : MonoBehaviour
#else
    internal sealed class HoverGlow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
#endif
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
