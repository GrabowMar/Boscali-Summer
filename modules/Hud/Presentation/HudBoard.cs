using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Hud.Configuration;
using BoscaliSummer.Features.Hud.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Hud.Presentation
{
    /// <summary>
    /// The one cockpit HUD element every Boscali presentation feature draws through: one canvas,
    /// one bounded stack of vanilla-styled lines, one palette, one place to configure.
    ///
    /// <para>A feature either holds a line while its condition is true (the accepted-contract
    /// list) or pushes a transient notice that expires on its own. The board
    /// owns the canvas, the sorting order, the placement, the stacking, the visibility rule and
    /// every presentation setting; a feature owns only its own words.</para>
    ///
    /// <para>By default the block hangs directly under the vanilla weapon and capacitor column,
    /// right-aligned to it and at its own width, so the two read as one column instead of two
    /// unrelated overlays. The column's live rectangle is measured from `CombatHUD.topRightPanel`
    /// once a second — read only, never written — and the fixed offsets in `HudLayout` are the
    /// fallback for a scene where it cannot be resolved.</para>
    /// </summary>
    internal sealed class HudBoard : MonoBehaviour, ISceneService, IHudBoard
    {
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;

        /// <summary>Vertical air between two lines of the stack, at the reference resolution.</summary>
        private const float RowGap = 5f;

        /// <summary>Notices never take the whole element: two slots stay theirs while they live.</summary>
        private const int ReservedNoticeRows = 2;

        private const float StyleRetrySeconds = 2f;

        /// <summary>The vanilla column moves only when the player changes their HMD options.</summary>
        private const float MeasureSeconds = 1f;

        /// <summary>
        /// The vanilla column deliberately overhangs the screen edge, so its own right edge
        /// cannot be matched exactly: the block clamps just inside the canvas instead.
        /// </summary>
        private const float ColumnOverhangAllowance = 16f;

        private const float FadeInSeconds = 0.14f;
        private const float FadeOutSeconds = 0.24f;

        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int OutlineColourId = Shader.PropertyToID("_OutlineColor");
        private static readonly int UnderlayColourId = Shader.PropertyToID("_UnderlayColor");

        /// <summary>Ten hertz: fast enough for a range readout, cheap enough to ignore.</summary>
        private const float TickSeconds = 0.1f;

        /// <summary>A line nobody re-set inside this window is stale and drops off.</summary>
        private const float StaleSeconds = 1.5f;

        /// <summary>Lines the board will create. Past this a feature is refused, once.</summary>
        private const int LineCeiling = 10;

        private readonly List<HudRow> held = new List<HudRow>(LineCeiling);
        private readonly List<HudRow> noticeRows = new List<HudRow>(HudNoticeQueue.Capacity);
        private readonly List<HudRow> drawn = new List<HudRow>(LineCeiling);
        private readonly List<Channel> channels = new List<Channel>(HudLayout.MaxChannels);
        private readonly HudNoticeQueue notices = new HudNoticeQueue();
        private readonly Vector3[] corners = new Vector3[4];

        private HudSettings settings;
        private ManualLogSource log;

        private GameObject root;
        private Canvas canvas;
        private CanvasGroup group;
        private RectTransform stack;

        private HudStyle style;
        private Material outlineMaterial;
        private bool styleReady;
        private bool warnedNoStyle;
        private float nextStyleAttempt;
        private float nextTick;
        private float alpha;

        private bool relayout = true;
        private bool feedsCeilingLogged;
        private bool linesCeilingLogged;

        private float appliedScale = -1f;
        private bool appliedRight;

        private bool columnMeasured;
        private float columnRight;
        private float columnBottom;
        private float nextMeasure;

        public void Configure(HudSettings hudSettings, ManualLogSource logger)
        {
            settings = hudSettings;
            log = logger;
        }

        // ------------------------------------------------------------------ IHudBoard

        public void DeclareChannel(string key, string label)
        {
            if (string.IsNullOrEmpty(key)) return;
            for (int i = 0; i < channels.Count; i++)
                if (channels[i].Key == key) return;
            if (channels.Count >= HudLayout.MaxChannels)
            {
                WarnCeiling(true, HudLayout.MaxChannels, key);
                return;
            }
            channels.Add(new Channel(this, key, string.IsNullOrEmpty(label) ? key.ToUpperInvariant() : label));
        }

        public IHudLine Acquire(string owner, string channel, string key)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < held.Count; i++)
                if (held[i].Owner == owner && held[i].Key == key) return held[i];

            if (held.Count >= LineCeiling)
            {
                WarnCeiling(false, LineCeiling, key);
                return null;
            }

            EnsureRoot();
            var row = new HudRow(owner, channel, key, stack);
            held.Add(row);
            relayout = true;
            return row;
        }

        public void Notice(string channel, HudTone tone, string text, string detail = null)
        {
            if (settings == null || !settings.Notices.Value || !ChannelEnabled(channel)) return;
            notices.Push(channel, tone, text, detail, Time.unscaledTime,
                HudLayout.ClampNoticeSeconds(settings.NoticeSeconds.Value));
        }

        public void ReleaseOwner(string owner)
        {
            for (int i = held.Count - 1; i >= 0; i--)
            {
                if (held[i].Owner != owner) continue;
                held[i].Destroy();
                held.RemoveAt(i);
            }
            relayout = true;
        }

        public IReadOnlyList<IHudChannel> Channels => channels;

        public bool Enabled
        {
            get => settings == null || settings.Enabled.Value;
            set
            {
                if (settings != null) settings.Enabled.Value = value;
            }
        }

        public HudAnchor Anchor
        {
            get => settings != null ? settings.ResolvedAnchor() : HudAnchor.UnderWeapons;
            set
            {
                if (settings == null) return;
                settings.Anchor.Value = HudLayout.ClampAnchor((int)value);
                relayout = true;
            }
        }

        public int ScaleStep
        {
            get => settings != null ? HudLayout.ClampScale(settings.ScaleStep.Value) : 1;
            set
            {
                if (settings == null) return;
                settings.ScaleStep.Value = HudLayout.ClampScale(value);
                relayout = true;
            }
        }

        public int OpacityStep
        {
            get => settings != null ? HudLayout.ClampOpacity(settings.OpacityStep.Value) : 1;
            set
            {
                if (settings == null) return;
                settings.OpacityStep.Value = HudLayout.ClampOpacity(value);
            }
        }

        public int MaxRows
        {
            get => settings != null ? HudLayout.ClampRows(settings.MaxRows.Value) : 4;
            set
            {
                if (settings == null) return;
                settings.MaxRows.Value = HudLayout.ClampRows(value);
            }
        }

        public bool NoticesEnabled
        {
            get => settings == null || settings.Notices.Value;
            set
            {
                if (settings == null || settings.Notices.Value == value) return;
                settings.Notices.Value = value;
                if (!value) notices.Clear();
            }
        }

        public float NoticeSeconds
        {
            get => settings != null
                ? HudLayout.ClampNoticeSeconds(settings.NoticeSeconds.Value)
                : HudLayout.DefaultNoticeSeconds;
            set
            {
                if (settings == null) return;
                settings.NoticeSeconds.Value = HudLayout.ClampNoticeSeconds(value);
            }
        }

        // ------------------------------------------------------------------- lifecycle

        public void ResetForScene()
        {
            for (int i = 0; i < held.Count; i++) held[i].Destroy();
            for (int i = 0; i < noticeRows.Count; i++) noticeRows[i].Destroy();
            held.Clear();
            noticeRows.Clear();
            drawn.Clear();
            notices.Clear();
            channels.Clear();
            if (root != null) Destroy(root);
            if (outlineMaterial != null) Destroy(outlineMaterial);
            outlineMaterial = null;
            root = null;
            canvas = null;
            group = null;
            stack = null;
            style = default;
            styleReady = false;
            warnedNoStyle = false;
            feedsCeilingLogged = false;
            linesCeilingLogged = false;
            columnMeasured = false;
            columnRight = 0f;
            columnBottom = 0f;
            nextStyleAttempt = 0f;
            nextMeasure = 0f;
            nextTick = 0f;
            alpha = 0f;
            relayout = true;
            appliedScale = -1f;
            VanillaHudStyle.Invalidate();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (Application.isBatchMode || settings == null || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + TickSeconds;

            ResolveStyle();
            notices.Expire(Time.unscaledTime);
            if (Time.unscaledTime >= nextMeasure)
            {
                nextMeasure = Time.unscaledTime + MeasureSeconds;
                MeasureColumn();
            }

            // A value written straight into a config entry (a hand-edited file) never passes
            // through a setter, so the layout is re-checked against the live ladder here rather
            // than trusting a relayout flag to be raised for us.
            if (!relayout)
            {
                HudPlacement live = HudLayout.Place(Anchor);
                if (appliedScale < 0f || !Mathf.Approximately(appliedScale, HudLayout.Scale(ScaleStep)) ||
                    appliedRight != live.AlignRight) relayout = true;
            }

            bool wanted = settings.Enabled.Value && HudLayout.Opacity(OpacityStep) > 0f && CanShow() && HasContent();
            if (!wanted && root == null) return;

            EnsureRoot();
            if (root == null) return;
            if (relayout) Layout();
            Drive();
            Fade(wanted);
        }

        /// <summary>
        /// The pilot's view: a live aircraft in a mission camera with the map closed. No line on
        /// the element answers to a rule of its own, so this is the one place it is decided.
        /// </summary>
        private static bool CanShow()
        {
            if (DynamicMap.mapMaximized) return false;
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null ||
                aircraft.disabled || aircraft.HasEjected()) return false;

            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null || cameras.currentState == null) return false;
            return cameras.currentState == cameras.cockpitState ||
                   cameras.currentState == cameras.orbitState ||
                   cameras.currentState == cameras.chaseState;
        }

        private bool HasContent()
        {
            for (int i = 0; i < held.Count; i++)
                if (!held[i].Released && !Stale(held[i])) return true;
            return notices.Count > 0;
        }

        private static bool Stale(HudRow row) => Time.unscaledTime - row.Refreshed > StaleSeconds;

        private bool ChannelEnabled(string key)
        {
            for (int i = 0; i < channels.Count; i++)
                if (channels[i].Key == key) return channels[i].Enabled;
            return true;
        }

        private void WarnCeiling(bool feeds, int ceiling, string key)
        {
            if (feeds ? feedsCeilingLogged : linesCeilingLogged) return;
            if (feeds) feedsCeilingLogged = true; else linesCeilingLogged = true;
            log?.LogWarning("Common HUD element is at its ceiling of " + ceiling + " " +
                (feeds ? "feeds" : "lines") + "; '" + key + "' was dropped. " +
                "This is a bounded pool, not a queue.");
        }

        // ---------------------------------------------------------------------- build

        private void EnsureRoot()
        {
            if (root != null) return;

            root = new GameObject("Boscali HUD",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            root.transform.SetParent(transform, false);

            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = ResolveSortingOrder();
            canvas.pixelPerfect = false;

            // The vanilla HUD canvas ships exactly these scaler values, so both canvases measure
            // in the same units and the block lines up with the column it hangs under.
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            group = root.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = 0f;

            var stackObject = new GameObject("Stack", typeof(RectTransform));
            stack = (RectTransform)stackObject.transform;
            stack.SetParent(root.transform, false);

            for (int i = 0; i < HudNoticeQueue.Capacity; i++)
                noticeRows.Add(new HudRow("notice", "notice", "n" + i, stack, notice: true));

            // The canvas has no screen rectangle until the end of this frame, so the vanilla
            // column is measured on the next tick rather than against a rect that is not there.
            nextMeasure = Time.unscaledTime + TickSeconds;

            log?.LogInfo("Common HUD element installed.");
        }

        /// <summary>
        /// The cockpit HUD canvas, one order above it, so the block shares vanilla's pixel
        /// measurement and draw layer while the weapon panel it hangs under can never clip it.
        /// The game names no sorting order in code, so the FlightHud hierarchy is the only honest
        /// way to tell that canvas apart from the map or the menu canvases.
        /// </summary>
        private static int ResolveSortingOrder()
        {
            FlightHud hud = SceneSingleton<FlightHud>.i;
            Canvas owner = hud != null ? FindOwningCanvas(hud.transform) : null;
            if (owner != null) return owner.sortingOrder + 1;

            Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid()) continue;
                if (candidate.GetComponentInChildren<FlightHud>(true) != null) return candidate.sortingOrder + 1;
            }

            return 1;
        }

        private static Canvas FindOwningCanvas(Transform start)
        {
            Transform cursor = start;
            while (cursor != null)
            {
                Canvas canvas = cursor.GetComponent<Canvas>();
                if (canvas != null) return canvas;
                cursor = cursor.parent;
            }
            return null;
        }

        /// <summary>
        /// Read vanilla's own HUD look, retrying while the objective prefab has not spawned yet.
        /// A scene that never resolves it gets the engine font and the stock palette, and the
        /// element still works.
        /// </summary>
        private void ResolveStyle()
        {
            if (styleReady) return;

            if (Time.unscaledTime >= nextStyleAttempt)
            {
                nextStyleAttempt = Time.unscaledTime + StyleRetrySeconds;
                if (VanillaHudStyle.TryCockpit(out VanillaHudStyle.CockpitStyle cockpit) && cockpit.Font != null)
                {
                    VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
                    Material material = WithOutline(cockpit.FontMaterial);
                    if (!ReferenceEquals(material, cockpit.FontMaterial)) outlineMaterial = material;
                    style = new HudStyle
                    {
                        Font = cockpit.Font,
                        FontMaterial = material,
                        Ink = cockpit.Colour.a > 0.05f ? cockpit.Colour : palette.AllClear,
                        AllClear = palette.AllClear,
                        Warning = palette.Warning,
                        Alert = palette.Alert,
                        TextSize = VanillaHudStyle.ObjectiveTextSize
                    };
                    styleReady = true;
                    relayout = true;
                    log?.LogInfo("Common HUD element resolved the vanilla HUD style: " +
                        style.TextSize.ToString("0.#") + " px labels, " +
                        (ReferenceEquals(material, cockpit.FontMaterial) ? "vanilla label material." : "outlined label material."));
                    return;
                }

                if (!warnedNoStyle)
                {
                    warnedNoStyle = true;
                    log?.LogInfo("Common HUD element is waiting for the vanilla HUD style; " +
                        "using the engine font until it resolves.");
                }
            }

            if (style.Font == null) style.Font = TMP_Settings.defaultFontAsset;
            style.FontMaterial = null;
            style.Ink = new Color(0f, 1f, 0f);
            style.AllClear = new Color(0f, 1f, 0f);
            style.Warning = new Color(1f, 1f, 0f);
            style.Alert = new Color(1f, 0f, 0f);
            if (style.TextSize < 1f) style.TextSize = 16f;
        }

        /// <summary>
        /// The vanilla objective label's own material already solves readability over sky, so it
        /// is copied as-is. When it carries neither an outline nor an underlay, one clone is made
        /// for the whole element with a thin black outline added — a saturated green or red line
        /// over a sunlit cloud measures barely 1.3:1 without it. The vanilla asset itself is
        /// never written to.
        /// </summary>
        private static Material WithOutline(Material source)
        {
            if (source == null) return null;
            if (!source.HasProperty(OutlineWidthId) || !source.HasProperty(OutlineColourId)) return source;
            if (source.GetFloat(OutlineWidthId) > 0.001f) return source;
            if (source.HasProperty(UnderlayColourId) && source.GetColor(UnderlayColourId).a > 0.001f) return source;

            var clone = new Material(source) { name = "Boscali HUD label (outlined)" };
            clone.SetFloat(OutlineWidthId, 0.12f);
            clone.SetColor(OutlineColourId, new Color(0f, 0f, 0f, 1f));
            return clone;
        }

        /// <summary>
        /// Measure the vanilla weapon and capacitor column, in this canvas's own units. Read
        /// only: the column's transform is never written to, re-parented or disabled. A scene
        /// without it keeps the fixed fallback offsets.
        ///
        /// <para>Unity assigns a screen-space canvas its screen rectangle and position at the end
        /// of the frame it was created in, so a measurement taken before that reads numbers in a
        /// different space entirely. Every reading is checked against the canvas rect before it is
        /// believed, and the first attempt is deferred past the creation frame.</para>
        /// </summary>
        private void MeasureColumn()
        {
            columnMeasured = false;
            if (stack == null || canvas == null) return;

            var canvasRect = (RectTransform)root.transform;
            Rect rect = canvasRect.rect;
            if (rect.width < 100f || rect.height < 100f) return;
            if (!VanillaHudStyle.TryWeaponPanel(out RectTransform panel) || panel == null) return;

            panel.GetWorldCorners(corners);
            Vector3 bottomLeft = canvasRect.InverseTransformPoint(corners[0]);
            Vector3 topRight = canvasRect.InverseTransformPoint(corners[2]);
            if (float.IsNaN(bottomLeft.x) || float.IsNaN(topRight.x)) return;

            float right = Mathf.Max(bottomLeft.x, topRight.x);
            float bottom = Mathf.Min(bottomLeft.y, topRight.y);
            if (bottom > rect.yMax || bottom < rect.yMin) return;
            if (right > rect.xMax + ColumnOverhangAllowance) return;

            columnRight = right;
            columnBottom = bottom;
            columnMeasured = true;
        }

        /// <summary>Re-measure every line. Runs on a scale or anchor change only, never per tick.</summary>
        private void Layout()
        {
            relayout = false;
            float scale = HudLayout.Scale(ScaleStep);
            HudPlacement place = HudLayout.Place(Anchor);
            appliedScale = scale;
            appliedRight = place.AlignRight;
            for (int i = 0; i < held.Count; i++) held[i].Layout(style, scale, place.AlignRight);
            for (int i = 0; i < noticeRows.Count; i++) noticeRows[i].Layout(style, scale, place.AlignRight);
        }

        // --------------------------------------------------------------------- drive

        /// <summary>
        /// Pick this tick's lines and stack them. Held lines come first, ordered loudest-first
        /// and then oldest-first, so a warning never hides under an all-clear and two lines of
        /// equal weight never swap places between ticks. Notices follow, oldest first, with two
        /// slots reserved for them so a live notice cannot be crowded off by standing status.
        /// </summary>
        private void Drive()
        {
            float scale = HudLayout.Scale(ScaleStep);
            HudPlacement place = HudLayout.Place(Anchor);

            drawn.Clear();
            for (int i = 0; i < held.Count; i++)
            {
                HudRow row = held[i];
                if (row.Released || Stale(row) || !ChannelEnabled(row.Channel)) continue;
                Insert(row);
            }

            // Notices are drawn first, at the top, so a live one always has a slot and is never
            // crowded out by standing status. Held lines take what is left, loudest first.
            int max = MaxRows;
            int noticeCount = notices.Count < ReservedNoticeRows ? notices.Count : ReservedNoticeRows;
            if (settings != null && !settings.Notices.Value) noticeCount = 0;
            int heldBudget = max - noticeCount;
            if (heldBudget < 1) heldBudget = 1;
            while (drawn.Count > heldBudget) drawn.RemoveAt(drawn.Count - 1);
            while (noticeCount > 0 && noticeCount + drawn.Count > max) noticeCount--;

            // Only the newest notices are worth a slot: an entry that is about to expire has
            // already been read, and the queue itself drops the oldest when it overflows.
            int firstNotice = notices.Count - noticeCount;

            float top = 0f;
            for (int i = 0; i < noticeCount; i++)
            {
                if (!notices.TryGet(firstNotice + i, out HudTone tone, out string text, out string detail)) continue;
                HudRow row = noticeRows[i];
                row.Set(tone, text, detail, 0f);
                row.Place(top);
                row.Paint(style);
                row.Show(true);
                top -= row.Height(style, scale) + RowGap;
            }

            for (int i = 0; i < drawn.Count; i++)
            {
                HudRow row = drawn[i];
                row.Place(top);
                row.Paint(style);
                row.Show(true);
                top -= row.Height(style, scale) + RowGap;
            }

            for (int i = 0; i < held.Count; i++) held[i].Show(drawn.Contains(held[i]));
            for (int i = noticeCount; i < noticeRows.Count; i++) noticeRows[i].Show(false);

            PlaceStack(place, Mathf.Max(0f, -top - RowGap));
        }

        /// <summary>
        /// Place a row in the stack, loudest first, and stable for equal tones so two lines of
        /// the same weight never swap places between ticks.
        /// </summary>
        private void Insert(HudRow row)
        {
            int index = drawn.Count;
            for (int i = 0; i < drawn.Count; i++)
            {
                if (Rank(row) > Rank(drawn[i]))
                {
                    index = i;
                    break;
                }
            }
            drawn.Insert(index, row);
        }

        private static int Rank(HudRow row) => (int)row.Tone;

        /// <summary>
        /// Park the stack. Under the weapon column the offsets come from the column's measured
        /// rectangle so the block shares its right edge and starts clear of its background; every
        /// other anchor uses the fixed ladder. Rows hang from the stack's top edge, so a bottom
        /// anchor stacks upwards without a row needing to know which way the stack is going.
        /// </summary>
        private void PlaceStack(HudPlacement place, float height)
        {
            float offsetX = place.OffsetX;
            float offsetY = place.OffsetY;

            if (place.FollowsWeaponColumn && columnMeasured)
            {
                var canvasRect = (RectTransform)root.transform;
                offsetX = Mathf.Min(columnRight, canvasRect.rect.xMax) - canvasRect.rect.xMax;
                offsetY = columnBottom - canvasRect.rect.yMax - HudLayout.ColumnGap;
            }

            stack.anchorMin = stack.anchorMax = new Vector2(place.AnchorX, place.AnchorY);
            stack.pivot = new Vector2(place.PivotX, place.PivotY);
            stack.anchoredPosition = new Vector2(
                SafeOffset(HorizontalEdge(place), offsetX),
                SafeOffset(VerticalEdge(place), offsetY));
            stack.sizeDelta = new Vector2(HudLayout.BlockWidth, height);
        }

        private static SideEdge HorizontalEdge(HudPlacement place) =>
            place.AnchorX < 0.5f ? SideEdge.Left : place.AnchorX > 0.5f ? SideEdge.Right : SideEdge.None;

        private static SideEdge VerticalEdge(HudPlacement place) =>
            place.AnchorY > 0.5f ? SideEdge.Top : place.AnchorY < 0.5f ? SideEdge.Bottom : SideEdge.None;

        private enum SideEdge { None, Left, Right, Top, Bottom }

        /// <summary>
        /// Move an edge offset inward by the screen's overscan or notch. A left or bottom offset
        /// is positive and grows; a top or right offset is negative, so it has to shrink, or the
        /// correction would walk the block off the screen it was meant to keep it on.
        /// </summary>
        private float SafeOffset(SideEdge edge, float offset)
        {
            float inset = SafeInset(edge);
            switch (edge)
            {
                case SideEdge.Left:
                case SideEdge.Bottom:
                    return offset + inset;
                case SideEdge.Right:
                case SideEdge.Top:
                    return offset - inset;
                default:
                    return offset;
            }
        }

        /// <summary>
        /// Overscan and notches, in reference units. Costs nothing on a desktop monitor and is
        /// the difference between a readable element and a clipped one on a TV or a headset.
        /// </summary>
        private float SafeInset(SideEdge edge)
        {
            if (edge == SideEdge.None || canvas == null) return 0f;
            Rect safe = Screen.safeArea;
            float factor = canvas.scaleFactor > 0.001f ? canvas.scaleFactor : 1f;
            switch (edge)
            {
                case SideEdge.Left: return safe.xMin / factor;
                case SideEdge.Right: return (Screen.width - safe.xMax) / factor;
                case SideEdge.Top: return (Screen.height - safe.yMax) / factor;
                default: return safe.yMin / factor;
            }
        }

        private void Fade(bool wanted)
        {
            float target = wanted ? HudLayout.Opacity(OpacityStep) : 0f;
            if (!Mathf.Approximately(alpha, target))
            {
                float speed = (target > alpha ? 1f / FadeInSeconds : 1f / FadeOutSeconds) * Time.unscaledDeltaTime;
                alpha = Mathf.MoveTowards(alpha, target, speed);
                group.alpha = alpha;
            }
            bool active = wanted || alpha > 0.001f;
            if (root.activeSelf != active) root.SetActive(active);
        }

        /// <summary>
        /// One declared feed. Enabled state lives in the config entry rather than in a field, so
        /// the settings page, the board and a hand-edited config file can never disagree.
        /// </summary>
        private sealed class Channel : IHudChannel
        {
            private readonly HudBoard board;

            internal Channel(HudBoard owner, string key, string label)
            {
                board = owner;
                Key = key;
                Label = label;
            }

            public string Key { get; }
            public string Label { get; }
            public bool Enabled => board.settings == null || board.settings.ChannelEnabled(Key);

            public void Toggle()
            {
                if (board.settings == null) return;
                board.settings.SetChannel(Key, !Enabled);
            }
        }
    }
}
