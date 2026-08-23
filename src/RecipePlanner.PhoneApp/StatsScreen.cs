using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using RecipePlanner.Core.Stats;
using RecipePlanner.UI;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// The production record: what this character has actually made.
    ///
    /// Every number here was already being computed and written to <c>cookbook.md</c>; none of it
    /// had ever been visible in-game. The mod's second stated question is "what have I made?", and
    /// until now the answer lived in a file the player had to leave the game to read.
    ///
    /// One scrolling column rather than the guide's two panes, because this is a report to be read
    /// top to bottom, not a set of things to look up. Nothing here is selectable, so a browse pane
    /// would be an empty gesture.
    ///
    /// Purely a view over <see cref="PlayerStatistics"/>: no totals are computed here. Every figure
    /// is recomputable from the event log, which is what makes the report trustworthy — and adding
    /// arithmetic at the display layer is how two parts of an app start disagreeing.
    /// </summary>
    internal sealed class StatsScreen
    {
        private const float HeaderHeight = 46f;
        private const float LineHeight = 26f;
        private const float BarHeight = 16f;

        private readonly RectTransform _root;
        private readonly Font _font;

        private RectTransform _content;
        private Text _title;

        private readonly List<StatRow> _rows = new List<StatRow>();
        private int _index;
        private float _y;

        private StatsScreen(RectTransform root, Font font)
        {
            _root = root;
            _font = font;
        }

        public static StatsScreen CreateInto(RectTransform parent, Font font)
        {
            var root = New(parent, "Stats");
            Fill(root);

            var backdropRect = New(root, "Backdrop");
            Fill(backdropRect);
            var backdrop = backdropRect.gameObject.AddComponent<Image>();
            backdrop.color = Backdrop;
            backdrop.raycastTarget = true;

            var screen = new StatsScreen(root, font);
            screen.Build();
            root.gameObject.SetActive(false);
            return screen;
        }

        // ---- construction ----

        private void Build()
        {
            var header = New(_root, "Header");
            Anchor(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -HeaderHeight), Vector2.zero);
            header.gameObject.AddComponent<Image>().color = HeaderFill;

            _title = Label(header, "Title", "Statistics", 24, FontStyle.Bold);
            _title.color = Accent;
            Anchor(_title.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 0f), new Vector2(-90f, 0f));

            CloseButton(header);

            var viewport = New(_root, "Viewport");
            Anchor(viewport, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -HeaderHeight - 6f));

            var catcher = viewport.gameObject.AddComponent<Image>();
            catcher.color = Transparent;
            catcher.raycastTarget = true;

            viewport.gameObject.AddComponent<RectMask2D>();

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var smooth = UiInterop.ConfigureWheel(scroll, LineHeight * 3f);
            if (smooth != null) smooth.SmoothTime = 0.16f;

            _content = New(viewport, "Content");
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;

            scroll.viewport = viewport;
            scroll.content = _content;
        }

        // ---- content ----

        public void Open(CookbookViewModel model)
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Populate(model);
        }

        public void Close() => _root.gameObject.SetActive(false);

        private void Populate(CookbookViewModel model)
        {
            _index = 0;
            _y = 0f;

            var stats = model?.Stats;
            _title.text = model != null && !string.IsNullOrEmpty(model.ProfileLabel)
                ? "Statistics — " + model.ProfileLabel
                : "Statistics";

            if (stats == null || stats.EventsFolded == 0)
            {
                Line("Nothing recorded yet.", Muted);
                Line("Cook a batch and it will appear here.", Muted);
                Finish();
                return;
            }

            var name = model.DisplayName ?? (id => id);

            Lifetime(stats);
            ByType(stats);
            TopProducts(stats, name);
            TopIngredients(stats);
            Records(stats, name);
            Excluded(stats);

            Finish();
        }

        private void Lifetime(PlayerStatistics stats)
        {
            var t = stats.Personal ?? new Totals();

            Heading("LIFETIME");
            Pair("Units produced", Num(t.UnitsProduced));
            Pair("Batches", Num(t.Batches));
            Pair("Unique recipes", Num(stats.UniqueRecipesProduced));
            Pair("Events recorded", Num(stats.EventsFolded));

            if (t.TotalValue > 0 || t.TotalCost > 0)
            {
                Pair("Value", Money(t.TotalValue));
                Pair("Cost", Money(t.TotalCost));

                // Derived rather than read from EstimatedProfit, which is a stored field: three
                // figures on screen that do not add up destroy confidence in all three.
                Pair("Profit", Money(t.TotalValue - t.TotalCost), Accent);
            }
            else
            {
                Line("Money is unavailable — the game's price table could not be read.", Muted);
            }
        }

        /// <summary>
        /// Units per drug type, as bars scaled to the biggest.
        ///
        /// Proportional to the largest rather than to the total: with one dominant type every other
        /// bar rounds to a sliver, and the comparison the player actually wants is between the
        /// types, not against the sum.
        /// </summary>
        private void ByType(PlayerStatistics stats)
        {
            if (stats.ByDrugType == null || stats.ByDrugType.Count == 0) return;

            var ordered = stats.ByDrugType
                .Where(p => p.Value != null)
                .OrderByDescending(p => p.Value.UnitsProduced)
                .ToList();

            var largest = ordered.Count > 0 ? Math.Max(1L, ordered[0].Value.UnitsProduced) : 1L;

            Heading("BY TYPE");
            foreach (var pair in ordered)
                Bar(pair.Key, Num(pair.Value.UnitsProduced) + " units",
                    (float)pair.Value.UnitsProduced / largest);
        }

        private void TopProducts(PlayerStatistics stats, Func<string, string> name)
        {
            var products = Ordered(stats.ByProduct, p => p.Units);
            if (products.Count == 0) return;

            Heading("MOST PRODUCED");
            foreach (var product in products.Take(8))
            {
                var label = !string.IsNullOrEmpty(product.DisplayName)
                    ? product.DisplayName
                    : name(product.ProductId);

                var detail = Num(product.Units) + " units";
                if (product.Value > 0) detail += "   " + Money(product.Value);

                Pair(label, detail);
            }
        }

        private void TopIngredients(PlayerStatistics stats)
        {
            var used = Ordered(stats.ByIngredient, i => i.UnitsConsumed);
            if (used.Count == 0) return;

            Heading("MOST USED INGREDIENTS");
            foreach (var ingredient in used.Take(8))
                Pair(ingredient.IngredientId,
                     Num(ingredient.UnitsConsumed) + " used   " + Num(ingredient.TimesUsed) + "x");
        }

        private void Records(PlayerStatistics stats, Func<string, string> name)
        {
            var records = stats.Records;
            if (records == null) return;

            var any = false;

            Heading("RECORDS");
            any |= Record("Most made", RecipeName(stats, records.MostUsedRecipeId, name));
            any |= Record("Most produced", ProductName(stats, records.MostProducedProductId, name));
            any |= Record("Most used ingredient", records.MostUsedIngredientId);
            any |= Record("Most profitable", RecipeName(stats, records.MostProfitableRecipeId, name));

            if (records.LargestBatchUnits > 0)
            {
                Pair("Biggest batch",
                     Num(records.LargestBatchUnits) + " units of "
                     + ProductName(stats, records.LargestBatchProductId, name));
                any = true;
            }

            if (!any) Line("Nothing notable yet.", Muted);
        }

        /// <summary>
        /// Production kept out of the totals, and why.
        ///
        /// Shown rather than quietly dropped: a player whose total is lower than they expected needs
        /// to see it was a deliberate exclusion. Without this the mod looks like it is miscounting,
        /// which is the single most likely thing to be reported as a bug.
        /// </summary>
        private void Excluded(PlayerStatistics stats)
        {
            var excluded = stats.Excluded;
            if (excluded == null || (excluded.UnitsProduced == 0 && excluded.Batches == 0)) return;

            Heading("NOT COUNTED AS YOURS");
            Pair("Units", Num(excluded.UnitsProduced));
            Pair("Batches", Num(excluded.Batches));

            if (stats.ExcludedByReason == null) return;

            foreach (var reason in stats.ExcludedByReason.OrderByDescending(p => p.Value))
                Pair("   " + Pretty(reason.Key), Num(reason.Value) + " event(s)", Muted);
        }

        /// <summary>"attribution:Employee" is how it is stored; "Employee" is what it means.</summary>
        private static string Pretty(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "Other";
            var colon = reason.IndexOf(':');
            return colon >= 0 && colon < reason.Length - 1 ? reason.Substring(colon + 1) : reason;
        }

        // ---- row primitives ----

        private sealed class StatRow
        {
            public RectTransform Rect;
            public Text Left;
            public Text Right;
            public RectTransform BarTrack;
            public RectTransform BarFill;
            public Image BarFillImage;
        }

        private void Heading(string text)
        {
            Spacer(10f);
            var row = RowAt(_index++);
            Place(row, LineHeight);

            row.Left.text = text;
            row.Left.color = Accent;
            row.Left.fontStyle = FontStyle.Bold;
            Anchor(row.Left.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 0f), new Vector2(-14f, 0f));

            row.Right.text = "";
            HideBar(row);
        }

        private void Line(string text, Color colour)
        {
            var row = RowAt(_index++);
            Place(row, LineHeight);

            row.Left.text = text;
            row.Left.color = colour;
            row.Left.fontStyle = FontStyle.Normal;
            Anchor(row.Left.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 0f), new Vector2(-14f, 0f));

            row.Right.text = "";
            HideBar(row);
        }

        private void Pair(string left, string right) => Pair(left, right, Primary);

        private void Pair(string left, string right, Color rightColour)
        {
            var row = RowAt(_index++);
            Place(row, LineHeight);

            row.Left.text = left;
            row.Left.color = Muted;
            row.Left.fontStyle = FontStyle.Normal;
            Anchor(row.Left.rectTransform, Vector2.zero, new Vector2(0.58f, 1f), new Vector2(20f, 0f), Vector2.zero);

            row.Right.text = right;
            row.Right.color = rightColour;
            row.Right.fontStyle = FontStyle.Bold;
            row.Right.alignment = TextAnchor.MiddleLeft;
            Anchor(row.Right.rectTransform, new Vector2(0.58f, 0f), Vector2.one, Vector2.zero, new Vector2(-14f, 0f));

            HideBar(row);
        }

        private void Bar(string label, string value, float fraction)
        {
            var row = RowAt(_index++);
            Place(row, LineHeight + BarHeight);

            row.Left.text = label;
            row.Left.color = Primary;
            row.Left.fontStyle = FontStyle.Bold;
            Anchor(row.Left.rectTransform, new Vector2(0f, 0.45f), new Vector2(0.58f, 1f),
                   new Vector2(20f, 0f), Vector2.zero);

            row.Right.text = value;
            row.Right.color = Muted;
            row.Right.fontStyle = FontStyle.Normal;
            row.Right.alignment = TextAnchor.MiddleRight;
            Anchor(row.Right.rectTransform, new Vector2(0.58f, 0.45f), Vector2.one,
                   Vector2.zero, new Vector2(-14f, 0f));

            row.BarTrack.gameObject.SetActive(true);
            Anchor(row.BarTrack, new Vector2(0f, 0f), new Vector2(1f, 0.45f),
                   new Vector2(20f, 4f), new Vector2(-14f, -2f));

            row.BarFill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            row.BarFill.offsetMin = Vector2.zero;
            row.BarFill.offsetMax = Vector2.zero;
            row.BarFillImage.color = Accent;
        }

        private void Spacer(float height)
        {
            var row = RowAt(_index++);
            Place(row, height);
            row.Left.text = "";
            row.Right.text = "";
            HideBar(row);
        }

        private static void HideBar(StatRow row) => row.BarTrack.gameObject.SetActive(false);

        private void Place(StatRow row, float height)
        {
            row.Rect.gameObject.SetActive(true);
            row.Rect.sizeDelta = new Vector2(row.Rect.sizeDelta.x, height);
            row.Rect.anchoredPosition = new Vector2(0f, _y);
            _y -= height;
        }

        private void Finish()
        {
            for (var i = _index; i < _rows.Count; i++) _rows[i].Rect.gameObject.SetActive(false);
            _content.sizeDelta = new Vector2(0f, Mathf.Max(0f, -_y) + 12f);
        }

        private StatRow RowAt(int index)
        {
            while (_rows.Count <= index)
            {
                var rect = New(_content, "Row" + _rows.Count);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
                rect.offsetMax = new Vector2(0f, rect.offsetMax.y);

                var track = New(rect, "BarTrack");
                var trackImage = track.gameObject.AddComponent<Image>();
                trackImage.sprite = UiSkin.Pill;
                trackImage.type = Image.Type.Sliced;
                trackImage.color = BarTrack;
                trackImage.raycastTarget = false;

                var fill = New(track, "BarFill");
                fill.anchorMin = new Vector2(0f, 0f);
                fill.anchorMax = new Vector2(0f, 1f);
                fill.offsetMin = Vector2.zero;
                fill.offsetMax = Vector2.zero;

                var fillImage = fill.gameObject.AddComponent<Image>();
                fillImage.sprite = UiSkin.Pill;
                fillImage.type = Image.Type.Sliced;
                fillImage.raycastTarget = false;

                _rows.Add(new StatRow
                {
                    Rect = rect,
                    Left = Label(rect, "Left", "", 17, FontStyle.Normal),
                    Right = Label(rect, "Right", "", 17, FontStyle.Normal),
                    BarTrack = track,
                    BarFill = fill,
                    BarFillImage = fillImage,
                });
            }

            return _rows[index];
        }

        // ---- helpers ----

        private bool Record(string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            Pair(label, value);
            return true;
        }

        private static string RecipeName(PlayerStatistics stats, string id, Func<string, string> name)
        {
            if (string.IsNullOrEmpty(id)) return null;

            RecipeStat stat;
            if (stats.ByRecipe != null && stats.ByRecipe.TryGetValue(id, out stat)
                && stat != null && !string.IsNullOrEmpty(stat.DisplayName))
                return stat.DisplayName;

            return name(id);
        }

        private static string ProductName(PlayerStatistics stats, string id, Func<string, string> name)
        {
            if (string.IsNullOrEmpty(id)) return null;

            ProductStat stat;
            if (stats.ByProduct != null && stats.ByProduct.TryGetValue(id, out stat)
                && stat != null && !string.IsNullOrEmpty(stat.DisplayName))
                return stat.DisplayName;

            return name(id);
        }

        private static List<T> Ordered<T>(Dictionary<string, T> map, Func<T, long> by) =>
            map == null ? new List<T>() : map.Values.Where(v => v != null).OrderByDescending(by).ToList();

        private static string Num(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string Money(double value) => "$" + value.ToString("N2", CultureInfo.InvariantCulture);

        // ---- small builders ----

        private void CloseButton(RectTransform header)
        {
            var rect = New(header, "Close");
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-82f, 4f);
            rect.offsetMax = new Vector2(-8f, -4f);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = UiSkin.Pill;
            image.type = Image.Type.Sliced;
            image.color = ButtonFill;

            var glowRect = New(rect, "Glow");
            Anchor(glowRect, Vector2.zero, Vector2.one, new Vector2(-6f, -6f), new Vector2(6f, 6f));
            glowRect.SetAsFirstSibling();
            var glow = glowRect.gameObject.AddComponent<Image>();
            glow.sprite = UiSkin.Glow;
            glow.type = Image.Type.Sliced;
            glow.raycastTarget = false;

            var text = Label(rect, "Label", "Close", 16, FontStyle.Bold);
            text.alignment = TextAnchor.MiddleCenter;
            Anchor(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            UiInterop.OnClick(button, () => { try { Close(); } catch { } });

            HoverGlow.Attach(rect.gameObject, glow, GlowRest, GlowHot);
        }

        private Text Label(RectTransform parent, string name, string content, int size, FontStyle style)
        {
            var rect = New(parent, name);
            var text = rect.gameObject.AddComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.color = Primary;
            return text;
        }

        private static RectTransform New(RectTransform parent, string name)
        {
            var go = UiInterop.NewRect(name);
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Fill(RectTransform rect) =>
            Anchor(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        // ---- palette, matching the cookbook ----

        private static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);
        private static readonly Color Backdrop = new Color(0.024f, 0.035f, 0.028f, 1f);
        private static readonly Color HeaderFill = new Color(0.042f, 0.062f, 0.050f, 1f);
        private static readonly Color ButtonFill = new Color(0.075f, 0.105f, 0.086f, 1f);
        private static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.05f);

        private static readonly Color Accent = new Color(0.24f, 0.92f, 0.44f, 1f);
        private static readonly Color Primary = new Color(0.93f, 0.96f, 0.94f, 1f);
        private static readonly Color Muted = new Color(0.52f, 0.62f, 0.55f, 1f);

        private static readonly Color GlowRest = new Color(0.24f, 0.92f, 0.44f, 0.05f);
        private static readonly Color GlowHot = new Color(0.30f, 1f, 0.50f, 0.30f);
    }
}
