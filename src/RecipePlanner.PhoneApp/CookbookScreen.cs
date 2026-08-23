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
            var root = UiInterop.NewRect("CookbookRoot").GetComponent<RectTransform>();
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
            backgroundImage.color = AppBackground;
            background.SetAsFirstSibling();

            var titleBar = CreateChild(_root, "CookbookTitleBar");
            Anchor(titleBar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -TitleHeight), Vector2.zero);
            var titleImage = titleBar.gameObject.AddComponent<Image>();
            titleImage.color = HeaderFill;

            AddGloss(titleBar, 0.05f, 0.09f);

            BuildHeaderMark(titleBar, font);

            // Three runs of text rather than one string, because they are three different things:
            // the app's name, how much is in it, and whose it is. One Text can only be one colour,
            // and the name being white while the character is green is what stops the header
            // reading as an undifferentiated sentence.
            _header = CreateText(titleBar, "CookbookHeader", "Cookbook", font, TitleFontSize, FontStyle.Bold);
            _header.color = TitleText;
            Anchor(_header.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(HeaderMarkWidth, 0f), new Vector2(-14f, 0f));

            _headerCount = CreateText(titleBar, "CookbookCount", "", font, TitleFontSize - 6, FontStyle.Normal);
            _headerCount.color = StatText;
            Anchor(_headerCount.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(HeaderMarkWidth, 0f), new Vector2(-14f, 0f));

            _headerProfile = CreateText(titleBar, "CookbookProfile", "", font, TitleFontSize - 6, FontStyle.Bold);
            _headerProfile.color = Neon;
            Anchor(_headerProfile.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                   new Vector2(HeaderMarkWidth, 0f), new Vector2(-14f, 0f));

            BuildStatusPip(titleBar, font);

            BuildToolbar(font);

            // The strip stops short of the right edge to leave room for the guide button. Strain
            // tiles are laid out left to right and rarely fill the row, so this reclaims space that
            // was empty anyway rather than taking any from them.
            _strip = CreateChild(_root, "StrainStrip");
            Anchor(_strip, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(8f, StripBottom), new Vector2(-DestinationsWidth - 16f, StripTop));

            BuildGuideButton(font);

            _caption = CreateText(_root, "Caption", "", font, CaptionFontSize, FontStyle.Bold);
            _caption.color = HeaderText;
            Anchor(_caption.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(12f, CaptionBottom), new Vector2(-12f, CaptionTop));

            // The strain and the tally are different kinds of fact, so they get different weights:
            // which strain you are looking at is the answer, how many it holds is a footnote.
            _captionCount = CreateText(_root, "CaptionCount", "", font, CaptionFontSize - 2, FontStyle.Normal);
            _captionCount.color = StatText;
            Anchor(_captionCount.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
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

            // Built last so it draws over the list. It lives outside the viewport on purpose —
            // see BuildEffectsCard.
            BuildEffectsCard(font);

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
            UiInterop.OnScrollChanged(_scroll, _ => UpdateWindow());

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

            // The rows about to be rebuilt are the ones the card was describing, and after a sort
            // or filter change the row it was pinned beside may not even be on screen. Dropping it
            // is the only honest option: a card left floating over a re-sorted list is pointing at
            // the wrong recipe.
            _pinnedProductId = null;
            HideEffects();

            _model = RecipePlannerUI.DataSource?.Invoke();
            RefreshToolbar(_model);

            if (_model == null || _model.IsEmpty)
            {
                ReleaseAll();
                _entries.Clear();
                _listContent.sizeDelta = Vector2.zero;
                ClearStrip();
                LayoutCaption("", "");
                ShowPlaceholder(font, "No recipes recorded yet — cook something and it will appear here.");
                LayoutHeader("", "");
                SetStatus(false, "No save");
                return;
            }

            ShowPlaceholder(font, null);

            LayoutHeader($"— {_model.TotalRecipes} recipes ·", _model.ProfileLabel);
            SetStatus(true, "Tracking");

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

                LayoutCaption(Title(section), section.Count + " recipes");
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
                LayoutCaption("ALL STRAINS", _entries.Count + " recipes");
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
        /// <summary>
        /// One frame at 60fps. Below this the player cannot perceive the cost, so reporting it is
        /// noise — and noise logged at Warn level is worse than useless: it fired on every single
        /// first draw (measured 4.4ms with 83 entries), so every player would find a WARNING in
        /// their log the first time they opened the app and reasonably report it as a fault.
        ///
        /// A log a player is expected to send you when something breaks has to be quiet when
        /// nothing has.
        /// </summary>
        private const double SlowFrameMs = 16.0;
        private float _nextReportTime;
        private int _rowsConstructed;

        private void UpdateWindow()
        {
            // Rows are recycled as the list scrolls, so the row the card is anchored beside is
            // about to show a different recipe. Keeping the card there would leave it pointing at
            // the wrong row — worse than simply dismissing it.
            if (_pinnedProductId != null || (_effectsCard != null && _effectsCard.gameObject.activeSelf))
            {
                _pinnedProductId = null;
                HideEffects();
            }

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
            private Image _outline;
            private Image _fill;
            private Text _allLabel;
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

                // The fill is the whole tile and the border is a real outline over it. This used to
                // be a coloured backing showing through around an inset fill, which was the only
                // way to fake an outline before UiSkin could generate one — that trick cannot make
                // a thin border without also making the tile visibly smaller.
                tile._border = tile.Root.gameObject.AddComponent<Image>();
                tile._border.sprite = UiSkin.Body;
                tile._border.type = Image.Type.Sliced;
                tile._border.color = Transparent;

                var fillRect = CreateChild(tile.Root, "Fill");
                Anchor(fillRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                tile._fill = fillRect.gameObject.AddComponent<Image>();
                tile._fill.sprite = UiSkin.Body;
                tile._fill.type = Image.Type.Sliced;
                tile._fill.raycastTarget = false;

                var outlineRect = CreateChild(tile.Root, "Outline");
                Anchor(outlineRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                tile._outline = outlineRect.gameObject.AddComponent<Image>();
                tile._outline.sprite = UiSkin.Ring;
                tile._outline.type = Image.Type.Sliced;
                tile._outline.raycastTarget = false;

                // Tiles get the same underglow as the buttons, so hovering a strain reads the same
                // way as hovering anything else in the app.
                var tileGlowRect = CreateChild(tile.Root, "Glow");
                Anchor(tileGlowRect, Vector2.zero, Vector2.one,
                       new Vector2(-GlowInset, -GlowInset), new Vector2(GlowInset, GlowInset));
                tileGlowRect.SetAsFirstSibling();
                var tileGlow = tileGlowRect.gameObject.AddComponent<Image>();
                tileGlow.sprite = UiSkin.Glow;
                tileGlow.type = Image.Type.Sliced;
                tileGlow.raycastTarget = false;
                HoverGlow.Attach(tile.Root.gameObject, tileGlow, GlowRest, GlowHot);

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
                    tile._allLabel = all;
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
                UiInterop.OnClick(button, () =>
                {
                    try { screen.SelectStrain(rootProductId, rootProductId != null); }
                    catch (Exception ex) { RecipePlannerUI.Log?.Warn("Strain select failed: " + ex.Message); }
                });

                return tile;
            }

            public void SetSelected(bool selected)
            {
                _outline.color = selected ? TileBorderSelected : TileBorderIdle;
                _fill.color = selected ? TileFillSelected : TileFillIdle;
                _count.color = selected ? Neon : ChainText;
                if (_allLabel != null) _allLabel.color = selected ? Neon : NameText;
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
            private Image _border;
            private Color _restFill;
            private Image _productIcon;
            private Text _name;
            private Text _stats;
            private Text _value;
            private Text _addiction;
            private Image _barTrack;
            private RectTransform _barFill;
            private Image _barFillImage;
            private RectTransform _barGlow;
            private Image _barGlowImage;
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
                view._stripe.sprite = UiSkin.Body;
                view._stripe.type = Image.Type.Sliced;

                // Raycastable so the row can be hovered. Drag and scroll are not handled here, so
                // they still bubble up to the ScrollRect exactly as before.
                view._stripe.raycastTarget = true;

                // Each row is a card with its own outline. Banding alone separated rows well enough
                // on a grey panel, but on near-black the two alternating fills are almost the same
                // colour and the list turns into one continuous slab.
                var cardBorder = CreateChild(view.Root, "CardBorder");
                Anchor(cardBorder, Vector2.zero, Vector2.one, new Vector2(0f, 1f), new Vector2(0f, -1f));
                view._border = cardBorder.gameObject.AddComponent<Image>();
                view._border.sprite = UiSkin.Ring;
                view._border.type = Image.Type.Sliced;
                view._border.color = CardBorder;
                view._border.raycastTarget = false;

                // Hover previews the recipe's effects; clicking pins the card so it survives the
                // pointer moving away — useful when reading a long effect list or comparing rows.
                var hover = HoverGlow.Attach(view.Root.gameObject, null, Transparent, Transparent);
                hover.HoverChanged += isHot =>
                {
                    // The row lights itself. Hover has to be visible on the row the pointer is
                    // actually over — the effects card appears beside it, which tells you a card
                    // opened but not which row it belongs to.
                    view._border.color = isHot ? Neon : CardBorder;
                    view._stripe.color = isHot ? CardFillHot : view._restFill;

                    if (view._entry == null) return;
                    if (isHot) view._screen.ShowEffects(view.Root, view._entry, view._font);
                    else if (view._screen._pinnedProductId == null) view._screen.HideEffects();
                };

                var rowButton = view.Root.gameObject.AddComponent<Button>();
                rowButton.targetGraphic = view._stripe;
                rowButton.transition = Selectable.Transition.None;   // the stripe carries row banding
                UiInterop.OnClick(rowButton, () =>
                {
                    try
                    {
                        if (view._entry == null) return;
                        var screen = view._screen;
                        var id = view._entry.ProductId;

                        if (screen._pinnedProductId == id) { screen._pinnedProductId = null; screen.HideEffects(); }
                        else { screen._pinnedProductId = id; screen.ShowEffects(view.Root, view._entry, view._font); }
                    }
                    catch (Exception ex) { RecipePlannerUI.Log?.Warn("Effects card failed: " + ex.Message); }
                });

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
                // Built either way so Bind has something to write to unconditionally, and simply
                // left inactive when switched off — a row that skips creating them would need every
                // later reference guarded, which is how null-reference bugs get in.
                // The price takes the whole band when the production figures below it are switched
                // off, rather than sitting in the top half with empty space under it.
                var valueBottom = UiFeatures.RowUnitsProduced ? 0.48f : 0f;

                view._value = CreateText(view.Root, "Value", "", font, PriceFontSize, FontStyle.Bold);
                view._value.color = PriceText;
                view._value.alignment = TextAnchor.MiddleRight;
                Anchor(view._value.rectTransform, new Vector2(0.44f, valueBottom), new Vector2(0.62f, 1f),
                       Vector2.zero, new Vector2(-6f, -4f));

                view._stats = CreateText(view.Root, "Stats", "", font, StatFontSize - 3, FontStyle.Normal);
                view._stats.color = StatText;
                view._stats.alignment = TextAnchor.MiddleRight;
                Anchor(view._stats.rectTransform, new Vector2(0.44f, 0f), new Vector2(0.62f, 0.48f),
                       Vector2.zero, new Vector2(-6f, 0f));

                if (!UiFeatures.RowValue) view._value.gameObject.SetActive(false);
                if (!UiFeatures.RowUnitsProduced) view._stats.gameObject.SetActive(false);

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
                // Slides left into the space the figures would have taken when they are switched
                // off, so the row reads as designed for what it shows rather than as one with a
                // hole in it. Keyed on the price, since that is the wider of the two columns.
                var showsFigures = UiFeatures.RowValue || UiFeatures.RowUnitsProduced;
                var meterLeft = showsFigures ? 0.62f : 0.46f;
                var trackLeft = showsFigures ? 0.74f : 0.58f;

                _addiction = CreateText(Root, "Addiction", "", font, StatFontSize - 1, FontStyle.Bold);
                _addiction.alignment = TextAnchor.MiddleRight;
                Anchor(_addiction.rectTransform, new Vector2(meterLeft, 0.5f), new Vector2(trackLeft, 0.5f),
                       new Vector2(0f, -12f), new Vector2(-8f, 12f));

                var track = CreateChild(Root, "AddictionTrack");
                track.anchorMin = new Vector2(trackLeft, 0.5f);
                track.anchorMax = new Vector2(1f, 0.5f);
                track.offsetMin = new Vector2(0f, -4f);
                track.offsetMax = new Vector2(-48f, 4f);

                _barTrack = track.gameObject.AddComponent<Image>();
                _barTrack.sprite = UiSkin.Pill;
                _barTrack.type = Image.Type.Sliced;
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
                _barFillImage.sprite = UiSkin.Pill;
                _barFillImage.type = Image.Type.Sliced;
                _barFillImage.raycastTarget = false;

                // A glow tracking the fill. On a near-black row a thin bar barely registers
                // peripherally, and this meter's whole job is to be readable without being read.
                _barGlow = CreateChild(track, "FillGlow");
                _barGlow.anchorMin = new Vector2(0f, 0f);
                _barGlow.anchorMax = new Vector2(0f, 1f);
                _barGlow.offsetMin = new Vector2(-5f, -5f);
                _barGlow.offsetMax = new Vector2(5f, 5f);
                _barGlow.SetAsFirstSibling();

                _barGlowImage = _barGlow.gameObject.AddComponent<Image>();
                _barGlowImage.sprite = UiSkin.Glow;
                _barGlowImage.type = Image.Type.Sliced;
                _barGlowImage.raycastTarget = false;
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

                _barGlow.anchorMax = new Vector2(value, 1f);
                _barGlow.offsetMin = new Vector2(-5f, -5f);
                _barGlow.offsetMax = new Vector2(5f, 5f);

                // Brighter as it fills, and gone entirely at zero — an empty meter that still glows
                // looks like it is lit for a reason.
                _barGlowImage.color = new Color(colour.r, colour.g, colour.b, value <= 0f ? 0f : 0.10f + value * 0.28f);

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
            /// Fades everything on the row except the restore button.
            ///
            /// Colours are re-applied from the palette each time rather than multiplied, because
            /// binding is repeated as rows recycle — scaling the current colour would darken a row
            /// further every time it scrolled past.
            /// </summary>
            private void ApplyDimming(bool hidden)
            {
                var f = hidden ? DimFactor : 1f;

                // Every one of these is null-checked. The chain slots and the overflow label are
                // built ON DEMAND — most rows never need them — so a row is not a fixed set of
                // objects, and treating it as one threw a NullReferenceException out of Bind.
                //
                // That aborted the bind halfway: the name and price had been written, the icons and
                // the origin label had not, and the row rendered as a white square with a stray
                // "unknown" under it. A row that dims itself must never be able to stop a row from
                // drawing at all.
                _name.color = Fade(_entry.IsFavourite ? Favourite : NameText, f);
                _value.color = Fade(PriceText, f);
                _stats.color = Fade(StatText, f);
                _unknownOrigin.color = Fade(ChainText, f);

                if (_addiction != null) _addiction.color = Fade(_addiction.color, f);
                if (_overflow != null) _overflow.color = Fade(ChainText, f);

                // Icons keep their own alpha — Apply() shows and hides them with SetActive, and
                // overwriting alpha here would fight that.
                if (_productIcon != null) _productIcon.color = Fade(Color.white, f);
                if (_baseIcon != null) _baseIcon.color = Fade(Color.white, f);

                foreach (var slot in _additive)
                    if (slot != null) slot.color = Fade(Color.white, f);

                foreach (var plus in _plus)
                    if (plus != null) plus.color = Fade(ChainText, f);

                if (_barFillImage != null) _barFillImage.color = Fade(_barFillImage.color, f);
                if (_barGlowImage != null)
                    _barGlowImage.color = Fade(_barGlowImage.color, hidden ? 0.3f : 1f);
            }

            /// <summary>Keeps alpha, scales brightness.</summary>
            private static Color Fade(Color colour, float factor) =>
                new Color(colour.r * factor, colour.g * factor, colour.b * factor, colour.a);

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
                StyleRoundedButton(button, image);

                _hideGlyph = CreateText(button, "Glyph", "x", font, StatFontSize, FontStyle.Bold);
                _hideGlyph.alignment = TextAnchor.MiddleCenter;
                Anchor(_hideGlyph.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

                var clickable = button.gameObject.AddComponent<Button>();
                clickable.targetGraphic = image;
                clickable.colors = ButtonColours;
                UiInterop.OnClick(clickable, () =>
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

                // Remembered, because hover overwrites the fill and has to put back the right one —
                // rows alternate, so there is no single colour to restore to.
                _restFill = ordinal % 2 == 0 ? CardFill : CardFillAlt;
                _stripe.color = _restFill;
                _border.color = CardBorder;

                Apply(_productIcon, IconSource.Product(entry.ProductId));

                _name.text = (entry.IsFavourite ? "* " : "") + entry.DisplayName;
                _name.color = entry.IsFavourite ? Favourite : NameText;

                _value.text = DescribePrice(entry);
                BindAddiction(entry.Addictiveness);
                _stats.text = DescribeStats(entry);

                _hideGlyph.text = entry.IsHidden ? "+" : "x";
                _hideGlyph.color = entry.IsHidden ? Neon : ChainText;

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

                // LAST, deliberately. A hidden row stays in the list dimmed rather than vanishing —
                // removing it outright looked like deletion and left no visible way back. Dimming
                // has to run after every other part of the bind, because each of those writes its
                // own colour and would otherwise undo the fade on whatever it touched.
                ApplyDimming(entry.IsHidden);
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
            StyleRoundedButton(button, image, pill: true);

            var text = CreateText(button, "Label", label, font, ToolFontSize, FontStyle.Normal);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = ChainText;
            Anchor(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var clickable = button.gameObject.AddComponent<Button>();
            clickable.targetGraphic = image;
            clickable.colors = ButtonColours;
            UiInterop.OnClick(clickable, () => { try { onClick(); } catch { } });

            _pills[text] = new ToolPill { Body = image, Label = text };

            x += width + 6f;
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

            query.CollapseHidden = !query.CollapseHidden;
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

                // Outlined rather than filled. One sort is always active, so a solid pill here
                // would mean "a sort exists" — which is never news — and would compete with the
                // filter, where solid genuinely means something is being excluded.
                StylePill(label, isActive ? PillStyle.Outlined : PillStyle.Quiet);

                var arrow = isActive ? (_query.Descending ? " ^" : " v") : "";
                var baseLabel = label.text.TrimEnd(' ', '^', 'v');
                label.text = baseLabel + arrow;
            }

            if (_filterLabel != null)
            {
                _filterLabel.text = _query.FavouritesOnly ? "Favourites"
                                  : _query.ProducedOnly ? "Produced"
                                  : "All";

                // Always solid: it always holds a value, and it is the control most likely to be
                // hiding rows a player is looking for.
                StylePill(_filterLabel, PillStyle.Solid);
            }

            if (_hiddenLabel != null)
            {
                // "Hidden: shown" is the resting state now, so the label says what you are seeing
                // rather than naming a setting.
                _hiddenLabel.text = _query.CollapseHidden ? "Hidden: off" : "Hidden: shown";
                StylePill(_hiddenLabel, _query.CollapseHidden ? PillStyle.Solid : PillStyle.Quiet);
            }
        }

        // ---------- toolbar pills ----------

        /// <summary>How loudly a pill states itself.</summary>
        private enum PillStyle
        {
            /// <summary>Off, or simply one of several. Reads as background.</summary>
            Quiet,

            /// <summary>On, but unremarkable — the active choice among several that must have one.</summary>
            Outlined,

            /// <summary>On, and changing what the player is seeing. Impossible to miss.</summary>
            Solid,
        }

        private sealed class ToolPill
        {
            public Image Body;
            public Text Label;
        }

        private readonly Dictionary<Text, ToolPill> _pills = new Dictionary<Text, ToolPill>();

        private void StylePill(Text label, PillStyle style)
        {
            ToolPill pill;
            if (label == null || !_pills.TryGetValue(label, out pill)) return;

            switch (style)
            {
                case PillStyle.Solid:
                    pill.Body.color = Neon;
                    // Dark text on the accent, not white: white on this green is barely legible,
                    // and the app's own background is the one colour guaranteed to contrast with it.
                    pill.Label.color = AppBackground;
                    pill.Label.fontStyle = FontStyle.Bold;
                    break;

                case PillStyle.Outlined:
                    pill.Body.color = TileFillSelected;
                    pill.Label.color = Neon;
                    pill.Label.fontStyle = FontStyle.Bold;
                    break;

                default:
                    pill.Body.color = ButtonIdle;
                    pill.Label.color = ChainText;
                    pill.Label.fontStyle = FontStyle.Normal;
                    break;
            }
        }

        // ---------- header ----------

        private const float HeaderMarkWidth = 62f;

        private Text _captionCount;
        private Text _headerCount;
        private Text _headerProfile;
        private Image _headerMark;
        private Image _statusDot;
        private Text _statusLabel;

        /// <summary>
        /// The app's mark: a bordered tile holding the game's own bud sprite.
        ///
        /// The sprite is filled in on refresh rather than here, because it comes from the loaded
        /// save's first base product — there is no catalogue yet at build time, and hard-coding an
        /// id would break on a game that adds or renames a strain.
        /// </summary>
        private void BuildHeaderMark(RectTransform titleBar, Font font)
        {
            var tile = CreateChild(titleBar, "Mark");
            tile.anchorMin = new Vector2(0f, 0.5f);
            tile.anchorMax = new Vector2(0f, 0.5f);
            tile.pivot = new Vector2(0f, 0.5f);
            tile.sizeDelta = new Vector2(38f, 38f);
            tile.anchoredPosition = new Vector2(14f, 0f);

            var fill = tile.gameObject.AddComponent<Image>();
            fill.sprite = UiSkin.Body;
            fill.type = Image.Type.Sliced;
            fill.color = TileFillSelected;
            fill.raycastTarget = false;

            var border = CreateChild(tile, "Border");
            Anchor(border, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var borderImage = border.gameObject.AddComponent<Image>();
            borderImage.sprite = UiSkin.Ring;
            borderImage.type = Image.Type.Sliced;
            borderImage.color = NeonDim;
            borderImage.raycastTarget = false;

            // The same leaf as the home-screen icon, so the app is recognisable as itself from the
            // phone's grid through to its header. Borrowing a bud sprite from the save was tried
            // first and was worse: it changed depending on which strains a character had.
            var icon = CreateChild(tile, "Icon");
            Anchor(icon, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            _headerMark = icon.gameObject.AddComponent<Image>();
            _headerMark.sprite = UiSkin.PotLeaf;
            _headerMark.preserveAspect = true;
            _headerMark.raycastTarget = false;
            _headerMark.color = Neon;
        }

        /// <summary>
        /// A pip saying whether anything is actually being recorded.
        ///
        /// It reports real state rather than decoration: green once a save has resolved to a
        /// profile, grey before that. A permanently-green "Synced" light that means nothing would
        /// be worse than no light, because a player would trust it.
        /// </summary>
        private void BuildStatusPip(RectTransform titleBar, Font font)
        {
            var dot = CreateChild(titleBar, "StatusDot");
            dot.anchorMin = new Vector2(1f, 0.5f);
            dot.anchorMax = new Vector2(1f, 0.5f);
            dot.pivot = new Vector2(1f, 0.5f);
            dot.sizeDelta = new Vector2(9f, 9f);
            dot.anchoredPosition = new Vector2(-84f, 0f);

            _statusDot = dot.gameObject.AddComponent<Image>();
            _statusDot.sprite = UiSkin.Pill;
            _statusDot.type = Image.Type.Sliced;
            _statusDot.color = StatText;
            _statusDot.raycastTarget = false;

            _statusLabel = CreateText(titleBar, "StatusLabel", "", font, TitleFontSize - 10, FontStyle.Normal);
            _statusLabel.color = StatText;
            _statusLabel.alignment = TextAnchor.MiddleRight;
            Anchor(_statusLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 1f),
                   Vector2.zero, new Vector2(-14f, 0f));
        }

        /// <summary>
        /// Lays the three header runs end to end.
        ///
        /// Measured rather than guessed: <c>preferredWidth</c> is the only way to know where one
        /// run finishes in a proportional font, and a fixed offset would either overlap on a long
        /// character name or leave a gap on a short one.
        /// </summary>
        private void LayoutHeader(string count, string profile)
        {
            if (_header == null) return;

            _header.text = "Cookbook";
            var x = HeaderMarkWidth + _header.preferredWidth + 10f;

            _headerCount.text = count ?? "";
            _headerCount.rectTransform.offsetMin = new Vector2(x, _headerCount.rectTransform.offsetMin.y);
            x += _headerCount.preferredWidth + 8f;

            _headerProfile.text = profile ?? "";
            _headerProfile.rectTransform.offsetMin = new Vector2(x, _headerProfile.rectTransform.offsetMin.y);
        }

        /// <summary>Same measured run-on as the header: the tally starts where the title ends.</summary>
        private void LayoutCaption(string title, string count)
        {
            if (_caption == null) return;

            _caption.text = title ?? "";
            _captionCount.text = count ?? "";
            _captionCount.rectTransform.offsetMin =
                new Vector2(12f + _caption.preferredWidth + 12f, _captionCount.rectTransform.offsetMin.y);
        }

        private void SetStatus(bool live, string label)
        {
            if (_statusDot != null) _statusDot.color = live ? Neon : StatText;
            if (_statusLabel != null) _statusLabel.text = label;
        }

        // ---------- mix guide ----------

        // Two destination buttons side by side. Wide enough each to read as somewhere to go rather
        // than another toolbar control — they sit beside 72px strain tiles, and anything much
        // narrower reads as one of them.
        private const float GuideButtonWidth = 104f;

        /// <summary>Double width, so the leaf can be large enough to read as a mark.</summary>
        private const float MixGuideButtonWidth = GuideButtonWidth * 2f;

        /// <summary>
        /// How much of the strain strip the destination buttons take. Narrows when the Statistics
        /// screen is switched off, so the strip gets the space back rather than leaving a gap where
        /// a button used to be.
        /// </summary>
        private static float DestinationsWidth =>
            MixGuideButtonWidth + (UiFeatures.StatisticsScreen ? GuideButtonWidth + 6f : 0f);

        private MixGuideScreen _mixGuide;
        private StatsScreen _stats;

        /// <summary>
        /// Opens the mixing reference. Built lazily on first use rather than alongside the cookbook:
        /// it constructs a few hundred objects, and a player who never opens it should not pay for
        /// them every time a save loads.
        /// </summary>
        private void BuildGuideButton(Font font)
        {
            // Rightmost first, then leftward — offsets are measured from the right edge, so laying
            // them out in that order keeps the arithmetic obvious.
            var guideRight = -8f;

            if (UiFeatures.StatisticsScreen)
            {
                Destination(font, "StatsButton", "STATS", UiSkin.BarChart, -8f, GuideButtonWidth, () =>
                {
                    if (_stats == null) _stats = StatsScreen.CreateInto(_root, ResolveFont(_root));
                    _stats.Open(_model);
                });

                guideRight = -GuideButtonWidth - 14f;   // left of the Stats button
            }

            Destination(font, "GuideButton", "MIX GUIDE", UiSkin.PotLeaf, guideRight, MixGuideButtonWidth, () =>
            {
                if (_mixGuide == null) _mixGuide = MixGuideScreen.CreateInto(_root, ResolveFont(_root));
                _mixGuide.Open();
            });
        }

        /// <summary>
        /// A button that leaves the cookbook.
        ///
        /// Outlined in the accent, unlike every other control on the screen. Everything else here
        /// changes what the list shows; these two replace it entirely, and a destination should not
        /// look like a toggle.
        /// </summary>
        private void Destination(
            Font font, string name, string label, Sprite icon, float right, float width, Action open)
        {
            var button = CreateChild(_root, name);
            button.anchorMin = new Vector2(1f, 1f);
            button.anchorMax = new Vector2(1f, 1f);
            button.pivot = new Vector2(1f, 1f);
            button.offsetMin = new Vector2(right - width, StripBottom);
            button.offsetMax = new Vector2(right, StripTop);

            var image = button.gameObject.AddComponent<Image>();
            StyleRoundedButton(button, image);
            image.color = TileFillSelected;

            var outline = CreateChild(button, "Outline");
            Anchor(outline, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var outlineImage = outline.gameObject.AddComponent<Image>();
            outlineImage.sprite = UiSkin.Ring;
            outlineImage.type = Image.Type.Sliced;
            outlineImage.color = Neon;
            outlineImage.raycastTarget = false;

            // Wide buttons put the mark beside the words; narrow ones stack them. A 28px glyph
            // centred in a 200px-wide button reads as a mostly-empty box, and a big one stacked
            // above two lines of text does not fit the strip's height.
            var wide = width >= GuideButtonWidth * 1.5f;

            var glyph = CreateChild(button, "Glyph");
            var glyphSize = wide ? 60f : 28f;
            glyph.sizeDelta = new Vector2(glyphSize, glyphSize);

            if (wide)
            {
                glyph.anchorMin = new Vector2(0f, 0.5f);
                glyph.anchorMax = new Vector2(0f, 0.5f);
                glyph.pivot = new Vector2(0f, 0.5f);
                glyph.anchoredPosition = new Vector2(14f, 0f);
            }
            else
            {
                glyph.anchorMin = new Vector2(0.5f, 0f);
                glyph.anchorMax = new Vector2(0.5f, 0f);
                glyph.pivot = new Vector2(0.5f, 0f);
                glyph.anchoredPosition = new Vector2(0f, 8f);
            }

            var glyphImage = glyph.gameObject.AddComponent<Image>();
            glyphImage.sprite = icon;
            glyphImage.color = Neon;
            glyphImage.preserveAspect = true;
            glyphImage.raycastTarget = false;

            var text = CreateText(button, "Label", label, font, wide ? ToolFontSize + 6 : ToolFontSize + 2,
                                  FontStyle.Bold);
            text.color = Neon;

            if (wide)
            {
                text.alignment = TextAnchor.MiddleCenter;
                Anchor(text.rectTransform, Vector2.zero, Vector2.one,
                       new Vector2(glyphSize + 18f, 0f), new Vector2(-10f, 0f));
            }
            else
            {
                text.alignment = TextAnchor.UpperCenter;
                Anchor(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 34f), new Vector2(0f, -6f));
            }

            var clickable = button.gameObject.AddComponent<Button>();
            clickable.targetGraphic = image;
            clickable.colors = ButtonColours;
            UiInterop.OnClick(clickable, () =>
            {
                try
                {
                    // Whichever screen opens covers the cookbook, so a card left hanging over it
                    // would float above the new screen with nothing underneath to explain it.
                    HideEffects();
                    _pinnedProductId = null;
                    open();
                }
                catch (Exception ex)
                {
                    RecipePlannerUI.Log?.Error(name + " failed to open: " + ex);
                }
            });
        }

        // ---------- effects card ----------

        private RectTransform _effectsCard;
        private Image _effectsBackdrop;
        private Text _effectsTitle;
        private readonly List<Text> _effectChips = new List<Text>();
        private string _pinnedProductId;

        private const float CardWidth = 340f;
        private const float CardPad = 14f;
        private const float ChipHeight = 36f;
        private const float ChipGap = 6f;
        private const float CardTitleHeight = 34f;

        /// <summary>
        /// A floating card listing every effect a recipe carries.
        ///
        /// A child of the app root rather than of the list, deliberately: the viewport has a
        /// RectMask2D, so a card built inside it would be clipped the moment it extended past a row.
        /// Nothing in it is a raycast target either — it follows the pointer around and must never
        /// end up swallowing the click meant for whatever is underneath.
        ///
        /// Effects were previously invisible in-game. They were recorded per recipe from the moment
        /// of the cook and only ever surfaced in the exported cookbook.md.
        /// </summary>
        private void BuildEffectsCard(Font font)
        {
            _effectsCard = CreateChild(_root, "EffectsCard");
            _effectsCard.anchorMin = new Vector2(0f, 1f);
            _effectsCard.anchorMax = new Vector2(0f, 1f);
            _effectsCard.pivot = new Vector2(0f, 1f);
            _effectsCard.sizeDelta = new Vector2(CardWidth, 100f);

            // The card itself carries NO image. uGUI draws a parent's own graphic first and then
            // every child over it, so putting the glow on a child of a card that had its own
            // backdrop painted the glow straight over the top and washed the whole thing out.
            // Glow and body are siblings instead, in that order.
            var glowRect = CreateChild(_effectsCard, "Glow");
            Anchor(glowRect, Vector2.zero, Vector2.one, new Vector2(-10f, -10f), new Vector2(10f, 10f));
            var glow = glowRect.gameObject.AddComponent<Image>();
            glow.sprite = UiSkin.Glow;
            glow.type = Image.Type.Sliced;
            glow.raycastTarget = false;
            glow.color = CardGlow;

            var bodyRect = CreateChild(_effectsCard, "Body");
            Anchor(bodyRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _effectsBackdrop = bodyRect.gameObject.AddComponent<Image>();
            _effectsBackdrop.sprite = UiSkin.Body;
            _effectsBackdrop.type = Image.Type.Sliced;
            _effectsBackdrop.color = CardBackdrop;
            _effectsBackdrop.raycastTarget = false;

            _effectsTitle = CreateText(_effectsCard, "Title", "", font, NameFontSize, FontStyle.Bold);
            _effectsTitle.color = CardTitleText;
            _effectsTitle.alignment = TextAnchor.MiddleLeft;
            _effectsTitle.raycastTarget = false;
            Anchor(_effectsTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(CardPad, -CardPad - CardTitleHeight), new Vector2(-CardPad, -CardPad));

            _effectsCard.gameObject.SetActive(false);
        }

        /// <summary>
        /// Chips are pooled and reused. A recipe can carry eight effects or none, and rebuilding
        /// the card's children on every hover would allocate constantly while the pointer travels
        /// down a list.
        /// </summary>
        private Text ChipAt(int index, Font font)
        {
            while (_effectChips.Count <= index)
            {
                var chipRect = CreateChild(_effectsCard, "Chip" + _effectChips.Count);
                chipRect.anchorMin = new Vector2(0f, 1f);
                chipRect.anchorMax = new Vector2(1f, 1f);
                chipRect.pivot = new Vector2(0.5f, 1f);

                var background = chipRect.gameObject.AddComponent<Image>();
                background.sprite = UiSkin.Body;
                background.type = Image.Type.Sliced;
                background.color = ChipBackdrop;
                background.raycastTarget = false;

                var label = CreateText(chipRect, "Label", "", font, NameFontSize - 2, FontStyle.Bold);
                label.alignment = TextAnchor.MiddleLeft;
                label.raycastTarget = false;
                Anchor(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-8f, 0f));

                _effectChips.Add(label);
            }

            return _effectChips[index];
        }

        /// <summary>Shows the card for one entry, positioned beside the row it belongs to.</summary>
        private void ShowEffects(RectTransform row, CookbookEntry entry, Font font)
        {
            if (_effectsCard == null || entry == null || row == null) return;

            var effects = entry.Effects ?? new List<string>();
            _effectsTitle.text = effects.Count > 0
                ? entry.DisplayName
                : entry.DisplayName + "  —  no effects recorded";

            for (var i = 0; i < _effectChips.Count; i++)
                _effectChips[i].transform.parent.gameObject.SetActive(false);

            var y = -CardPad - CardTitleHeight - ChipGap;
            for (var i = 0; i < effects.Count; i++)
            {
                var chip = ChipAt(i, font);
                var chipRect = (RectTransform)chip.transform.parent;

                chipRect.gameObject.SetActive(true);
                chipRect.offsetMin = new Vector2(CardPad, 0f);
                chipRect.offsetMax = new Vector2(-CardPad, 0f);
                chipRect.sizeDelta = new Vector2(chipRect.sizeDelta.x, ChipHeight);
                chipRect.anchoredPosition = new Vector2(0f, y);

                chip.text = effects[i];
                chip.color = EffectColour(effects[i]);

                y -= ChipHeight + ChipGap;
            }

            var height = CardPad + CardTitleHeight + ChipGap
                       + Mathf.Max(effects.Count, 0) * (ChipHeight + ChipGap) + CardPad;
            _effectsCard.sizeDelta = new Vector2(CardWidth, height);

            PositionCardBeside(row, height);
            _effectsCard.gameObject.SetActive(true);
            _effectsCard.SetAsLastSibling();
        }

        /// <summary>
        /// Places the card to the right of the row, flipping it up when it would run off the
        /// bottom of the screen. Both rects are converted through world space because the row lives
        /// inside a scrolling container and the card does not.
        /// </summary>
        private void PositionCardBeside(RectTransform row, float height)
        {
            var corners = new Vector3[4];
            row.GetWorldCorners(corners);

            var topLeft = (Vector2)_root.InverseTransformPoint(corners[1]);
            var bottomRight = (Vector2)_root.InverseTransformPoint(corners[3]);

            var rootRect = _root.rect;
            var x = Mathf.Min(bottomRight.x - CardWidth - 56f, rootRect.xMax - CardWidth - 8f);
            x = Mathf.Max(x, rootRect.xMin + 8f);

            var y = topLeft.y;
            if (y - height < rootRect.yMin + 8f) y = rootRect.yMin + 8f + height;
            y = Mathf.Min(y, rootRect.yMax - 8f);

            _effectsCard.anchoredPosition = new Vector2(x - rootRect.xMin, y - rootRect.yMax);
        }

        private void HideEffects()
        {
            if (_effectsCard != null) _effectsCard.gameObject.SetActive(false);
        }

        /// <summary>
        /// A stable colour per effect name, so "Sneaky" is the same shade every time it appears and
        /// the eye learns them. Derived from the name rather than a table because the game's effect
        /// list is data, not a constant of ours — a future update adding one should not need a code
        /// change here. Saturation and lightness are fixed so nothing comes out muddy or unreadable.
        /// </summary>
        private static Color EffectColour(string effect)
        {
            if (string.IsNullOrEmpty(effect)) return NameText;

            // The game's own LabelColor where we have it, so the card matches what the Products
            // screen shows for the same effect. Reading it costs nothing — the mix guide already
            // has every effect loaded — and a colour that disagrees with the game's is worse than
            // no colour at all, because the player learns the wrong association.
            var real = RealEffectColour(effect);
            if (real.HasValue) return real.Value;

            // Nothing loaded yet, or an effect the guide has never seen. A hue derived from the
            // name at least stays stable between openings.
            unchecked
            {
                var hash = 17;
                foreach (var c in effect) hash = hash * 31 + char.ToLowerInvariant(c);
                var hue = Mathf.Abs(hash % 360) / 360f;
                return Color.HSVToRGB(hue, 0.52f, 1f);
            }
        }

        /// <summary>
        /// Effects are keyed by id in the guide but the cookbook carries display names, so both are
        /// checked. Cached because this runs once per chip on every hover.
        /// </summary>
        private static Dictionary<string, Color> _effectColours;

        /// <summary>Dropped when the app is rebuilt; the guide behind these is per save.</summary>
        internal static void ForgetEffectColours() => _effectColours = null;

        private static Color? RealEffectColour(string effect)
        {
            try
            {
                if (_effectColours == null)
                {
                    var guide = RecipePlannerUI.MixGuideSource?.Invoke();
                    if (guide == null || guide.Effects.Count == 0) return null;

                    _effectColours = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
                    foreach (var known in guide.Effects)
                    {
                        if (known == null) continue;
                        var colour = new Color(known.ColourR, known.ColourG, known.ColourB, 1f);
                        if (!string.IsNullOrEmpty(known.Id)) _effectColours[known.Id] = colour;
                        if (!string.IsNullOrEmpty(known.Name)) _effectColours[known.Name] = colour;
                    }
                }

                Color found;
                return _effectColours.TryGetValue(effect, out found) ? found : (Color?)null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gives a control the rounded body and the underglow that lights on hover.
        ///
        /// The glow is a sibling drawn *behind* the body and inset outwards, so it reads as light
        /// spilling from underneath rather than as a border. It sits at low alpha at rest and is
        /// lifted by <see cref="HoverGlow"/>, which drives it in step with uGUI's own colour tint
        /// on the body — tinting only the body makes it pop while the glow stays flat.
        /// </summary>
        private static Image StyleRoundedButton(RectTransform target, Image body, bool pill = false)
        {
            var glowRect = CreateChild(target, "Glow");
            Anchor(glowRect, Vector2.zero, Vector2.one,
                   new Vector2(-GlowInset, -GlowInset), new Vector2(GlowInset, GlowInset));

            // Behind the body, and behind anything added later (label, icon).
            glowRect.SetAsFirstSibling();

            var glow = glowRect.gameObject.AddComponent<Image>();
            glow.sprite = UiSkin.Glow;
            glow.type = Image.Type.Sliced;
            glow.raycastTarget = false;   // never steal the click from the button underneath it
            glow.color = GlowRest;

            body.sprite = pill ? UiSkin.Pill : UiSkin.Body;
            body.type = Image.Type.Sliced;
            body.color = ButtonIdle;

            // The outline carries the shape at rest; the fill is nearly the background colour, so
            // without it a toolbar of unselected pills would be invisible against the panel.
            var border = CreateChild(target, "Border");
            Anchor(border, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var borderImage = border.gameObject.AddComponent<Image>();
            borderImage.sprite = pill ? UiSkin.Pill : UiSkin.Ring;
            borderImage.type = Image.Type.Sliced;
            borderImage.color = CardBorder;
            borderImage.raycastTarget = false;

            // A pill has no ring sprite of its own, so its border is a slightly larger pill sitting
            // behind the fill — the fill covers all but a hairline of it.
            if (pill)
            {
                border.SetSiblingIndex(1);
                Anchor(border, Vector2.zero, Vector2.one,
                       new Vector2(-1.2f, -1.2f), new Vector2(1.2f, 1.2f));
                borderImage.color = new Color(1f, 1f, 1f, 0.10f);
            }

            HoverGlow.Attach(target.gameObject, glow, GlowRest, GlowHot);
            return glow;
        }

        // ---------- palette ----------

        private const float GlowInset = 6f;

        /// <summary>
        /// Hover is a good deal brighter than rest on purpose. The phone is viewed at a distance in
        /// a dim scene, and the subtle 0.10 → 0.15 lift uGUI defaults to is invisible there.
        /// </summary>
        private static ColorBlock ButtonColours => new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(1.35f, 1.45f, 1.38f, 1f),
            pressedColor = new Color(0.72f, 0.82f, 0.75f, 1f),
            selectedColor = Color.white,
            disabledColor = new Color(1f, 1f, 1f, 0.35f),
            colorMultiplier = 1f,
            fadeDuration = 0.09f,
        };

        // Green, to match the effects card, and dialled well back from the first pass. At rest the
        // glow should read as a soft edge rather than a light source; only hover should announce
        // itself. A row of permanently glowing buttons is noise, and noise everywhere is the same
        // as nowhere — nothing stands out when the pointer actually lands.
        // One accent, used everywhere something is active, selected or worth money. A single strong
        // colour on near-black is what makes the layout readable at a glance — every extra hue
        // competes with it and the eye stops knowing which one means "look here".
        private static readonly Color Neon = new Color(0.24f, 0.92f, 0.44f, 1f);
        private static readonly Color NeonDim = new Color(0.24f, 0.92f, 0.44f, 0.30f);

        private static readonly Color GlowRest = new Color(0.24f, 0.92f, 0.44f, 0.05f);
        private static readonly Color GlowHot = new Color(0.30f, 1f, 0.50f, 0.30f);

        private static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);

        private static readonly Color AppBackground = new Color(0.024f, 0.035f, 0.028f, 1f);
        private static readonly Color HeaderFill = new Color(0.042f, 0.062f, 0.050f, 1f);

        private static readonly Color CardFill = new Color(0.055f, 0.078f, 0.064f, 1f);
        private static readonly Color CardFillAlt = new Color(0.045f, 0.065f, 0.053f, 1f);
        private static readonly Color CardBorder = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color CardFillHot = new Color(0.075f, 0.115f, 0.090f, 1f);

        private static readonly Color ButtonIdle = new Color(0.075f, 0.105f, 0.086f, 1f);
        private static readonly Color RowEdge = new Color(1f, 1f, 1f, 0.04f);

        private static readonly Color TileBorderIdle = new Color(1f, 1f, 1f, 0.09f);
        private static readonly Color TileBorderSelected = Neon;
        private static readonly Color TileFillIdle = new Color(0.055f, 0.078f, 0.064f, 1f);
        private static readonly Color TileFillSelected = new Color(0.09f, 0.20f, 0.12f, 1f);

        private static readonly Color HeaderText = Neon;
        private static readonly Color TitleText = new Color(0.94f, 0.97f, 0.95f, 1f);
        private static readonly Color NameText = new Color(0.93f, 0.96f, 0.94f, 1f);
        private static readonly Color ChainText = new Color(0.48f, 0.56f, 0.51f, 1f);
        private static readonly Color StatText = new Color(0.52f, 0.62f, 0.55f, 1f);
        private static readonly Color PriceText = Neon;

        // The meter is one colour now. It was a yellow-to-red ramp, which read as a warning — but
        // addictiveness is not a hazard in this game, it is a selling point, so a rising green bar
        // says "more of the thing you want" rather than "careful".
        private static readonly Color BarFill = Neon;
        private static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color RowStripe = new Color(1f, 1f, 1f, 0.02f);
        private static readonly Color Favourite = new Color(1f, 0.82f, 0.32f, 1f);

        /// <summary>How far a hidden row is dimmed. Low enough to read as set aside, high enough to stay legible.</summary>
        private const float DimFactor = 0.42f;

        // Green, and much softer than the first attempt: the card sits over the list, so a strong
        // halo bleeds into the rows behind it and makes both harder to read.
        private static readonly Color CardBackdrop = new Color(0.055f, 0.105f, 0.075f, 0.985f);
        private static readonly Color CardGlow = new Color(0.30f, 0.95f, 0.45f, 0.13f);
        private static readonly Color ChipBackdrop = new Color(0.35f, 0.95f, 0.50f, 0.10f);
        private static readonly Color CardTitleText = new Color(0.62f, 0.96f, 0.68f, 1f);

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
        /// One green, brightening slightly as it fills.
        ///
        /// This replaced an amber-to-red ramp that existed for a specific reason: the meter shares
        /// a row with the price, and matching its colour implies a relationship between the two
        /// that does not exist. That objection is still true — it is simply outweighed. A red bar
        /// reads as a warning, and addictiveness in this game is a selling point rather than a
        /// hazard, so the old colour was miscommunicating something worse.
        ///
        /// The confusion is kept small by form rather than hue: the price is large bold text, the
        /// meter is a thin bar, and nothing else on the row is shaped like either.
        /// </summary>
        private static Color AddictionColour(float addictiveness) =>
            Color.Lerp(new Color(0.20f, 0.72f, 0.36f, 1f), BarFill, Mathf.Clamp01(addictiveness));

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
            var go = UiInterop.NewRect(name);
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
