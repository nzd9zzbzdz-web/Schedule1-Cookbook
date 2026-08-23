using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using RecipePlanner.Core.Recipes;
using RecipePlanner.UI;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// Builds and populates the Cookbook screen inside the app container the phone gives us.
    ///
    /// Layout is constructed in code rather than from a prefab, because a mod has no asset bundle
    /// to ship one in. Fonts and colours are lifted from whatever the phone is already using so it
    /// matches the rest of the UI instead of looking bolted on.
    ///
    /// Navigation is a strip of strain tiles across the top — the bud artwork is what a player
    /// recognises, not a name in a list — and the recipes for the selected strain fill the space
    /// below it. One strain at a time is what makes eighty-odd recipes navigable.
    ///
    /// The list is <b>virtualised</b>: rows exist only for the slice actually on screen and are
    /// recycled as it scrolls. An earlier version built every row eagerly under a
    /// VerticalLayoutGroup with a ContentSizeFitter, which meant a full layout pass over ninety
    /// rows of six to eight objects each whenever anything changed. It locked the game up. Nothing
    /// here uses a layout group: rows sit at absolute offsets, and because every row is the same
    /// height the visible slice is arithmetic rather than a search.
    /// </summary>
    public sealed class CookbookScreen
    {
        private readonly RectTransform _root;
        private RectTransform _viewport;
        private RectTransform _listContent;
        private RectTransform _strip;
        private ScrollRect _scroll;
        private SmoothScroll _smooth;
        private Text _header;
        private Text _caption;

        private static Font _font;

        private CookbookScreen(RectTransform root)
        {
            _root = root;
        }

        public static CookbookScreen CreateInto(RectTransform appContainer)
        {
            // Everything goes inside ONE root rather than as loose siblings. If the template left a
            // LayoutGroup on the container it would override the position of every direct child,
            // so the screen owns a single child it fully controls the inside of.
            var root = new GameObject("CookbookRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(appContainer, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            ReportContainerLayout(appContainer);

            // If a layout group IS driving the container, make sure our root is told to fill it.
            if (appContainer.GetComponent<LayoutGroup>() != null)
            {
                var element = root.gameObject.AddComponent<LayoutElement>();
                element.flexibleWidth = 1f;
                element.flexibleHeight = 1f;
                element.preferredWidth = appContainer.rect.width;
                element.preferredHeight = appContainer.rect.height;
            }

            var screen = new CookbookScreen(root);
            screen.Build();
            return screen;
        }

        private static void ReportContainerLayout(RectTransform container)
        {
            try
            {
                var components = new List<string>();
                foreach (var component in container.GetComponents<Component>())
                    if (component != null) components.Add(component.GetType().Name);

                RecipePlannerUI.Log?.Info(
                    $"Cookbook: container components = {string.Join(", ", components.ToArray())}");
            }
            catch { /* diagnostics must never make things worse */ }
        }

        /// <summary>
        /// Borrows a font already present in the phone UI. Falls back to Unity's built-in Arial,
        /// which always exists, rather than risking a null font and an invisible screen.
        /// </summary>
        private static Font ResolveFont(RectTransform context)
        {
            if (_font != null) return _font;

            var existing = context != null ? context.GetComponentInChildren<Text>(true) : null;
            _font = existing != null && existing.font != null
                ? existing.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");

            return _font;
        }

        // ---------- metrics ----------
        // The phone renders small on screen, so everything is sized for that rather than for how it
        // looks in a full-size mock-up.

        private const float TitleHeight = 52f;
        private const float ToolbarTop = -56f;
        private const float ToolbarBottom = -100f;
        private const float StripTop = -104f;
        private const float StripBottom = -206f;
        private const float CaptionTop = -208f;
        private const float CaptionBottom = -236f;
        private const float ListTopInset = -240f;

        private const float EntryHeight = 74f;
        private const float RowGap = 1f;
        private const float ListTopPad = 6f;
        private const float IconSize = 58f;
        private const float ChainIconSize = 30f;
        private const float SidePadding = 10f;

                private const float TileIconSize = 72f;
        private const float TileGap = 6f;
        private const float TileMinWidth = 46f;
        private const float TileMaxWidth = 96f;
        private const float TileInset = 5f;
        private const float TileBorder = 2f;

        private const int NameFontSize = 22;
        private const int StatFontSize = 18;
        private const int PriceFontSize = 23;
        private const int CaptionFontSize = 20;
        private const int TileFontSize = 15;
        private const int TileCountFontSize = 13;
        private const int ToolFontSize = 16;
        private const int TitleFontSize = 26;

        /// <summary>Additive slots kept on a pooled row. Longer chains collapse into a "+N" tail.</summary>
        private const int MaxChainSlots = 8;

        /// <summary>Rows built beyond the viewport edge, so a fast flick does not show a gap.</summary>
        private const float WindowBuffer = 120f;

        private static float RowPitch => EntryHeight + RowGap;

        private void Build()
        {
            var font = ResolveFont(_root);

            // The app must paint its own ground. Without this the panel is transparent and you see
            // straight through the phone into the room.
            var background = CreateChild(_root, "CookbookBackground");
            Anchor(background, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var backgroundImage = background.gameObject.AddComponent<Image>();
            backgroundImage.color = new Color(0.10f, 0.10f, 0.12f, 1f);
            background.SetAsFirstSibling();

            var titleBar = CreateChild(_root, "CookbookTitleBar");
            Anchor(titleBar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -TitleHeight), Vector2.zero);
            var titleImage = titleBar.gameObject.AddComponent<Image>();
            titleImage.color = new Color(0.42f, 0.13f, 0.15f, 1f);   // matches the game's app headers

            AddGloss(titleBar, 0.18f, 0.22f);

            _header = CreateText(titleBar, "CookbookHeader", "Cookbook", font, TitleFontSize, FontStyle.Bold);
            Anchor(_header.rectTransform, Vector2.zero, Vector2.one,
                   new Vector2(14f, 0f), new Vector2(-14f, 0f));

            BuildToolbar(font);

            _strip = CreateChild(_root, "StrainStrip");
            Anchor(_strip, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(8f, StripBottom), new Vector2(-8f, StripTop));

            _caption = CreateText(_root, "Caption", "", font, CaptionFontSize, FontStyle.Bold);
            _caption.color = HeaderText;
            Anchor(_caption.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(12f, CaptionBottom), new Vector2(-12f, CaptionTop));

            _viewport = CreateChild(_root, "CookbookViewport");
            Anchor(_viewport, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, ListTopInset));

            // An invisible Graphic covering the viewport is what makes the list scrollable.
            //
            // The EventSystem delivers scroll and drag events to whatever the raycaster hits, and
            // the event bubbles to the first ancestor that handles it. With nothing raycastable on
            // the viewport, a wheel over the list body hit the app's background image instead — a
            // SIBLING of the viewport, not a child — so it bubbled to the phone and never reached
            // this ScrollRect. Alpha is irrelevant to raycasting; only raycastTarget is.
            var catcher = _viewport.gameObject.AddComponent<Image>();
            catcher.color = Transparent;
            catcher.raycastTarget = true;

            _viewport.gameObject.AddComponent<RectMask2D>();

            _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            // Zero, deliberately: SmoothScroll owns the wheel. Any value here reintroduces the
            // instant per-notch jump on top of the momentum, and the two fight each other.
            _scroll.scrollSensitivity = 0f;

            // Inertia off: SmoothScroll eases the wheel to an exact target, and ScrollRect's own
            // deceleration would drift past it. Dragging still works — SmoothScroll stands aside
            // for the duration of a drag.
            _scroll.inertia = false;

            _smooth = _viewport.gameObject.AddComponent<SmoothScroll>();
            _smooth.Target = _scroll;

            // Exactly one recipe per wheel notch, eased. Expressed in rows rather than pixels so
            // it stays honest if the row height changes.
            _smooth.StepPixels = RowPitch;
            _smooth.SmoothTime = 0.18f;
            _smooth.Moved = UpdateWindow;

            _listContent = CreateChild(_viewport, "Content");
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta = Vector2.zero;

            // NO nested Canvas here. It was tried, to confine uGUI batch rebuilds to the list rather
            // than dirtying the phone's whole canvas on every scroll step, and it made the list
            // invisible: rows were constructed, bound and active — the instrumentation counted ten
            // live — but nothing drew. A sub-canvas under this RectMask2D viewport does not survive
            // the phone's rendering setup. Left out deliberately; do not add it back without
            // checking the list still renders.

            _scroll.viewport = _viewport;
            _scroll.content = _listContent;

            // The only work that happens while scrolling. Rebinding a handful of pooled rows is
            // cheap; rebuilding a layout tree of a thousand objects was not.
            _scroll.onValueChanged.AddListener(_ => UpdateWindow());

            PrewarmPool(font);
            DiagnoseVisibility(backgroundImage);
        }

        /// <summary>
        /// Builds a viewport's worth of rows up front, inactive, so the pool is never empty when the
        /// player first picks a strain.
        ///
        /// Measured: constructing eight entry rows on demand cost 6.7ms — one dropped frame, at the
        /// exact moment the list was supposed to open. The work is unavoidable, but paying it while
        /// the app is animating in is free, and after this rows are only ever rebound.
        /// </summary>
        private void PrewarmPool(Font font)
        {
            try
            {
                var rows = Mathf.CeilToInt((ViewportHeight() + WindowBuffer * 2f) / EntryHeight) + 1;

                for (var i = 0; i < rows; i++)
                {
                    var view = EntryView.Create(this, _listContent, font);
                    _rowsConstructed++;

                    // Never leave a freshly built row active: it has no content and sits at the
                    // origin at full width, so it would draw as a bar across the top of the list.
                    view.Root.gameObject.SetActive(false);
                    _entryPool.Push(view);
                }
            }
            catch (Exception ex)
            {
                // A cold pool is slower, not broken — never fail the screen over an optimisation.
                RecipePlannerUI.Log?.Warn("Cookbook pool prewarm failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Reports why a correctly-built, correctly-sized, active screen might still not be on
        /// screen. Everything structural already checked out, so the cause has to be one of the
        /// things below — and each needs a different fix, so guessing is wasteful.
        /// </summary>
        private void DiagnoseVisibility(Image background)
        {
            try
            {
                var report = new StringBuilder("Cookbook visibility:\n");

                report.Append($"  background: enabled={background.enabled} color={background.color} ")
                      .Append($"rendererAlpha={background.canvasRenderer.GetAlpha():0.##} ")
                      .Append($"rect={background.rectTransform.rect.width:0}x{background.rectTransform.rect.height:0}\n");

                // A CanvasGroup anywhere above us can zero the whole subtree out.
                foreach (var group in _root.GetComponentsInParent<CanvasGroup>(true))
                    report.Append($"  CanvasGroup '{group.name}': alpha={group.alpha:0.##} ")
                          .Append($"active={group.gameObject.activeInHierarchy}\n");

                var canvas = _root.GetComponentInParent<Canvas>();
                report.Append(canvas != null
                    ? $"  Canvas '{canvas.name}': enabled={canvas.enabled} mode={canvas.renderMode} " +
                      $"sorting={canvas.sortingOrder}\n"
                    : "  Canvas: NONE IN PARENTS — nothing under this transform can render.\n");

                RecipePlannerUI.Log?.Info(report.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                RecipePlannerUI.Log?.Warn("Visibility diagnostics failed: " + ex.Message);
            }
        }

        // ---------- model + selection ----------

        private CookbookViewModel _model;
        private Text _placeholder;

        /// <summary>
        /// Root product id of the selected strain, or null for "every strain at once".
        ///
        /// Kept across refreshes so hiding a recipe or changing the sort does not throw the player
        /// back to the top of the cookbook.
        /// </summary>
        private string _selectedRoot;

        private bool _hasSelection;

        /// <summary>Entries currently listed. Uniform height, so position is index arithmetic.</summary>
        private readonly List<CookbookEntry> _entries = new List<CookbookEntry>();

        public void Refresh()
        {
            var font = ResolveFont(_root);

            _model = RecipePlannerUI.DataSource?.Invoke();
            RefreshToolbar(_model);

            if (_model == null || _model.IsEmpty)
            {
                ReleaseAll();
                _entries.Clear();
                _listContent.sizeDelta = Vector2.zero;
                ClearStrip();
                _caption.text = "";
                ShowPlaceholder(font, "No recipes recorded yet — cook something and it will appear here.");
                if (_header != null) _header.text = "Cookbook";
                return;
            }

            ShowPlaceholder(font, null);

            if (_header != null)
                _header.text = $"Cookbook — {_model.TotalRecipes} recipes · {_model.ProfileLabel}";

            RefreshStrip(font);
            Relayout();
        }

        /// <summary>Sections in the model, with the selected one resolved. Null when "all".</summary>
        private CookbookSection SelectedSection()
        {
            if (!_hasSelection || _selectedRoot == null || _model?.Sections == null) return null;

            foreach (var section in _model.Sections)
                if (section != null &&
                    string.Equals(section.RootProductId, _selectedRoot, StringComparison.OrdinalIgnoreCase))
                    return section;

            return null;
        }

        private void SelectStrain(string rootProductId, bool hasSelection)
        {
            _selectedRoot = rootProductId;
            _hasSelection = hasSelection;

            UpdateTileHighlights();
            Relayout();
        }

        /// <summary>
        /// Rebuilds the entry list for the current selection, then redraws the visible slice.
        /// </summary>
        private void Relayout()
        {
            _entries.Clear();

            var section = SelectedSection();
            if (section != null)
            {
                foreach (var entry in section.Entries)
                    if (entry != null) _entries.Add(entry);

                _caption.text = $"{Title(section)}   ·   {section.Count} recipes";
            }
            else if (_model?.Sections != null)
            {
                foreach (var s in _model.Sections)
                {
                    if (s?.Entries == null) continue;
                    foreach (var entry in s.Entries)
                        if (entry != null) _entries.Add(entry);
                }

                // A selection that no longer exists — the strain's last recipe was hidden — falls
                // back to showing everything rather than an empty list with no way out.
                _caption.text = $"ALL STRAINS   ·   {_entries.Count} recipes";
            }

            _listContent.sizeDelta = new Vector2(0f, ListTopPad + _entries.Count * RowPitch + 8f);

            // Every live row must go back to the pool. Rows are tracked by their index, and the
            // list they index into has just been rebuilt — a row whose index happens to stay inside
            // the visible range would otherwise never be rebound, and would keep drawing the
            // previous strain's product at the previous offset.
            ReleaseAll();

            // Clamp the scroll offset BEFORE drawing, every time the list changes.
            //
            // Switching from a strain with twenty-three recipes to one with one leaves the content
            // scrolled hundreds of pixels past its new end. The visible window is then computed
            // beyond the last row, so nothing binds: a correct title, a correct strip, and a
            // completely empty list with no placeholder, because the model was never empty.
            // ScrollRect does settle itself eventually, but only in response to an input it may
            // never receive.
            var maximum = Mathf.Max(0f, _listContent.sizeDelta.y - ViewportHeight());
            var position = _listContent.anchoredPosition;
            _listContent.anchoredPosition = new Vector2(position.x, Mathf.Clamp(position.y, 0f, maximum));

            // A glide aimed at the old list's length would fight the clamp above.
            _smooth?.Cancel();

            UpdateWindow();
            ReportFirstDraw();
        }

        private static string Title(CookbookSection section) =>
            (section.DisplayName ?? section.RootProductId ?? "?").ToUpperInvariant();

        private float ViewportHeight()
        {
            var height = _viewport != null ? _viewport.rect.height : 0f;

            // rect is zero until the first layout pass; fall back to the space the viewport was
            // anchored into, so the very first draw is not empty.
            if (height <= 1f) height = Mathf.Max(1f, _root.rect.height + ListTopInset - 8f);
            return height;
        }

        // ---------- the strain strip ----------

        private readonly List<StrainTile> _tiles = new List<StrainTile>();
        private string _stripSignature;

        /// <summary>
        /// Rebuilds the tile strip, but only when the set of strains actually changes.
        ///
        /// Refresh runs on every sort, filter and hide, and destroying and recreating nine tiles
        /// each time is both wasteful and visibly flickery. The signature is the section list, so
        /// a strain appearing or vanishing rebuilds and nothing else does.
        /// </summary>
        private void RefreshStrip(Font font)
        {
            var signature = BuildStripSignature();
            if (signature == _stripSignature)
            {
                UpdateTileHighlights();
                return;
            }

            _stripSignature = signature;
            ClearStrip();

            var sections = _model?.Sections;
            if (sections == null) return;

            var count = sections.Count + 1;   // + the "all strains" tile
            var available = Mathf.Max(1f, _strip.rect.width > 1f ? _strip.rect.width : _root.rect.width - 16f);
            var width = Mathf.Clamp((available - TileGap * (count - 1)) / count, TileMinWidth, TileMaxWidth);

            var x = 0f;
            _tiles.Add(StrainTile.Create(this, _strip, font, null, "ALL", _model.TotalRecipes, x, width));
            x += width + TileGap;

            foreach (var section in sections)
            {
                if (section == null) continue;

                _tiles.Add(StrainTile.Create(
                    this, _strip, font, section.RootProductId, Title(section), section.Count, x, width));
                x += width + TileGap;
            }

            UpdateTileHighlights();
        }

        private string BuildStripSignature()
        {
            if (_model?.Sections == null) return "";

            var builder = new StringBuilder();
            foreach (var section in _model.Sections)
            {
                if (section == null) continue;
                builder.Append(section.RootProductId).Append(':').Append(section.Count).Append('|');
            }
            return builder.ToString();
        }

        private void ClearStrip()
        {
            foreach (var tile in _tiles)
                if (tile?.Root != null) UnityEngine.Object.Destroy(tile.Root.gameObject);

            _tiles.Clear();
        }

        private void UpdateTileHighlights()
        {
            foreach (var tile in _tiles)
            {
                if (tile == null) continue;

                var selected = tile.RootProductId == null
                    ? !_hasSelection
                    : _hasSelection && string.Equals(tile.RootProductId, _selectedRoot,
                                                     StringComparison.OrdinalIgnoreCase);
                tile.SetSelected(selected);
            }
        }

        // ---------- virtualisation ----------

        private readonly Dictionary<int, EntryView> _live = new Dictionary<int, EntryView>();
        private readonly Stack<EntryView> _entryPool = new Stack<EntryView>();
        private readonly List<int> _retired = new List<int>();

        // Instrumentation. Three rounds of "fix the rendering" earlier in this project were lost to
        // confident reasoning with no measurement, so the scroll path reports its own cost. Silent
        // unless a frame is actually slow, and throttled so it can never become the problem.
        private static readonly System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();
        private const double SlowFrameMs = 4.0;
        private float _nextReportTime;
        private int _rowsConstructed;

        private void UpdateWindow()
        {
            Clock.Reset();
            Clock.Start();
            var constructedBefore = _rowsConstructed;

            UpdateWindowCore();

            Clock.Stop();
            var elapsed = Clock.Elapsed.TotalMilliseconds;

            if (elapsed >= SlowFrameMs && Time.unscaledTime >= _nextReportTime)
            {
                _nextReportTime = Time.unscaledTime + 2f;
                RecipePlannerUI.Log?.Warn(
                    $"Cookbook list update took {elapsed:0.0}ms — {_live.Count} rows live, " +
                    $"{_rowsConstructed - constructedBefore} newly constructed, " +
                    $"pool: {_entryPool.Count}, {_entries.Count} entries total.");
            }
        }

        /// <summary>
        /// Binds rows for the slice of the list inside the viewport (plus a buffer) and returns
        /// everything else to the pool. Every row is the same height, so the slice is arithmetic.
        /// </summary>
        private void UpdateWindowCore()
        {
            if (_entries.Count == 0)
            {
                ReleaseAll();
                return;
            }

            var scrollTop = _listContent.anchoredPosition.y;
            var top = scrollTop - WindowBuffer;
            var bottom = scrollTop + ViewportHeight() + WindowBuffer;

            var first = Mathf.Clamp(Mathf.FloorToInt((top - ListTopPad) / RowPitch), 0, _entries.Count);
            var last = Mathf.Clamp(Mathf.CeilToInt((bottom - ListTopPad) / RowPitch) + 1, first, _entries.Count);

            // Retire before acquiring, so the pool is warm. The other order allocates a fresh row
            // for every scroll step instead of reusing the one that just left the viewport.
            _retired.Clear();
            foreach (var pair in _live)
                if (pair.Key < first || pair.Key >= last) _retired.Add(pair.Key);

            foreach (var index in _retired)
            {
                Release(_live[index]);
                _live.Remove(index);
            }

            for (var i = first; i < last; i++)
            {
                if (_live.ContainsKey(i)) continue;

                EntryView row;
                if (_entryPool.Count > 0) row = _entryPool.Pop();
                else { row = EntryView.Create(this, _listContent, ResolveFont(_root)); _rowsConstructed++; }

                row.Bind(_entries[i], i);
                row.Place(ListTopPad + i * RowPitch, EntryHeight);
                _live[i] = row;
            }
        }

        private void Release(EntryView view)
        {
            if (view?.Root == null) return;
            view.Root.gameObject.SetActive(false);
            _entryPool.Push(view);
        }

        private void ReleaseAll()
        {
            foreach (var pair in _live) Release(pair.Value);
            _live.Clear();
        }

        private bool _reportedFirstDraw;

        /// <summary>
        /// Describes the first drawn list once per session.
        ///
        /// "The list is empty" has now had two completely different causes — a scroll offset past
        /// the end, and rows that were built and active but never rendered. Those look identical in
        /// a screenshot and need opposite fixes, so the state that distinguishes them gets written
        /// down rather than guessed at.
        /// </summary>
        private void ReportFirstDraw()
        {
            if (_reportedFirstDraw) return;
            _reportedFirstDraw = true;

            try
            {
                var report = new StringBuilder("Cookbook first draw: ");
                report.Append($"{_tiles.Count} tiles, {_entries.Count} entries, {_live.Count} rows bound, ")
                      .Append($"content={_listContent.sizeDelta.y:0}h at y={_listContent.anchoredPosition.y:0}, ")
                      .Append($"viewport={ViewportHeight():0}h");

                EntryView firstRow = null;
                foreach (var pair in _live) { firstRow = pair.Value; break; }

                if (firstRow?.Root == null)
                {
                    report.Append(" — NO ROWS BOUND.");
                }
                else
                {
                    var rect = firstRow.Root;
                    report.Append($"\n  first row: size={rect.rect.width:0}x{rect.rect.height:0} ")
                          .Append($"anchoredY={rect.anchoredPosition.y:0} ")
                          .Append($"activeInHierarchy={rect.gameObject.activeInHierarchy}");

                    var graphic = rect.GetComponent<Graphic>();
                    if (graphic != null)
                        report.Append($" alpha={graphic.canvasRenderer.GetAlpha():0.##} ")
                              .Append($"cull={graphic.canvasRenderer.cull}");
                }

                RecipePlannerUI.Log?.Info(report.ToString());
            }
            catch (Exception ex)
            {
                RecipePlannerUI.Log?.Warn("First-draw report failed: " + ex.Message);
            }
        }

        private void ShowPlaceholder(Font font, string message)
        {
            if (message == null)
            {
                if (_placeholder != null) _placeholder.gameObject.SetActive(false);
                return;
            }

            if (_placeholder == null)
            {
                _placeholder = CreateText(_root, "Placeholder", message, font, NameFontSize, FontStyle.Normal);
                _placeholder.alignment = TextAnchor.UpperLeft;
                _placeholder.color = new Color(1f, 1f, 1f, 0.7f);
                Anchor(_placeholder.rectTransform, Vector2.zero, Vector2.one,
                       new Vector2(20f, 8f), new Vector2(-20f, ListTopInset - 8f));
            }

            _placeholder.text = message;
            _placeholder.gameObject.SetActive(true);
        }

        // ---------- strain tile ----------

        /// <summary>
        /// One strain in the top strip, styled after the game's own product cards: a square with a
        /// thin border and the bud artwork centred in it, plus the number of recipes descending
        /// from that strain. Tapping one lists those recipes below.
        ///
        /// No name on the card. The game identifies a strain by the look of its bud and so does
        /// this — the name would not fit at a readable size anyway, and the caption under the strip
        /// says which one is selected.
        /// </summary>
        private sealed class StrainTile
        {
            public RectTransform Root;

            /// <summary>Null for the "all strains" tile.</summary>
            public string RootProductId;

            private Image _border;
            private Image _fill;
            private Text _count;

            public static StrainTile Create(
                CookbookScreen screen, RectTransform parent, Font font,
                string rootProductId, string label, int count, float x, float width)
            {
                var tile = new StrainTile { RootProductId = rootProductId };

                // Stretched vertically, so the card fills the strip's height minus an inset. Note
                // sizeDelta is NOT used for height here: on a stretched axis it is the difference
                // from the parent's size, not the size, and setting it to the card height made the
                // tiles overflow into the toolbar above and the caption below.
                tile.Root = CreateChild(parent, "Tile_" + (rootProductId ?? "all"));
                tile.Root.anchorMin = new Vector2(0f, 0f);
                tile.Root.anchorMax = new Vector2(0f, 1f);
                tile.Root.pivot = new Vector2(0f, 0.5f);
                tile.Root.offsetMin = new Vector2(x, TileInset);
                tile.Root.offsetMax = new Vector2(x + width, -TileInset);

                tile._border = tile.Root.gameObject.AddComponent<Image>();

                // The border is this image showing through around an inset fill — a mod has no
                // sprite assets to draw a real outline with.
                var fillRect = CreateChild(tile.Root, "Fill");
                Anchor(fillRect, Vector2.zero, Vector2.one,
                       new Vector2(TileBorder, TileBorder), new Vector2(-TileBorder, -TileBorder));
                tile._fill = fillRect.gameObject.AddComponent<Image>();
                tile._fill.raycastTarget = false;
                AddGloss(fillRect, 0.12f, 0.20f);

                var sprite = rootProductId != null ? IconSource.Product(rootProductId) : null;
                if (sprite != null)
                {
                    var icon = CreateChild(tile.Root, "Bud");
                    icon.anchorMin = new Vector2(0.5f, 0.5f);
                    icon.anchorMax = new Vector2(0.5f, 0.5f);
                    icon.pivot = new Vector2(0.5f, 0.5f);
                    icon.anchoredPosition = new Vector2(0f, 2f);
                    icon.sizeDelta = new Vector2(TileIconSize, TileIconSize);

                    var image = icon.gameObject.AddComponent<Image>();
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                }
                else
                {
                    // The "all strains" card has no bud of its own.
                    var all = CreateText(tile.Root, "AllLabel", label, font, TileFontSize, FontStyle.Bold);
                    all.alignment = TextAnchor.MiddleCenter;
                    all.color = NameText;
                    Anchor(all.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -2f));
                }

                tile._count = CreateText(tile.Root, "Count", count.ToString(), font,
                                         TileCountFontSize, FontStyle.Bold);
                tile._count.alignment = TextAnchor.LowerRight;
                tile._count.color = ChainText;
                Anchor(tile._count.rectTransform, Vector2.zero, Vector2.one,
                       new Vector2(0f, 3f), new Vector2(-5f, 0f));

                var button = tile.Root.gameObject.AddComponent<Button>();
                button.targetGraphic = tile._border;
                button.onClick.AddListener(() =>
                {
                    try { screen.SelectStrain(rootProductId, rootProductId != null); }
                    catch (Exception ex) { RecipePlannerUI.Log?.Warn("Strain select failed: " + ex.Message); }
                });

                return tile;
            }

            public void SetSelected(bool selected)
            {
                _border.color = selected ? TileBorderSelected : TileBorderIdle;
                _fill.color = selected ? TileFillSelected : TileFillIdle;
                _count.color = selected ? HeaderText : ChainText;
            }
        }
        // ---------- pooled row view ----------

        /// <summary>
        /// A product row: its own bud on the left, the name above the chain that produced it, and
        /// totals on the right. Rebindable — the child objects are created once and reused for
        /// whatever entry scrolls into their slot, and positioned by the screen rather than by a
        /// layout group so the list can move without Unity walking a hierarchy every frame.
        /// </summary>
        private sealed class EntryView
        {
            public RectTransform Root;

            private CookbookScreen _screen;
            private CookbookEntry _entry;
            private Font _font;

            private Image _stripe;
            private Image _productIcon;
            private Text _name;
            private Text _stats;
            private Text _value;
            private Text _addiction;
            private Image _barTrack;
            private RectTransform _barFill;
            private Image _barFillImage;
            private Image _baseIcon;
            private Text _unknownOrigin;
            private Text _overflow;
            private Text _hideGlyph;

            private readonly Text[] _plus = new Text[MaxChainSlots];
            private readonly Image[] _additive = new Image[MaxChainSlots];

            public void Place(float top, float height)
            {
                Root.anchoredPosition = new Vector2(0f, -top);
                Root.sizeDelta = new Vector2(-SidePadding * 2f, height);
                Root.gameObject.SetActive(true);
            }

            public static EntryView Create(CookbookScreen screen, RectTransform parent, Font font)
            {
                var view = new EntryView { _screen = screen, _font = font };

                view.Root = CreateChild(parent, "Entry");
                view.Root.anchorMin = new Vector2(0f, 1f);
                view.Root.anchorMax = new Vector2(1f, 1f);
                view.Root.pivot = new Vector2(0.5f, 1f);

                view._stripe = view.Root.gameObject.AddComponent<Image>();
                view._stripe.raycastTarget = false;

                // A hairline under every row, rather than relying on the alternating tint alone:
                // banding separates pairs of rows, an edge separates each one from the next.
                var edge = CreateChild(view.Root, "Edge");
                edge.anchorMin = new Vector2(0f, 0f);
                edge.anchorMax = new Vector2(1f, 0f);
                edge.offsetMin = new Vector2(4f, 0f);
                edge.offsetMax = new Vector2(-4f, 1f);
                var edgeImage = edge.gameObject.AddComponent<Image>();
                edgeImage.color = RowEdge;
                edgeImage.raycastTarget = false;

                view._productIcon = CreateIcon(view.Root, "ProductIcon");
                var iconRect = view._productIcon.rectTransform;
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(8f, 0f);
                iconRect.sizeDelta = new Vector2(IconSize, IconSize);

                view._name = CreateText(view.Root, "Name", "", font, NameFontSize, FontStyle.Bold);
                Anchor(view._name.rectTransform, new Vector2(0f, 0.48f), new Vector2(0.44f, 1f),
                       new Vector2(ChainSlotLeft, 0f), new Vector2(0f, -4f));

                // Price sits in the middle of the row, beside the recipe it belongs to, rather than
                // out on the far edge. Its colour is CONSTANT: an earlier version tinted this line
                // by addictiveness, but the line carried the price too, so money appeared to change
                // colour for a reason that had nothing to do with money.
                view._value = CreateText(view.Root, "Value", "", font, PriceFontSize, FontStyle.Bold);
                view._value.color = PriceText;
                view._value.alignment = TextAnchor.MiddleRight;
                Anchor(view._value.rectTransform, new Vector2(0.44f, 0.48f), new Vector2(0.62f, 1f),
                       Vector2.zero, new Vector2(-6f, -4f));

                view._stats = CreateText(view.Root, "Stats", "", font, StatFontSize - 3, FontStyle.Normal);
                view._stats.color = StatText;
                view._stats.alignment = TextAnchor.MiddleRight;
                Anchor(view._stats.rectTransform, new Vector2(0.44f, 0f), new Vector2(0.62f, 0.48f),
                       Vector2.zero, new Vector2(-6f, 0f));

                view.CreateAddictionMeter(font);

                view._unknownOrigin = CreateText(view.Root, "Unknown", "origin unknown", font,
                                                 StatFontSize - 2, FontStyle.Italic);
                view._unknownOrigin.color = ChainText;
                Anchor(view._unknownOrigin.rectTransform, new Vector2(0f, 0f), new Vector2(0.44f, 0.48f),
                       new Vector2(ChainSlotLeft, 2f), Vector2.zero);

                var x = ChainSlotLeft;
                view._baseIcon = CreateChainIcon(view.Root, "BaseIcon", ref x);

                // Chain slots are created ON DEMAND, not up front.
                //
                // Measured: constructing eight rows cost 13ms, and sixteen of a row's twenty-seven
                // objects were chain slots. Almost every recipe uses one to three, so the rest were
                // paid for and never shown. Each slot sits at a position computed from its index,
                // so building them out of order changes nothing about the layout.
                view.CreateHideButton(font);
                return view;
            }

            /// <summary>
            /// The addictiveness meter: a filled track with the percentage above it, mirroring the
            /// slider the game's own Products panel shows. A bar reads at a glance in a long list
            /// where a bare number does not, which is the point of putting it on every row.
            /// </summary>
            private void CreateAddictionMeter(Font font)
            {
                // Percentage and track share one line rather than stacking. Stacked, the pair read
                // as two separate things at a glance; side by side they read as one measurement.
                _addiction = CreateText(Root, "Addiction", "", font, StatFontSize - 1, FontStyle.Bold);
                _addiction.alignment = TextAnchor.MiddleRight;
                Anchor(_addiction.rectTransform, new Vector2(0.62f, 0.5f), new Vector2(0.74f, 0.5f),
                       new Vector2(0f, -12f), new Vector2(-8f, 12f));

                var track = CreateChild(Root, "AddictionTrack");
                track.anchorMin = new Vector2(0.74f, 0.5f);
                track.anchorMax = new Vector2(1f, 0.5f);
                track.offsetMin = new Vector2(0f, -4f);
                track.offsetMax = new Vector2(-48f, 4f);

                _barTrack = track.gameObject.AddComponent<Image>();
                _barTrack.color = BarTrack;
                _barTrack.raycastTarget = false;

                // The fill is sized by its right ANCHOR rather than by width, so it scales with the
                // row and needs no arithmetic against the track's pixel size.
                _barFill = CreateChild(track, "Fill");
                _barFill.anchorMin = new Vector2(0f, 0f);
                _barFill.anchorMax = new Vector2(0f, 1f);
                _barFill.offsetMin = Vector2.zero;
                _barFill.offsetMax = Vector2.zero;

                _barFillImage = _barFill.gameObject.AddComponent<Image>();
                _barFillImage.raycastTarget = false;
            }

            private void BindAddiction(float addictiveness)
            {
                var value = Mathf.Clamp01(addictiveness);
                var colour = AddictionColour(value);

                // Floored to a whole percent, exactly as the game prints it, so the two agree.
                _addiction.text = Mathf.FloorToInt(value * 100f) + "%";
                _addiction.color = colour;

                _barFill.anchorMax = new Vector2(value, 1f);
                _barFill.offsetMin = Vector2.zero;
                _barFill.offsetMax = Vector2.zero;
                _barFillImage.color = colour;

                // A product with no addictiveness shows no meter at all rather than an empty
                // track, which would read as "measured at zero" instead of "nothing to show".
                var known = value > 0f;
                _barTrack.gameObject.SetActive(known);
                _addiction.gameObject.SetActive(known);
            }

            /// <summary>Where the chain starts — after the product icon.</summary>
            private const float ChainSlotLeft = 8f + IconSize + 12f;
            private const float ChainSlotPitch = 13f + ChainIconSize + 3f;

            private static float SlotX(int index) =>
                ChainSlotLeft + (ChainIconSize + 3f) + index * ChainSlotPitch;

            /// <summary>Builds slot <paramref name="index"/> the first time a recipe needs it.</summary>
            private void EnsureSlot(int index)
            {
                if (_plus[index] != null) return;

                var x = SlotX(index);

                _plus[index] = CreateText(Root, "Plus", "+", _font, StatFontSize - 3, FontStyle.Normal);
                _plus[index].color = ChainText;
                _plus[index].alignment = TextAnchor.MiddleCenter;
                Anchor(_plus[index].rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0.48f),
                       new Vector2(x, 2f), new Vector2(x + 12f, 0f));

                var iconX = x + 13f;
                _additive[index] = CreateChainIcon(Root, "Additive", ref iconX);
            }

            private void EnsureOverflow()
            {
                if (_overflow != null) return;

                var x = SlotX(MaxChainSlots);
                _overflow = CreateText(Root, "Overflow", "", _font, StatFontSize - 3, FontStyle.Normal);
                _overflow.color = ChainText;
                Anchor(_overflow.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0.48f),
                       new Vector2(x, 2f), new Vector2(x + 52f, 0f));
            }

            private static Image CreateChainIcon(RectTransform parent, string name, ref float x)
            {
                var icon = CreateIcon(parent, name);
                var rect = icon.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(x, 4f);
                rect.sizeDelta = new Vector2(ChainIconSize, ChainIconSize);
                x += ChainIconSize + 3f;
                return icon;
            }

            private static Image CreateIcon(RectTransform parent, string name)
            {
                var rect = CreateChild(parent, name);
                var image = rect.gameObject.AddComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;
                return image;
            }

            /// <summary>
            /// Hides the entry from this list only. The recipe stays in the game and keeps its
            /// history and statistics — this exists purely so a cookbook of hundreds can be pruned
            /// to the ones the player actually cares about.
            /// </summary>
            private void CreateHideButton(Font font)
            {
                var button = CreateChild(Root, "Hide");
                button.anchorMin = new Vector2(1f, 0f);
                button.anchorMax = new Vector2(1f, 1f);
                button.pivot = new Vector2(1f, 0.5f);
                button.offsetMin = new Vector2(-40f, 18f);
                button.offsetMax = new Vector2(-8f, -18f);

                var image = button.gameObject.AddComponent<Image>();
                image.color = ButtonIdle;
                AddGloss(button, 0.12f, 0.14f);

                _hideGlyph = CreateText(button, "Glyph", "x", font, StatFontSize, FontStyle.Bold);
                _hideGlyph.alignment = TextAnchor.MiddleCenter;
                Anchor(_hideGlyph.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

                var clickable = button.gameObject.AddComponent<Button>();
                clickable.targetGraphic = image;
                clickable.onClick.AddListener(() =>
                {
                    try
                    {
                        if (_entry == null) return;
                        RecipePlannerUI.SetRecipeHidden?.Invoke(RecipeIdOf(_entry), !_entry.IsHidden);
                        _screen.Refresh();
                    }
                    catch (Exception ex) { RecipePlannerUI.Log?.Warn("Hide failed: " + ex.Message); }
                });
            }

            public void Bind(CookbookEntry entry, int ordinal)
            {
                _entry = entry;

                _stripe.color = ordinal % 2 == 0 ? RowStripe : Transparent;

                Apply(_productIcon, IconSource.Product(entry.ProductId));

                _name.text = (entry.IsFavourite ? "* " : "") + entry.DisplayName;
                _name.color = entry.IsFavourite ? Favourite : NameText;

                _value.text = DescribePrice(entry);
                BindAddiction(entry.Addictiveness);
                _stats.text = DescribeStats(entry);

                _hideGlyph.text = entry.IsHidden ? "+" : "x";
                _hideGlyph.color = entry.IsHidden ? StatText : ChainText;

                var known = entry.OriginKnown;
                _unknownOrigin.gameObject.SetActive(!known);

                Apply(_baseIcon, known ? IconSource.Product(entry.RootProductId) : null);

                var steps = known ? entry.Steps.Count : 0;
                var shown = Mathf.Min(steps, MaxChainSlots);

                for (var i = 0; i < shown; i++)
                {
                    EnsureSlot(i);
                    _plus[i].gameObject.SetActive(true);
                    Apply(_additive[i], IconSource.Item(entry.Steps[i].AdditiveId));
                }

                // Slots past this recipe's length may not exist at all — only hide the ones that do.
                for (var i = shown; i < MaxChainSlots; i++)
                {
                    if (_plus[i] == null) continue;
                    _plus[i].gameObject.SetActive(false);
                    Apply(_additive[i], null);
                }

                var remaining = steps - shown;
                if (remaining > 0)
                {
                    EnsureOverflow();
                    _overflow.text = "+" + remaining;
                    _overflow.gameObject.SetActive(true);
                }
                else if (_overflow != null)
                {
                    _overflow.gameObject.SetActive(false);
                }
            }

            /// <summary>An icon with no artwork is switched off rather than drawn as a white box.</summary>
            private static void Apply(Image image, Sprite sprite)
            {
                image.sprite = sprite;
                image.gameObject.SetActive(sprite != null);
            }
        }

        // ---------- toolbar ----------

        private readonly List<Text> _sortLabels = new List<Text>();
        private Text _filterLabel;
        private Text _hiddenLabel;

        /// <summary>
        /// Buttons rather than a text field on purpose: a uGUI InputField inside a running game
        /// fights the player's movement keys for keyboard focus. The strain strip, sorting and
        /// filtering cover the "hundreds of recipes" problem without that risk; search can come
        /// later with proper input capture.
        /// </summary>
        private void BuildToolbar(Font font)
        {
            var bar = CreateChild(_root, "CookbookToolbar");
            Anchor(bar, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(8f, ToolbarBottom), new Vector2(-8f, ToolbarTop));

            var x = 0f;
            x = AddSortButton(bar, font, "Name", CookbookSort.Name, ref x);
            x = AddSortButton(bar, font, "Made", CookbookSort.UnitsProduced, ref x);
            x = AddSortButton(bar, font, "Value", CookbookSort.Value, ref x);
            x = AddSortButton(bar, font, "Recent", CookbookSort.RecentlyProduced, ref x);
            x = AddSortButton(bar, font, "Steps", CookbookSort.ChainLength, ref x);
            x = AddSortButton(bar, font, "Addictive", CookbookSort.Addictiveness, ref x);

            x += 10f;
            _filterLabel = AddToolButton(bar, font, "All", ref x, 104f, CycleFilter);
            _hiddenLabel = AddToolButton(bar, font, "Hidden: off", ref x, 132f, ToggleHidden);
        }

        private float AddSortButton(RectTransform bar, Font font, string label, CookbookSort sort, ref float x)
        {
            var text = AddToolButton(bar, font, label, ref x, 84f, () =>
            {
                var query = CurrentQuery();
                if (query == null) return;

                // Tapping the active sort flips its direction, which is what people expect from a
                // column header.
                if (query.Sort == sort) query.Descending = !query.Descending;
                else { query.Sort = sort; query.Descending = false; }

                Refresh();
            });

            text.name = "Sort_" + sort;
            _sortLabels.Add(text);
            return x;
        }

        private Text AddToolButton(
            RectTransform bar, Font font, string label, ref float x, float width, Action onClick)
        {
            var button = CreateChild(bar, "Btn_" + label);
            button.anchorMin = new Vector2(0f, 0f);
            button.anchorMax = new Vector2(0f, 1f);
            button.pivot = new Vector2(0f, 0.5f);
            button.offsetMin = new Vector2(x, 2f);
            button.offsetMax = new Vector2(x + width, -2f);

            var image = button.gameObject.AddComponent<Image>();
            image.color = ButtonIdle;
            AddGloss(button);

            var text = CreateText(button, "Label", label, font, ToolFontSize, FontStyle.Normal);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = NameText;
            Anchor(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var clickable = button.gameObject.AddComponent<Button>();
            clickable.targetGraphic = image;
            clickable.onClick.AddListener(() => { try { onClick(); } catch { } });

            x += width + 4f;
            return text;
        }

        private void CycleFilter()
        {
            var query = CurrentQuery();
            if (query == null) return;

            // All -> Favourites -> Produced -> All
            if (!query.FavouritesOnly && !query.ProducedOnly) query.FavouritesOnly = true;
            else if (query.FavouritesOnly) { query.FavouritesOnly = false; query.ProducedOnly = true; }
            else query.ProducedOnly = false;

            Refresh();
        }

        private void ToggleHidden()
        {
            var query = CurrentQuery();
            if (query == null) return;

            query.ShowHidden = !query.ShowHidden;
            Refresh();
        }

        private CookbookQuery _query;
        private CookbookQuery CurrentQuery() => _query;

        private void RefreshToolbar(CookbookViewModel model)
        {
            _query = model?.Query;
            if (_query == null) return;

            foreach (var label in _sortLabels)
            {
                var isActive = label.name == "Sort_" + _query.Sort;
                label.color = isActive ? HeaderText : ChainText;
                label.fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal;

                var arrow = isActive ? (_query.Descending ? " ^" : " v") : "";
                var baseLabel = label.text.TrimEnd(' ', '^', 'v');
                label.text = baseLabel + arrow;
            }

            if (_filterLabel != null)
                _filterLabel.text = _query.FavouritesOnly ? "Favourites"
                                  : _query.ProducedOnly ? "Produced"
                                  : "All";

            if (_hiddenLabel != null)
                _hiddenLabel.text = _query.ShowHidden ? "Hidden: on" : "Hidden: off";
        }

        // ---------- palette ----------

        private static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);
        private static readonly Color ButtonIdle = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color RowEdge = new Color(1f, 1f, 1f, 0.055f);

        private static readonly Color TileBorderIdle = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color TileBorderSelected = new Color(1f, 0.84f, 0.45f, 0.95f);
        private static readonly Color TileFillIdle = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color TileFillSelected = new Color(1f, 0.84f, 0.45f, 0.14f);

        private static readonly Color HeaderText = new Color(1f, 0.84f, 0.45f, 1f);
        private static readonly Color NameText = new Color(0.95f, 0.95f, 0.97f, 1f);
        private static readonly Color ChainText = new Color(0.62f, 0.66f, 0.72f, 1f);
        private static readonly Color StatText = new Color(0.55f, 0.78f, 0.62f, 1f);
        private static readonly Color PriceText = new Color(0.45f, 0.95f, 0.55f, 1f);
        private static readonly Color BarLow = new Color(0.92f, 0.78f, 0.42f, 1f);
        private static readonly Color BarHigh = new Color(0.94f, 0.36f, 0.31f, 1f);
        private static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color RowStripe = new Color(1f, 1f, 1f, 0.045f);
        private static readonly Color Favourite = new Color(1f, 0.80f, 0.30f, 1f);

        /// <summary>
        /// The cookbook keys recipes by base + additive chain, so an entry whose origin is unknown
        /// falls back to its product id — otherwise every unknown-origin row would share one key.
        /// </summary>
        private static string RecipeIdOf(CookbookEntry entry)
        {
            if (!entry.OriginKnown || entry.Steps.Count == 0) return entry.ProductId;

            var additives = new List<string>();
            foreach (var step in entry.Steps) additives.Add(step.AdditiveId);
            return Recipe.ComputeId(entry.RootProductId, additives);
        }

        /// <summary>
        /// What the product sells for.
        ///
        /// Prefers the player's asking price and falls back to the game's suggested value, so the
        /// line is never blank for something that simply has not been listed yet.
        /// </summary>
        private static string DescribePrice(CookbookEntry entry)
        {
            // Same rule the Value sort uses, so what is displayed and what is ordered agree.
            var price = Cookbook.SellPrice(entry);

            return price > 0f ? "$" + price.ToString("N0") : "";
        }

        /// <summary>
        /// Amber through to red as a product gets more addictive.
        ///
        /// Deliberately nowhere near the money green: the two sit on the same row, and a meter that
        /// starts out the same colour as the price implies a relationship between them that does
        /// not exist.
        /// </summary>
        private static Color AddictionColour(float addictiveness) =>
            Color.Lerp(BarLow, BarHigh, Mathf.Clamp01(addictiveness));

        private static string DescribeStats(CookbookEntry entry)
        {
            if (entry.UnitsProduced == 0) return "";

            var line = $"{entry.UnitsProduced} units";
            if (entry.TotalValue > 0) line += $"   ${entry.TotalValue:N0}";
            return line;
        }

        // ---------- small uGUI helpers ----------

        /// <summary>
        /// Lays a thin light strip along the top of a panel and a darker one along the bottom, so a
        /// flat rectangle reads as a piece of glass catching the light.
        ///
        /// A mod ships no sprites, so there is no gradient or rounded corner to be had — but a
        /// bright top edge and a dark bottom edge are most of what sells the effect anyway, and
        /// they cost two untextured quads.
        /// </summary>
        private static void AddGloss(RectTransform panel, float topAlpha = 0.14f, float bottomAlpha = 0.16f)
        {
            var top = CreateChild(panel, "Gloss");
            top.anchorMin = new Vector2(0f, 1f);
            top.anchorMax = new Vector2(1f, 1f);
            top.offsetMin = new Vector2(1f, -1.5f);
            top.offsetMax = new Vector2(-1f, 0f);
            var topImage = top.gameObject.AddComponent<Image>();
            topImage.color = new Color(1f, 1f, 1f, topAlpha);
            topImage.raycastTarget = false;

            var bottom = CreateChild(panel, "Shade");
            bottom.anchorMin = new Vector2(0f, 0f);
            bottom.anchorMax = new Vector2(1f, 0f);
            bottom.offsetMin = new Vector2(1f, 0f);
            bottom.offsetMax = new Vector2(-1f, 1.5f);
            var bottomImage = bottom.gameObject.AddComponent<Image>();
            bottomImage.color = new Color(0f, 0f, 0f, bottomAlpha);
            bottomImage.raycastTarget = false;
        }

        private static RectTransform CreateChild(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static Text CreateText(
            RectTransform parent, string name, string content, Font font, int size, FontStyle style)
        {
            var rect = CreateChild(parent, name);
            var text = rect.gameObject.AddComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
