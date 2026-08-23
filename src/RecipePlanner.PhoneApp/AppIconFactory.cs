using UnityEngine;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// Generates the Cookbook's home-screen icon at runtime.
    ///
    /// A mod ships no art, and reusing the template app's sprite would put two identical icons on
    /// the phone. Drawing a rounded tile in a colour none of the built-in apps use makes it
    /// findable at a glance, which is what a home-screen icon is for.
    /// </summary>
    public static class AppIconFactory
    {
        private const int Size = 128;
        private const int CornerRadius = 26;

        private static Sprite _cookbook;

        /// <summary>Warm amber — distinct from the green, orange, red, blue, navy and purple in use.</summary>
        private static readonly Color Fill = new Color(0.85f, 0.62f, 0.16f, 1f);
        private static readonly Color Mark = new Color(1f, 0.97f, 0.90f, 1f);

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

        /// <summary>Three horizontal bars, suggesting a page of entries.</summary>
        private static Color32 Paint(int x, int y)
        {
            const int left = 34, right = 94;
            var isBar = (Between(y, 42, 52) || Between(y, 60, 70) || Between(y, 78, 88))
                        && x >= left && x <= right;

            return isBar ? (Color32)Mark : (Color32)Fill;
        }

        private static bool Between(int v, int lo, int hi) => v >= lo && v <= hi;

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
