using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using RecipePlanner.Core.Mixing;
using RecipePlanner.UI;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// The mixing reference: what each ingredient does, and how to reach each effect.
    ///
    /// Two columns rather than nested screens. The list on the left is the thing being browsed and
    /// the panel on the right is the answer, both visible at once — on a phone-sized surface,
    /// drilling in and back out to compare two ingredients is far worse than a little less room for
    /// each.
    ///
    /// The list is built eagerly rather than virtualised like the cookbook's. That list holds every
    /// recipe a player has ever discovered and can run to hundreds; this one holds the game's fixed
    /// set of mixers and effects, which is small enough that the machinery would cost more than it
    /// saves.
    ///
    /// Everything shown is read from the live game — see MixGuideReader. Nothing here is a constant,
    /// because Schedule I can randomise its mix maps per save.
    /// </summary>
    internal sealed class MixGuideScreen
    {
        private const float HeaderHeight = 46f;
        private const float TabsHeight = 38f;
        private const float ListWidth = 0.42f;
        private const float RowHeight = 34f;
        private const float RowGap = 2f;

        private readonly RectTransform _root;
        private readonly Font _font;

        private RectTransform _listContent;
        private RectTransform _detail;
        private Text _detailTitle;
        private Text _tabIngredients;
        private Text _tabEffects;
        private Text _note;

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<Text> _detailLines = new List<Text>();

        private MixGuide _guide;
        private bool _byIngredient = true;
        private string _selectedId;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        private MixGuideScreen(RectTransform root, Font font)
        {
            _root = root;
            _font = font;
        }

        public static MixGuideScreen CreateInto(RectTransform parent, Font font)
        {
            var root = New(parent, "MixGuide");
            Fill(root);

            // The backdrop is its own child rather than an Image on the root, and it is fully
            // opaque. This is a full-screen takeover: any translucency at all lets the cookbook's
            // rows read straight through the guide's panels, which is unreadable rather than
            // stylish. It also swallows clicks meant for the list underneath.
            var backdropRect = New(root, "Backdrop");
            Fill(backdropRect);
            backdropRect.SetAsFirstSibling();

            var backdrop = backdropRect.gameObject.AddComponent<Image>();
            backdrop.color = Backdrop;
            backdrop.raycastTarget = true;

            var screen = new MixGuideScreen(root, font);
            screen.Build();
            root.gameObject.SetActive(false);
            return screen;
        }

        // ---- construction ----

        private void Build()
        {
            var header = New(_root, "Header");
            Anchor(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -HeaderHeight), Vector2.zero);
            var headerImage = header.gameObject.AddComponent<Image>();
            headerImage.color = HeaderFill;

            var title = Label(header, "Title", "Mix Guide", 24, FontStyle.Bold);
            title.color = Accent;
            Anchor(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 0f), new Vector2(-90f, 0f));

            Button(header, "Close", "Close", new Vector2(1f, 0.5f), -82f, 74f, () => Close());

            var tabs = New(_root, "Tabs");
            Anchor(tabs, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(8f, -HeaderHeight - TabsHeight), new Vector2(-8f, -HeaderHeight - 2f));

            _tabIngredients = Button(tabs, "TabIngredients", "By ingredient", new Vector2(0f, 0.5f), 8f, 150f,
                                     () => { _byIngredient = true; _selectedId = null; Populate(); });
            _tabEffects = Button(tabs, "TabEffects", "By effect", new Vector2(0f, 0.5f), 166f, 130f,
                                 () => { _byIngredient = false; _selectedId = null; Populate(); });

            _note = Label(_root, "Note", "", 14, FontStyle.Italic);
            _note.color = Muted;
            _note.alignment = TextAnchor.MiddleRight;
            Anchor(_note.rectTransform, new Vector2(0.4f, 1f), new Vector2(1f, 1f),
                   new Vector2(0f, -HeaderHeight - TabsHeight), new Vector2(-10f, -HeaderHeight - 2f));

            BuildList();
            BuildDetail();
        }

        private void BuildList()
        {
            var viewport = New(_root, "ListViewport");
            Anchor(viewport, new Vector2(0f, 0f), new Vector2(ListWidth, 1f),
                   new Vector2(8f, 8f), new Vector2(-4f, -HeaderHeight - TabsHeight - 4f));

            var catcher = viewport.gameObject.AddComponent<Image>();
            catcher.color = PanelFill;
            catcher.raycastTarget = true;

            viewport.gameObject.AddComponent<RectMask2D>();

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 0f;

            var smooth = viewport.gameObject.AddComponent<SmoothScroll>();
            smooth.Target = scroll;
            smooth.StepPixels = (RowHeight + RowGap) * 3f;
            smooth.SmoothTime = 0.16f;

            _listContent = New(viewport, "Content");
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta = Vector2.zero;

            scroll.viewport = viewport;
            scroll.content = _listContent;
        }

        private void BuildDetail()
        {
            _detail = New(_root, "Detail");
            Anchor(_detail, new Vector2(ListWidth, 0f), new Vector2(1f, 1f),
                   new Vector2(4f, 8f), new Vector2(-8f, -HeaderHeight - TabsHeight - 4f));

            var fill = _detail.gameObject.AddComponent<Image>();
            fill.sprite = UiSkin.Body;
            fill.type = Image.Type.Sliced;
            fill.color = PanelFill;
            fill.raycastTarget = false;

            _detailTitle = Label(_detail, "DetailTitle", "", 22, FontStyle.Bold);
            _detailTitle.color = Accent;
            Anchor(_detailTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(14f, -42f), new Vector2(-14f, -10f));
        }

        // ---- data ----

        public void Open()
        {
            _guide = null;
            try { _guide = RecipePlannerUI.MixGuideSource?.Invoke(); }
            catch (Exception ex) { RecipePlannerUI.Log?.Warn("Mix guide unavailable: " + ex.Message); }

            _selectedId = null;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Populate();
        }

        public void Close() => _root.gameObject.SetActive(false);

        private void Populate()
        {
            RefreshTabs();

            foreach (var row in _rows) row.Rect.gameObject.SetActive(false);

            if (_guide == null || !_guide.IsUsable)
            {
                _note.text = "";
                ShowDetail("Nothing to show",
                    new[] { "The mixing data could not be read from the game.",
                            "Load a save and open this again." });
                _listContent.sizeDelta = Vector2.zero;
                return;
            }

            _note.text = !_guide.TransformsAvailable
                ? "transformations unavailable"
                : _guide.TransformsApproximate ? "transformations derived locally" : "";

            var y = 0f;
            var index = 0;

            if (_byIngredient)
            {
                foreach (var ingredient in _guide.IngredientsByPrice())
                {
                    var effect = _guide.Effect(ingredient.EffectId);
                    BindRow(index++, ref y,
                            ingredient.Name ?? ingredient.Id,
                            effect != null ? effect.Name : "—",
                            effect != null ? Colour(effect) : Muted,
                            ingredient.Id);
                }
            }
            else
            {
                foreach (var effect in _guide.EffectsByTier())
                {
                    BindRow(index++, ref y,
                            effect.Name ?? effect.Id,
                            "T" + effect.Tier.ToString(CultureInfo.InvariantCulture),
                            Colour(effect),
                            effect.Id);
                }
            }

            _listContent.sizeDelta = new Vector2(0f, Mathf.Max(0f, -y));

            // Something selected on open, so the right-hand panel is never a blank rectangle the
            // player has to guess at.
            if (_selectedId == null && index > 0) Select(FirstId());
            else if (_selectedId != null) Select(_selectedId);
        }

        private string FirstId()
        {
            if (_byIngredient)
            {
                var list = _guide.IngredientsByPrice();
                return list.Count > 0 ? list[0].Id : null;
            }

            var effects = _guide.EffectsByTier();
            return effects.Count > 0 ? effects[0].Id : null;
        }

        /// <summary>
        /// One list row, with its parts held directly.
        ///
        /// Deliberately not looked up with GetComponentsInChildren and indexed: that silently
        /// depends on child creation order, so adding an icon later would swap the two labels
        /// without a compiler error and without an obvious break.
        /// </summary>
        private sealed class Row
        {
            public RectTransform Rect;
            public Image Background;
            public Text Left;
            public Text Right;
            public Button Click;
        }

        private void BindRow(int index, ref float y, string left, string right, Color rightColour, string id)
        {
            var row = RowAt(index);
            row.Rect.gameObject.SetActive(true);

            // Height first, then position. The horizontal insets are set once at construction and
            // must NOT be reapplied here: with anchorMin.y == anchorMax.y, offsetMin.y and
            // offsetMax.y ARE the vertical position and height, so assigning them zeroed both and
            // stacked every row on top of the first. The list looked like it held one item when it
            // held thirty-five.
            row.Rect.sizeDelta = new Vector2(row.Rect.sizeDelta.x, RowHeight);
            row.Rect.anchoredPosition = new Vector2(0f, y);

            row.Left.text = left;
            row.Right.text = right;
            row.Right.color = rightColour;

            row.Background.color = string.Equals(id, _selectedId, StringComparison.OrdinalIgnoreCase)
                ? RowSelected : RowIdle;

            row.Click.onClick.RemoveAllListeners();
            var captured = id;
            row.Click.onClick.AddListener(() => { try { Select(captured); Populate(); } catch { } });

            y -= RowHeight + RowGap;
        }

        private Row RowAt(int index)
        {
            while (_rows.Count <= index)
            {
                var rect = New(_listContent, "Row" + _rows.Count);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);

                // Horizontal insets, set once. Stretched across the width, so these are the only
                // offsets that mean what they look like — the vertical pair is position and height.
                rect.offsetMin = new Vector2(4f, rect.offsetMin.y);
                rect.offsetMax = new Vector2(-4f, rect.offsetMax.y);

                var image = rect.gameObject.AddComponent<Image>();
                image.sprite = UiSkin.Body;
                image.type = Image.Type.Sliced;
                image.color = RowIdle;

                var left = Label(rect, "Left", "", 17, FontStyle.Normal);
                left.color = Primary;
                Anchor(left.rectTransform, Vector2.zero, new Vector2(0.66f, 1f), new Vector2(10f, 0f), Vector2.zero);

                var right = Label(rect, "Right", "", 16, FontStyle.Bold);
                right.alignment = TextAnchor.MiddleRight;
                Anchor(right.rectTransform, new Vector2(0.66f, 0f), Vector2.one, Vector2.zero, new Vector2(-10f, 0f));

                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.None;

                _rows.Add(new Row
                {
                    Rect = rect, Background = image, Left = left, Right = right, Click = button,
                });
            }

            return _rows[index];
        }

        // ---- detail ----

        private void Select(string id)
        {
            _selectedId = id;
            if (_guide == null || id == null) return;

            if (_byIngredient) ShowIngredient(id);
            else ShowEffect(id);
        }

        private void ShowIngredient(string ingredientId)
        {
            IngredientInfo ingredient = null;
            foreach (var candidate in _guide.Ingredients)
                if (candidate != null && string.Equals(candidate.Id, ingredientId, StringComparison.OrdinalIgnoreCase))
                { ingredient = candidate; break; }

            if (ingredient == null) return;

            var lines = new List<string>();
            var effect = _guide.Effect(ingredient.EffectId);

            lines.Add("Costs $" + ingredient.Price.ToString("N2", CultureInfo.InvariantCulture));
            lines.Add(effect != null ? "Adds: " + effect.Name : "Adds no effect of its own");
            lines.Add("");

            var changes = _guide.ByIngredient(ingredientId);
            if (!_guide.TransformsAvailable)
            {
                lines.Add("Transformations could not be read from this save.");
            }
            else if (changes.Count == 0)
            {
                lines.Add("Changes nothing that is already on the product.");
            }
            else
            {
                lines.Add("Changes what is already there:");
                foreach (var change in changes)
                    lines.Add("   " + _guide.EffectName(change.FromEffectId)
                              + "  ->  " + _guide.EffectName(change.ToEffectId));
            }

            ShowDetail(ingredient.Name ?? ingredient.Id, lines);
        }

        private void ShowEffect(string effectId)
        {
            var effect = _guide.Effect(effectId);
            if (effect == null) return;

            var lines = new List<string>();
            if (!string.IsNullOrEmpty(effect.Description)) { lines.Add(effect.Description); lines.Add(""); }

            lines.Add("Tier " + effect.Tier.ToString(CultureInfo.InvariantCulture)
                      + "     Addictiveness " + Percent(effect.Addictiveness));

            if (effect.ValueChange != 0 || effect.ValueMultiplier != 0f)
                lines.Add("Value  x" + effect.ValueMultiplier.ToString("0.00", CultureInfo.InvariantCulture)
                          + (effect.ValueChange != 0
                             ? "   +$" + effect.ValueChange.ToString(CultureInfo.InvariantCulture)
                             : ""));
            lines.Add("");

            var routes = _guide.RoutesTo(effectId);

            if (routes.AddedDirectlyBy.Count > 0)
            {
                lines.Add("Added directly by:");
                foreach (var ingredient in routes.AddedDirectlyBy)
                    lines.Add("   " + (ingredient.Name ?? ingredient.Id)
                              + "   $" + ingredient.Price.ToString("N2", CultureInfo.InvariantCulture));
                lines.Add("");
            }

            if (!_guide.TransformsAvailable)
            {
                lines.Add("Transformations could not be read from this save.");
            }
            else if (routes.ConvertedFrom.Count > 0)
            {
                lines.Add("Or convert an existing effect:");
                foreach (var route in routes.ConvertedFrom)
                    lines.Add("   " + _guide.EffectName(route.FromEffectId)
                              + "  +  " + IngredientName(route.IngredientId));
            }
            else if (routes.AddedDirectlyBy.Count == 0)
            {
                lines.Add("Nothing in this save reaches this effect.");
            }

            ShowDetail(effect.Name ?? effect.Id, lines);
        }

        private string IngredientName(string id)
        {
            foreach (var ingredient in _guide.Ingredients)
                if (ingredient != null && string.Equals(ingredient.Id, id, StringComparison.OrdinalIgnoreCase))
                    return ingredient.Name ?? ingredient.Id;
            return id;
        }

        private void ShowDetail(string title, IEnumerable<string> lines)
        {
            _detailTitle.text = title;

            var index = 0;
            var y = -50f;

            foreach (var line in lines)
            {
                var label = DetailLineAt(index++);
                label.gameObject.SetActive(true);
                label.text = line;
                label.color = line.Contains("->") || line.StartsWith("   ") ? Primary : Muted;
                Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(14f, y - 22f), new Vector2(-14f, y));
                y -= 24f;
            }

            for (var i = index; i < _detailLines.Count; i++) _detailLines[i].gameObject.SetActive(false);
        }

        private Text DetailLineAt(int index)
        {
            while (_detailLines.Count <= index)
                _detailLines.Add(Label(_detail, "Line" + _detailLines.Count, "", 16, FontStyle.Normal));

            return _detailLines[index];
        }

        private void RefreshTabs()
        {
            _tabIngredients.color = _byIngredient ? Accent : Muted;
            _tabEffects.color = _byIngredient ? Muted : Accent;
        }

        private static string Percent(float value) =>
            Mathf.RoundToInt(value * 100f).ToString(CultureInfo.InvariantCulture) + "%";

        /// <summary>The game's own label colour for an effect, so the guide matches the Products screen.</summary>
        private static Color Colour(EffectInfo effect) =>
            effect == null ? Primary : new Color(effect.ColourR, effect.ColourG, effect.ColourB, 1f);

        // ---- small builders ----

        private Text Button(RectTransform parent, string name, string label,
                            Vector2 pivot, float x, float width, Action onClick)
        {
            var rect = New(parent, name);
            rect.anchorMin = new Vector2(pivot.x, 0f);
            rect.anchorMax = new Vector2(pivot.x, 1f);
            rect.pivot = new Vector2(pivot.x, 0.5f);
            rect.offsetMin = new Vector2(x, 3f);
            rect.offsetMax = new Vector2(x + width, -3f);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = UiSkin.Body;
            image.type = Image.Type.Sliced;
            image.color = ButtonFill;

            var glowRect = New(rect, "Glow");
            Anchor(glowRect, Vector2.zero, Vector2.one, new Vector2(-6f, -6f), new Vector2(6f, 6f));
            glowRect.SetAsFirstSibling();
            var glow = glowRect.gameObject.AddComponent<Image>();
            glow.sprite = UiSkin.Glow;
            glow.type = Image.Type.Sliced;
            glow.raycastTarget = false;

            var text = Label(rect, "Label", label, 16, FontStyle.Bold);
            text.alignment = TextAnchor.MiddleCenter;
            Anchor(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { try { onClick(); } catch { } });

            HoverGlow.Attach(rect.gameObject, glow, GlowRest, GlowHot);
            return text;
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
            var go = new GameObject(name, typeof(RectTransform));
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

        // ---- palette, matching the cookbook's green ----

        // Fully opaque throughout. The guide covers the cookbook completely, so anything
        // translucent lets the list read through it — legible as a design idea, illegible in fact.
        private static readonly Color Backdrop = new Color(0.035f, 0.055f, 0.042f, 1f);
        private static readonly Color HeaderFill = new Color(0.07f, 0.15f, 0.10f, 1f);
        private static readonly Color PanelFill = new Color(0.065f, 0.088f, 0.072f, 1f);
        private static readonly Color ButtonFill = new Color(0.13f, 0.19f, 0.15f, 1f);
        private static readonly Color RowIdle = new Color(0.10f, 0.135f, 0.113f, 1f);
        private static readonly Color RowSelected = new Color(0.16f, 0.34f, 0.22f, 1f);

        private static readonly Color Accent = new Color(0.62f, 0.96f, 0.68f, 1f);
        private static readonly Color Primary = new Color(0.93f, 0.95f, 0.93f, 1f);
        private static readonly Color Muted = new Color(0.60f, 0.68f, 0.62f, 1f);

        private static readonly Color GlowRest = new Color(0.35f, 0.85f, 0.45f, 0.07f);
        private static readonly Color GlowHot = new Color(0.42f, 1f, 0.55f, 0.26f);
    }
}
