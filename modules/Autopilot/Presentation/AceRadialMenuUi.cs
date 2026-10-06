using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Configuration;
using BoscaliSummer.Modules.Autopilot.Domain;
using BoscaliSummer.Modules.Autopilot.Runtime;
using BoscaliSummer.Core.Lifecycle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoscaliSummer.Modules.Autopilot.Presentation
{
    /// <summary>
    /// ACE3-style self-interaction menu on the configured key (default C), presented on the
    /// shared avionics kit so it reads as one system with the HUD. Hold the key: the cursor is
    /// freed at screen centre, flying and looking stop, and the options fan out from the centre
    /// node. Rest on a branch to expand it; release the key over an option to run it. A quick
    /// tap latches the menu open for mouse clicks instead. Right-click, the key again, Esc, the
    /// map or pausing closes it.
    ///
    /// Every option is an ACE3-style icon disc with its data tag beside it on the outward side:
    /// the label on top, the live state line under it in its tone colour, and a tone rail on the
    /// tag's outer edge. Hover fills the disc; the open breadcrumb keeps a bright outline.
    /// </summary>
    internal sealed class AceRadialMenuUi : MonoBehaviour, ISceneService
    {
        private const float TapSeconds = 0.25f;
        private const int WidgetCount = AceRadialMenuTree.MaxVisible + 1;
        private const int VectorCapacity = (WidgetCount * 72) + 160;
        private const int PlateCapacity = (WidgetCount * 4) + 24;
        private const int SelectorCapacity = 12;
        private const CursorFlags ForeignCursorOwners =
            CursorFlags.GameMenu | CursorFlags.Dialogue | CursorFlags.Chat;

        // Discs and glyphs (1080p canvas units; option roots scale with the pop-out animation).
        private const float DiscRadius = 17f;
        private const float CenterRadius = 22f;
        private const float DiscOutline = 1.25f;
        private const float PathOutline = 2f;
        private const float IconHalf = 8f;
        private const float CenterIconHalf = 10f;
        private const float IconStroke = 1.5f;
        private const int DiscSegments = 18;
        private const float LeaderWidth = 1.25f;
        private const float DimAlpha = 0.35f;

        // Data tag: label + state line beside the disc, on a dark plate that starts under the disc.
        private const float LabelSize = 14f;
        private const float StatusSize = 11.5f;
        private const float CenterLabelSize = 12f;
        private const float TextGap = 7f;
        private const float LabelLift = 7f;
        private const float StatusDrop = 8f;
        private const float PlatePadding = 5f;
        private const float RailWidth = 2f;
        private const float PlateBaseAlpha = 0.72f;
        private const float LabelDimAlpha = 0.55f;
        private const float CenterLabelDrop = 30f;
        private const float HintDrop = 250f;

        private const float SelectorSize = 46f;
        private const float SelectorArm = 10f;
        private const float PunchFrom = 1.25f;
        private const float PunchSeconds = 0.15f;

        private readonly List<NodeWidget> widgets = new List<NodeWidget>(WidgetCount);
        private readonly float[] prevX = new float[WidgetCount];
        private readonly float[] prevY = new float[WidgetCount];
        private readonly AceRadialMenuTree tree = new AceRadialMenuTree();
        private readonly int[] lastIndexAtLevel = new int[AceRadialMenuTree.MaxDepth + 2];

        private AutopilotSettings settings;
        private AvHudHost host;
        private AvVector vector;
        private AvVector plateVector;
        private AvVector selectorVector;
        private RectTransform selectorRoot;
        private TMP_Text hint;
        private float hintPlateW;
        private float hintPlateH;

        private bool isOpen;
        private bool holding;
        private bool releasePending;
        private bool tagsDirty;
        private float openedAt;
        private int prevCount = -1;
        private int prevHovered = -2;
        private int selectorHoveredIndex = -2;

        public bool IsOpen => isOpen;

        private struct NodeWidget
        {
            public RectTransform Root;
            public TMP_Text Label;
            public TMP_Text Status;
            public string LastLabel;
            public string LastStatus;
            public int Side;
            /// <summary>Unscaled text block extents (widest line, total height) for the plate.</summary>
            public float TextW;
            public float LabelH;
            public float StatusH;
        }

        public void Configure(AutopilotSettings autopilotSettings) => settings = autopilotSettings;

        public void ResetForScene()
        {
            CloseMenu(forceRelease: true);
            CockpitStateProbe.Reset();
        }

        private void Awake() => BuildCanvas();

        private void OnDisable() => CloseMenu(forceRelease: true);

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) CloseMenu(forceRelease: true);
        }

        private void OnDestroy()
        {
            CloseMenu(forceRelease: true);
            host?.Destroy();
        }

        private void Update()
        {
            TryReleaseInput();
            KeyCode key = settings?.AceRadialKey?.Value ?? KeyCode.C;
            if (key == KeyCode.None) return;

            if (!isOpen)
            {
                if (Input.GetKeyDown(key) && CanOpen()) OpenMenu();
                return;
            }

            if (!StillValid() || Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CloseMenu();
                return;
            }

            host.Refresh(1f);
            float unit = host.ScaleFactor;
            float now = Time.unscaledTime;
            Vector2 mouse = Input.mousePosition;
            Vector2 center = ScreenCenter();
            tree.Tick(new AceVec2(mouse.x, mouse.y), new AceVec2(center.x, center.y), unit, now);

            if (holding && !Input.GetKey(key))
            {
                holding = false;
                AceRadialAction action = tree.HoveredRunnable();
                if (action != null) { Run(action); return; }
                if (tree.Hovered != null || now - openedAt > TapSeconds) { CloseMenu(); return; }
            }
            else if (!holding && Input.GetKeyDown(key))
            {
                CloseMenu();
                return;
            }

            if (Input.GetMouseButtonDown(0) && !tree.ExpandHovered(now))
            {
                AceRadialAction action = tree.HoveredRunnable();
                if (action != null) { Run(action); return; }
            }

            Render(unit);
        }

        private static Vector2 ScreenCenter() => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        private bool CanOpen()
        {
            if (GameplayUI.GameIsPaused || DynamicMap.mapMaximized) return false;
            if ((CursorManager.GetFlags() & ForeignCursorOwners) != 0) return false;
            if (NativeWheelOpen() || Typing()) return false;
            return GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null;
        }

        private static bool StillValid() =>
            Application.isFocused && !GameplayUI.GameIsPaused && !DynamicMap.mapMaximized &&
            GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null;

        private static bool NativeWheelOpen()
        {
            try { return RadialMenuMain.IsInUse(); }
            catch (Exception) { return false; }
        }

        private static bool Typing()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        private void OpenMenu()
        {
            releasePending = false;
            AceRadialInputGuard.Acquire();
            openedAt = Time.unscaledTime;
            host.Refresh(1f);
            float unit = host.ScaleFactor;
            Vector2 center = ScreenCenter();
            AceVec2 centerVec = new AceVec2(center.x, center.y);
            tree.Open(AceRadialCatalog.Build(), openedAt);
            tree.Tick(centerVec, centerVec, unit, openedAt);
            holding = true;
            isOpen = true;
            prevCount = -1;
            prevHovered = -2;
            selectorHoveredIndex = -2;
            host.SetVisible(true);
            Render(unit);
        }

        private void Run(AceRadialAction action)
        {
            CloseMenu();
            try { action.Run(); }
            catch (Exception e) { Plugin.Logger?.LogWarning("Autopilot: menu action '" + action.Id + "' failed: " + e.Message); }
        }

        private void CloseMenu(bool forceRelease = false)
        {
            if (isOpen)
            {
                isOpen = false;
                holding = false;
                tree.Close();
                host.SetVisible(false);
                releasePending = true;
            }
            if (forceRelease)
            {
                releasePending = false;
                AceRadialInputGuard.Release();
                return;
            }
            TryReleaseInput();
        }

        /// <summary>A click that closed the menu must not reach the guns: the mouse goes back
        /// to Rewired only once both buttons are up.</summary>
        private void TryReleaseInput()
        {
            if (!releasePending || Input.GetMouseButton(0) || Input.GetMouseButton(1)) return;
            releasePending = false;
            AceRadialInputGuard.Release();
        }

        // ------------------------------------------------------------------ render

        private void Render(float unit)
        {
            float textScale = 1f / Mathf.Clamp(unit, .1f, 1f);
            float hintSize = 11f * textScale;
            bool hintSizeChanged = !Mathf.Approximately(hint.fontSize, hintSize);
            if (hintSizeChanged) hint.fontSize = hintSize;
            IReadOnlyList<AceRadialNodeLayout> layout = tree.Layout;
            int hovered = tree.HoveredIndex;
            int count = Mathf.Min(layout.Count, widgets.Count);
            float centerX = count > 0 ? layout[0].Position.X / unit : 0f;
            float centerY = count > 0 ? layout[0].Position.Y / unit : 0f;

            string hintText = !tree.HasOptions ? "NO ACTIONS AVAILABLE"
                : holding ? "RELEASE TO SELECT  ·  RMB CANCEL"
                : "CLICK TO SELECT  ·  RMB CLOSE";
            bool hintChanged = hintText != hint.text;
            if (hintChanged || hintSizeChanged)
            {
                hint.SetText(hintText);
                Vector2 hintPref = hint.GetPreferredValues(hintText, 0f, 0f);
                hintPlateW = hintPref.x + (PlatePadding * 2f);
                hintPlateH = hintPref.y + (PlatePadding * 2f);
            }

            tagsDirty = false;
            for (int i = 0; i < count; i++) PaintTag(i, layout[i], i == hovered && i > 0, centerX, unit);
            for (int i = count; i < widgets.Count; i++)
                if (widgets[i].Root.gameObject.activeSelf) widgets[i].Root.gameObject.SetActive(false);

            bool geometryDirty = tagsDirty || count != prevCount || hovered != prevHovered || hintChanged || hintSizeChanged;
            for (int i = 0; i < count && !geometryDirty; i++)
            {
                AceVec2 p = layout[i].Position;
                if (!Mathf.Approximately(p.X, prevX[i]) || !Mathf.Approximately(p.Y, prevY[i])) geometryDirty = true;
            }

            if (geometryDirty)
            {
                PaintGeometry(layout, count, hovered, centerX, centerY, unit);
                for (int i = 0; i < count; i++) { prevX[i] = layout[i].Position.X; prevY[i] = layout[i].Position.Y; }
                prevCount = count;
                prevHovered = hovered;
            }

            bool showSelector = hovered > 0 && hovered < count;
            selectorRoot.gameObject.SetActive(showSelector);
            if (showSelector)
            {
                AceVec2 at = layout[hovered].Position;
                selectorRoot.anchoredPosition = new Vector2(at.X / unit, at.Y / unit);
                if (hovered != selectorHoveredIndex) host.Reveal.Punch(selectorRoot, PunchFrom, PunchSeconds);
            }
            selectorHoveredIndex = showSelector ? hovered : -2;

            hint.rectTransform.anchoredPosition = new Vector2(centerX, centerY - HintDrop);
        }

        /// <summary>Label + state line beside the disc (or under the centre node).</summary>
        private void PaintTag(int index, AceRadialNodeLayout node, bool hovered, float centerX, float unit)
        {
            NodeWidget widget = widgets[index];
            if (!widget.Root.gameObject.activeSelf) widget.Root.gameObject.SetActive(true);
            float px = node.Position.X / unit;
            widget.Root.anchoredPosition = new Vector2(px, node.Position.Y / unit);
            widget.Root.localScale = Vector3.one * node.Scale;

            bool centre = node.Level == 0;
            AceRadialNode data = node.Node;
            int side = centre ? 0 : px >= centerX - 1f ? 1 : -1;

            string label = (data.Action.Label ?? string.Empty).ToUpperInvariant()
                + (!centre && data.IsBranch ? "  ›" : string.Empty);
            string status = centre ? string.Empty : data.Status.Text ?? string.Empty;

            bool changed = false;
            // Keep text readable when the reference canvas scales down at 720p.
            // Only owned labels change; discs and navigation retain their existing layout.
            float textScale = 1f / Mathf.Clamp(unit, .1f, 1f);
            float labelSize = (centre ? CenterLabelSize : LabelSize) * textScale;
            float statusSize = StatusSize * textScale;
            if (!Mathf.Approximately(widget.Label.fontSize, labelSize) ||
                !Mathf.Approximately(widget.Status.fontSize, statusSize))
            {
                widget.Label.fontSize = labelSize;
                widget.Status.fontSize = statusSize;
                changed = true;
            }
            if (label != widget.LastLabel)
            {
                widget.Label.SetText(label);
                widget.LastLabel = label;
                changed = true;
            }
            if (status != widget.LastStatus)
            {
                widget.Status.SetText(status);
                widget.LastStatus = status;
                changed = true;
            }
            FontStyles weight = hovered || (node.OnPath && !centre) ? FontStyles.Bold : FontStyles.Normal;
            if (widget.Label.fontStyle != weight)
            {
                widget.Label.fontStyle = weight;
                changed = true;
            }
            if (changed || side != widget.Side)
            {
                widget.Side = side;
                LayoutTag(ref widget, centre);
                widgets[index] = widget;
                tagsDirty = true;
            }

            bool live = centre || node.OnPath || node.CurrentLevel;
            float alpha = live || hovered ? 1f : LabelDimAlpha;
            widget.Label.color = (data.Enabled ? AvTheme.TextPrimary : AvTheme.Disabled).WithAlpha(alpha);
            widget.Status.color = (data.Enabled ? ToneColor(data.Status.Tone) : AvTheme.Disabled).WithAlpha(alpha);
        }

        /// <summary>Sizes and anchors the two text boxes to their own glyphs on the chosen side.</summary>
        private static void LayoutTag(ref NodeWidget widget, bool centre)
        {
            Vector2 labelPref = string.IsNullOrEmpty(widget.LastLabel) ? Vector2.zero : widget.Label.GetPreferredValues(widget.LastLabel, 0f, 0f);
            Vector2 statusPref = string.IsNullOrEmpty(widget.LastStatus) ? Vector2.zero : widget.Status.GetPreferredValues(widget.LastStatus, 0f, 0f);
            // Measure the displayed weight; bold hover/breadcrumb labels must fit both
            // their text rect and the plate. Leave a pixel each side for SDF rounding.
            if (labelPref != Vector2.zero) labelPref += new Vector2(2f, 2f);
            if (statusPref != Vector2.zero) statusPref += new Vector2(2f, 2f);
            widget.TextW = Mathf.Max(labelPref.x, statusPref.x);
            widget.LabelH = labelPref.y;
            widget.StatusH = statusPref.y;

            RectTransform label = widget.Label.rectTransform;
            RectTransform status = widget.Status.rectTransform;
            label.sizeDelta = labelPref;
            status.sizeDelta = statusPref;

            if (centre)
            {
                label.pivot = new Vector2(0.5f, 1f);
                widget.Label.alignment = TextAlignmentOptions.Top;
                label.anchoredPosition = new Vector2(0f, -CenterLabelDrop);
                status.gameObject.SetActive(false);
                return;
            }

            bool right = widget.Side >= 0;
            float x = (right ? 1f : -1f) * (DiscRadius + TextGap);
            bool hasStatus = statusPref.y > 0f;
            label.pivot = new Vector2(right ? 0f : 1f, 0.5f);
            status.pivot = label.pivot;
            widget.Label.alignment = right ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
            widget.Status.alignment = widget.Label.alignment;
            label.anchoredPosition = new Vector2(x, hasStatus ? LabelLift : 0f);
            status.anchoredPosition = new Vector2(x, -StatusDrop);
            if (status.gameObject.activeSelf != hasStatus) status.gameObject.SetActive(hasStatus);
        }

        private static Color ToneColor(AceTone tone)
        {
            switch (tone)
            {
                case AceTone.Active: return AvTheme.Accent;
                case AceTone.Caution: return AvTheme.Warning;
                case AceTone.Danger: return AvTheme.Alert;
                default: return AvTheme.Dim;
            }
        }

        /// <summary>
        /// Rebuilds both vector surfaces: tag plates (under everything), then leaders trimmed
        /// to the disc edges, then each disc with its glyph, then the centre node.
        /// </summary>
        private void PaintGeometry(IReadOnlyList<AceRadialNodeLayout> layout, int count, int hovered,
            float centerX, float centerY, float unit)
        {
            AvQuadBuffer buffer = vector.Buffer;
            buffer.Clear();
            AvQuadBuffer plates = plateVector.Buffer;
            plates.Clear();
            for (int i = 0; i < lastIndexAtLevel.Length; i++) lastIndexAtLevel[i] = -1;

            Rgba info = AvTheme.RailInfo.ToRgba();
            Rgba selected = AvTheme.Selected.ToRgba();

            for (int i = 0; i < count; i++)
            {
                AceRadialNodeLayout node = layout[i];
                int level = node.Level;
                if (level < lastIndexAtLevel.Length) lastIndexAtLevel[level] = i;

                float px = node.Position.X / unit;
                float py = node.Position.Y / unit;
                float k = node.Scale;
                float radius = (level == 0 ? CenterRadius : DiscRadius) * k;

                if (level > 0 && level - 1 < lastIndexAtLevel.Length && lastIndexAtLevel[level - 1] >= 0)
                {
                    AceRadialNodeLayout parent = layout[lastIndexAtLevel[level - 1]];
                    float parentRadius = (parent.Level == 0 ? CenterRadius : DiscRadius) * parent.Scale;
                    float alpha = node.OnPath || node.CurrentLevel ? 0.85f : DimAlpha;
                    TrimmedLeader(buffer, parent.Position.X / unit, parent.Position.Y / unit, parentRadius,
                        px, py, radius, info.WithAlpha(alpha));
                }

                if (level == 0) continue;

                AceRadialNode data = node.Node;
                bool live = node.OnPath || node.CurrentLevel;
                bool isHovered = i == hovered;
                float tagAlpha = live || isHovered ? 1f : LabelDimAlpha;

                PaintPlate(plates, widgets[i], px, py, k, tagAlpha, isHovered, data);

                Rgba ink = !data.Enabled ? AvTheme.Disabled.ToRgba()
                    : data.IsBranch ? info : AvTheme.TextPrimary.ToRgba();
                float discAlpha = live || isHovered ? 1f : DimAlpha + 0.2f;

                Rgba fill = isHovered && data.Enabled ? selected.WithAlpha(0.92f)
                    : AvTokens.HudPanel.WithAlpha(0.9f * discAlpha);
                Disc(buffer, px, py, radius, fill);

                if (!data.Enabled)
                    AvStrokes.DashedRing(buffer, px, py, radius, 10, 0.55f, DiscOutline, ink.WithAlpha(discAlpha));
                else
                    AvStrokes.Ring(buffer, px, py, radius, DiscSegments,
                        node.OnPath ? PathOutline : DiscOutline,
                        (isHovered ? selected : node.OnPath ? info : ink.WithAlpha(0.8f)).WithAlpha(discAlpha));

                Rgba glyph = isHovered && data.Enabled ? AvTokens.TextInk : ink.WithAlpha(discAlpha);
                AceRadialIcons.Draw(buffer, data.Action.Icon, px, py, IconHalf * k, IconStroke, glyph);
            }

            // Centre node: the aircraft itself.
            Disc(buffer, centerX, centerY, CenterRadius, AvTokens.HudPanel.WithAlpha(0.92f));
            AvStrokes.Ring(buffer, centerX, centerY, CenterRadius, 24, PathOutline, info);
            AvStrokes.Ring(buffer, centerX, centerY, CenterRadius + 5f, 24, 0.75f, info.WithAlpha(0.35f));
            AceRadialIcons.Draw(buffer, AceIcon.Aircraft, centerX, centerY, CenterIconHalf, IconStroke, info);

            if (count > 0) PaintCentrePlate(plates, widgets[0], centerX, centerY);

            if (hintPlateW > 0f && hintPlateH > 0f)
            {
                float hintX = centerX - (hintPlateW * 0.5f);
                float hintY = (centerY - HintDrop) - (hintPlateH * 0.5f);
                AvStrokes.Fill(plates, hintX, hintY, hintPlateW, hintPlateH, AvTokens.HudPanel.WithAlpha(PlateBaseAlpha));
                AvStrokes.Fill(plates, hintX, hintY, 1f, hintPlateH, info);
            }

            vector.Commit();
            plateVector.Commit();
        }

        /// <summary>
        /// Dark tag plate from under the disc out past the text, with a tone rail on its outer
        /// edge (the state line's colour) and a selection border while hovered.
        /// </summary>
        private static void PaintPlate(AvQuadBuffer plates, NodeWidget widget, float px, float py, float k,
            float alpha, bool hovered, AceRadialNode data)
        {
            if (widget.TextW <= 0f) return;
            bool hasStatus = widget.StatusH > 0f;
            float top = (hasStatus ? LabelLift + (widget.LabelH * 0.5f) : widget.LabelH * 0.5f) + PlatePadding;
            float bottom = (hasStatus ? -StatusDrop - (widget.StatusH * 0.5f) : -(widget.LabelH * 0.5f)) - PlatePadding;
            float outer = DiscRadius + TextGap + widget.TextW + PlatePadding;
            top = Mathf.Max(top, DiscRadius * 0.7f);
            bottom = Mathf.Min(bottom, -DiscRadius * 0.7f);

            float w = outer * k;
            float h = (top - bottom) * k;
            float y = py + (bottom * k);
            bool right = widget.Side >= 0;
            float x = right ? px : px - w;

            AvStrokes.Fill(plates, x, y, w, h, AvTokens.HudPanel.WithAlpha(PlateBaseAlpha * alpha));
            Rgba rail = (data.Enabled && !data.Status.IsEmpty ? ToneColor(data.Status.Tone) : AvTheme.RailInfo).ToRgba();
            float railX = right ? x + w - RailWidth : x;
            AvStrokes.Fill(plates, railX, y, RailWidth, h, rail.WithAlpha(alpha * (data.Status.IsEmpty ? 0.6f : 1f)));
            // Portal: hairline top/bottom edges carry the tag, and the hovered tag wears corner brackets.
            Rgba hair = AvTheme.RailInfo.ToRgba().WithAlpha(0.35f * alpha);
            AvStrokes.Fill(plates, x, y + h - 1f, w, 1f, hair);
            AvStrokes.Fill(plates, x, y, w, 1f, hair);
            if (hovered) AvStrokes.Bracket(plates, x, y, w, h, 5f, 1.25f, AvTheme.Selected.ToRgba());
        }

        private static void PaintCentrePlate(AvQuadBuffer plates, NodeWidget widget, float cx, float cy)
        {
            if (widget.TextW <= 0f) return;
            float w = widget.TextW + (PlatePadding * 2f);
            float h = widget.LabelH + PlatePadding;
            float x = cx - (w * 0.5f);
            float y = cy - CenterLabelDrop - widget.LabelH - (PlatePadding * 0.5f);
            AvStrokes.Fill(plates, x, y, w, h, AvTokens.HudPanel.WithAlpha(PlateBaseAlpha));
            AvStrokes.Fill(plates, x, y + h - 1f, w, 1f, AvTheme.RailInfo.ToRgba());
            AvStrokes.Fill(plates, x, y, w, 1f, AvTheme.RailInfo.ToRgba().WithAlpha(0.35f));
        }

        private static void Disc(AvQuadBuffer buffer, float x, float y, float radius, Rgba color) =>
            AvStrokes.Ring(buffer, x, y, radius * 0.5f, DiscSegments, radius, color);

        private static void TrimmedLeader(AvQuadBuffer buffer, float ax, float ay, float ar,
            float bx, float by, float br, Rgba color)
        {
            float dx = bx - ax, dy = by - ay;
            float len = Mathf.Sqrt((dx * dx) + (dy * dy));
            if (len <= ar + br + 1f) return;
            float ux = dx / len, uy = dy / len;
            AvStrokes.Line(buffer, ax + (ux * ar), ay + (uy * ar), bx - (ux * br), by - (uy * br), LeaderWidth, color);
        }

        // ------------------------------------------------------------------ build

        private void BuildCanvas()
        {
            host = AvHudHost.Create("Boscali / Interaction", transform, 4);
            host.SetVisible(false);

            vector = AvVector.Create(host.Motion, "Geometry", VectorCapacity);
            // Tag plates live in Static, which renders under Data (the TMP labels) and under
            // Motion (discs, glyphs, leaders), so a plate never paints over its own text.
            plateVector = AvVector.Create(host.Static, "Plates", PlateCapacity);

            selectorRoot = new GameObject("Selector", typeof(RectTransform)).GetComponent<RectTransform>();
            selectorRoot.SetParent(host.Motion, false);
            selectorRoot.anchorMin = Vector2.zero;
            selectorRoot.anchorMax = Vector2.zero;
            selectorRoot.pivot = new Vector2(0.5f, 0.5f);
            selectorRoot.sizeDelta = new Vector2(SelectorSize, SelectorSize);
            selectorVector = AvVector.Create(selectorRoot, "Bracket", SelectorCapacity);
            BuildSelectorBracket();
            selectorRoot.gameObject.SetActive(false);

            for (int i = 0; i < WidgetCount; i++) widgets.Add(BuildWidget(i));

            hint = HudLabel("Hint", host.Data, 11f, TextAlignmentOptions.Center);
            hint.color = AvTheme.Dim.WithAlpha(0.85f);
            hint.rectTransform.anchorMin = Vector2.zero;
            hint.rectTransform.anchorMax = Vector2.zero;
            hint.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            hint.rectTransform.sizeDelta = new Vector2(420f, 24f);
        }

        private void BuildSelectorBracket()
        {
            AvQuadBuffer buffer = selectorVector.Buffer;
            buffer.Clear();
            // selectorVector fills selectorRoot's own rect (AvVector.Create stretches to its
            // parent), so its local origin is that rect's bottom-left corner.
            AvStrokes.Bracket(buffer, 0f, 0f, SelectorSize, SelectorSize, SelectorArm, 1.5f, AvTheme.Selected.ToRgba());
            selectorVector.Commit();
        }

        private NodeWidget BuildWidget(int index)
        {
            var root = new GameObject("Option" + index, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(host.Data, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Vector2.zero;
            TMP_Text label = HudLabel("Label", root, index == 0 ? CenterLabelSize : LabelSize, TextAlignmentOptions.MidlineLeft);
            TMP_Text status = HudLabel("Status", root, StatusSize, TextAlignmentOptions.MidlineLeft);
            foreach (TMP_Text text in new[] { label, status })
            {
                RectTransform rt = text.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
            }
            root.gameObject.SetActive(false);
            return new NodeWidget { Root = root, Label = label, Status = status, Side = int.MinValue };
        }

        private static readonly Dictionary<TMP_FontAsset, Material> outlinedMaterials =
            new Dictionary<TMP_FontAsset, Material>(2);

        /// <summary>One shared outlined material per font (fonts outlive the scene; nobody owns or destroys it). Only opts in when the shader exposes an outline.</summary>
        private static Material OutlinedMaterial(TMP_FontAsset font)
        {
            if (outlinedMaterials.TryGetValue(font, out Material existing) && existing != null) return existing;
            Material source = font.material;
            if (source == null) return null;
            Material material = source;
            if (source.HasProperty("_OutlineWidth") && source.HasProperty("_OutlineColor"))
            {
                material = new Material(source);
                material.EnableKeyword("OUTLINE_ON");
                material.SetFloat("_OutlineWidth", 0.18f);
                material.SetColor("_OutlineColor", Color.black.WithAlpha(0.6f));
            }
            outlinedMaterials[font] = material;
            return material;
        }

        private static TMP_Text HudLabel(string name, Transform parent, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var label = go.GetComponent<TMP_Text>();
            label.transform.SetParent(parent, false);
            ((RectTransform)label.transform).sizeDelta = new Vector2(220f, 24f);

            TMP_FontAsset font = AvType.Face(AvFace.Cond);
            if (font != null)
            {
                label.font = font;
                Material outlined = OutlinedMaterial(font);
                if (outlined != null) label.fontSharedMaterial = outlined;
            }
            else
            {
                label.font = TMP_Settings.defaultFontAsset;
            }

            label.fontSize = size;
            label.color = AvTheme.TextPrimary;
            label.enableWordWrapping = false;
            label.richText = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = align;
            label.enableAutoSizing = false;
            label.isTextObjectScaleStatic = true;
            label.raycastTarget = false;
            return label;
        }
    }
}
