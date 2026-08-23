using UnityEngine;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// Generates the Cookbook's home-screen icon at runtime.
    ///
    /// A mod ships no art, and reusing the template app's sprite would put two identical icons on
    /// the phone. The leaf is drawn rather than approximated with bars because a home-screen icon
    /// is read at a glance and at a small size: a recognisable silhouette identifies the app before
    /// the label does, where an abstract mark makes the player read every icon in turn.
    /// </summary>
    public static class AppIconFactory
    {
        private const int Size = 128;
        private const int CornerRadius = 26;

        private static Sprite _cookbook;

        private static readonly Color Fill = new Color(0.055f, 0.13f, 0.075f, 1f);
        private static readonly Color Leaf = new Color(0.24f, 0.92f, 0.44f, 1f);
        private static readonly Color LeafDeep = new Color(0.13f, 0.62f, 0.28f, 1f);

        public static Sprite Cookbook()
        {
            if (_cookbook != null) return _cookbook;

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[Size * Size];
            var clear = new Color32(0, 0, 0, 0);

            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                pixels[y * Size + x] = InsideRoundedRect(x, y) ? Paint(x, y) : clear;

            texture.SetPixels32(pixels);
            texture.Apply();

            _cookbook = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
            _cookbook.name = "CookbookAppIcon";
            return _cookbook;
        }

        private static Color32 Paint(int x, int y)
        {
            // Normalised around the leaf's origin, which sits below centre so the fan has room to
            // spread upward without clipping the tile.
            var px = (x + 0.5f) / Size - 0.5f;
            var py = (y + 0.5f) / Size - 0.30f;

            var coverage = UiSkin.LeafCoverage(px, py);
            if (coverage <= 0f) return Fill;

            // Deeper green toward the middle of each leaflet, brighter at the edges, which suggests
            // the veining a flat silhouette loses at this size.
            var shade = Color.Lerp(Leaf, LeafDeep, Mathf.Clamp01(coverage * 0.55f));
            return Color.Lerp(Fill, shade, Mathf.Clamp01(coverage * 6f));
        }

        // The leaf itself lives in UiSkin, so the home-screen icon and the in-app mark are the
        // same shape rather than two hand-tuned drawings that nearly agree.

        /// <summary>Squared distance keeps this integer-only; no per-pixel sqrt.</summary>
        private static bool InsideRoundedRect(int x, int y)
        {
            var cx = x < CornerRadius ? CornerRadius
                   : x > Size - 1 - CornerRadius ? Size - 1 - CornerRadius
                   : x;
            var cy = y < CornerRadius ? CornerRadius
                   : y > Size - 1 - CornerRadius ? Size - 1 - CornerRadius
                   : y;

            if (cx == x && cy == y) return true;   // straight edge, not a corner

            var dx = x - cx;
            var dy = y - cy;
            return dx * dx + dy * dy <= CornerRadius * CornerRadius;
        }
    }
}
