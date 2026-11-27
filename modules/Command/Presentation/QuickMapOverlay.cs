using System;
using System.Globalization;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// Tactical HUD overlay rendered inside the held Quick Map square (<c>MapHoldKey</c>):
    /// corner brackets, dashed concentric range rings centered on ownship, forward heading reference line,
    /// rotating North perimeter marker, and compact top/bottom telemetry strips (mode, ring scale, grid, zoom, HDG, RTB).
    /// Drawn in live HUD ink so it matches the cockpit visor theme.
    /// </summary>
    internal sealed class QuickMapOverlay
    {
        private const int VectorCapacity = 600;
        private const int MaxRings = 3;
        private const float Margin = 2f;
        private const float TelemetryInterval = 0.1f;

        private RectTransform root;
        private CanvasGroup group;
        private AvVector chromeVector;
        private AvVector ringsVector;
        private TMP_Text topLeft, topRight, bottomLeft, bottomRight, northLabel;
        private readonly TMP_Text[] ringLabels = new TMP_Text[MaxRings];

        private float lastSide = -1f, lastRingPx = -1f, lastTurnDeg = float.NaN, lastHdgDeg = float.NaN;
        private int lastRings = -1, lastInkArgb;
        private bool lastNorthUp, lastImperial;
        private float nextTelemetryUpdate, lastTelemetryScale;
        private string lastTopLeft = "", lastTopRight = "", lastBottomLeft = "", lastBottomRight = "";

        public float LastRingStepMetres { get; private set; }
        public string LastNearestBase { get; private set; } = "";
        public string LastGrid { get; private set; } = "";

        public bool Ensure(RectTransform mapRoot)
        {
            if (root != null)
            {
                if (root.parent != mapRoot) root.SetParent(mapRoot, false);
                return true;
            }
            if (mapRoot == null) return false;
            Destroy();

            if (!VanillaHudStyle.TryHudFont(out TMP_FontAsset font, out Material mat))
            {
                font = LeftHudStack.Font != null ? LeftHudStack.Font : TMP_Settings.defaultFontAsset;
                mat = font != null ? VanillaHudStyle.Glow(font.material) : null;
            }

            var go = new GameObject("BoscaliQuickMapOverlay", typeof(RectTransform), typeof(CanvasGroup));
            root = (RectTransform)go.transform;
            root.SetParent(mapRoot, false);
            Stretch(root);
            group = go.GetComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.interactable = group.blocksRaycasts = false;
            group.alpha = 0f;

            var maskGo = new GameObject("RingsMask", typeof(RectTransform), typeof(RectMask2D));
            var maskRect = (RectTransform)maskGo.transform;
            maskRect.SetParent(root, false);
            Stretch(maskRect);
            // Zero-size centered anchors: AvVector coordinates have (0, 0) at the parent's lower-left, so centering
            // these holders aligns AvStrokes 1:1 with the map square center (-half..+half) and TMP labels.
            RectTransform ringsCenter = CenterOrigin(maskRect, "RingsCenter");
            ringsVector = AvVector.Create(ringsCenter, "Rings", VectorCapacity, HudGlow.Stroke, HudGlow.Alpha);

            RectTransform chromeCenter = CenterOrigin(root, "ChromeCenter");
            chromeVector = AvVector.Create(chromeCenter, "Chrome", 240, HudGlow.Stroke, HudGlow.Alpha);

            topLeft = MakeText(root, "TopLeft", font, mat, TextAlignmentOptions.TopLeft, HudType.Label, bold: true);
            PlaceEdgeLabel(topLeft.rectTransform, 0f, 1f, new Vector2(0f, 1f), new Vector2(24f, -6f), new Vector2(360f, 18f));

            topRight = MakeText(root, "TopRight", font, mat, TextAlignmentOptions.TopRight, HudType.Label, bold: true);
            PlaceEdgeLabel(topRight.rectTransform, 1f, 1f, new Vector2(1f, 1f), new Vector2(-24f, -6f), new Vector2(220f, 18f));

            bottomLeft = MakeText(root, "BottomLeft", font, mat, TextAlignmentOptions.BottomLeft, HudType.Label, bold: true);
            PlaceEdgeLabel(bottomLeft.rectTransform, 0f, 0f, new Vector2(0f, 0f), new Vector2(24f, 6f), new Vector2(420f, 18f));

            bottomRight = MakeText(root, "BottomRight", font, mat, TextAlignmentOptions.BottomRight, HudType.Label, bold: false);
            PlaceEdgeLabel(bottomRight.rectTransform, 1f, 0f, new Vector2(1f, 0f), new Vector2(-24f, 6f), new Vector2(220f, 18f));

            northLabel = MakeText(root, "North", font, mat, TextAlignmentOptions.Center, HudType.Body, bold: true);
            Anchor(northLabel.rectTransform, 0.5f, 0.5f, Vector2.zero, new Vector2(24f, 20f));
            northLabel.text = "N";

            for (int i = 0; i < MaxRings; i++)
            {
                ringLabels[i] = MakeText(root, "Ring" + i, font, mat, TextAlignmentOptions.BottomLeft, HudType.Label, bold: false);
                Anchor(ringLabels[i].rectTransform, 0.5f, 0.5f, Vector2.zero, new Vector2(72f, 18f));
                ringLabels[i].gameObject.SetActive(false);
            }

            go.SetActive(false);
            return true;
        }

        public void Present(RectTransform mapRoot, float alpha, float sidePx, float appliedScale, float displayFactor,
            bool northUp, bool imperial, float headingDeg, string grid, string nearestBase, float baseDistM, int baseBearingDeg)
        {
            if (alpha <= 0.01f) { Hide(); return; }
            if (!Ensure(mapRoot)) return;
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            if (root.GetSiblingIndex() != mapRoot.childCount - 1) root.SetAsLastSibling();
            group.alpha = Mathf.Clamp01(alpha);

            Color ink = VanillaHudStyle.Ink;
            Color inkHi = VanillaHudStyle.InkHi;
            Color inkDim = VanillaHudStyle.InkDim;

            float ringStepM = QuickMapMath.RingStepMetres(sidePx, displayFactor, appliedScale, northUp, imperial, out int ringCount, out float pxPerRing);
            grid = grid ?? "";
            nearestBase = nearestBase ?? "";
            bool ringLabelsDirty = ringStepM != LastRingStepMetres || imperial != lastImperial;
            bool telemetryDirty = ringLabelsDirty || northUp != lastNorthUp || appliedScale != lastTelemetryScale
                || grid != LastGrid || nearestBase != LastNearestBase;
            LastRingStepMetres = ringStepM;
            LastGrid = grid;
            LastNearestBase = nearestBase;

            Color32 ink32 = ink;
            int inkArgb = (ink32.a << 24) | (ink32.r << 16) | (ink32.g << 8) | ink32.b;

            float turnDeg = northUp ? 0f : headingDeg;
            bool meshDirty = Mathf.Abs(sidePx - lastSide) > 0.5f
                || Mathf.Abs(pxPerRing - lastRingPx) > 0.5f
                || ringCount != lastRings
                || northUp != lastNorthUp
                || float.IsNaN(lastTurnDeg) || Mathf.Abs(Mathf.DeltaAngle(turnDeg, lastTurnDeg)) > 0.25f
                || (northUp && (float.IsNaN(lastHdgDeg) || Mathf.Abs(Mathf.DeltaAngle(headingDeg, lastHdgDeg)) > 0.4f))
                || inkArgb != lastInkArgb;

            if (meshDirty)
            {
                lastSide = sidePx;
                lastRingPx = pxPerRing;
                lastRings = ringCount;
                lastNorthUp = northUp;
                lastTurnDeg = turnDeg;
                lastHdgDeg = headingDeg;
                lastInkArgb = inkArgb;
                PaintVectors(sidePx, pxPerRing, ringCount, northUp, turnDeg, headingDeg, ink, inkHi);
            }
            if (meshDirty || ringLabelsDirty)
            {
                lastImperial = imperial;
                PlaceLabels(sidePx, pxPerRing, ringCount, northUp, turnDeg, ringStepM, imperial, inkHi, ink);
            }

            float now = Time.unscaledTime;
            if (telemetryDirty || now >= nextTelemetryUpdate)
            {
                nextTelemetryUpdate = now + TelemetryInterval;
                lastTelemetryScale = appliedScale;
                UpdateTelemetry(northUp, ringStepM, imperial, appliedScale, headingDeg, grid, nearestBase, baseDistM, baseBearingDeg, ink, inkHi, inkDim);
            }
        }

        private void PaintVectors(float sidePx, float pxPerRing, int ringCount, bool northUp, float turnDeg, float headingDeg, Color ink, Color inkHi)
        {
            float half = sidePx * 0.5f - Margin;
            float ownY = northUp ? 0f : -sidePx * QuickMapMath.DecenterShare;
            Rgba baseInk = ink.ToRgba();
            Rgba hi = inkHi.ToRgba();
            Rgba low = baseInk.WithAlpha(HudInk.LineLow);
            Rgba mid = baseInk.WithAlpha(HudInk.LineMid);
            Rgba full = baseInk.WithAlpha(HudInk.LineFull);

            AvQuadBuffer cb = chromeVector.Buffer;
            cb.Clear();
            // Frame: perimeter hairline + corner L-brackets.
            float x0 = -half, y0 = -half, w = half * 2f, h = half * 2f;
            AvStrokes.Line(cb, x0, y0, x0 + w, y0, HudStroke.Hairline, low);
            AvStrokes.Line(cb, x0, y0 + h, x0 + w, y0 + h, HudStroke.Hairline, low);
            AvStrokes.Line(cb, x0, y0, x0, y0 + h, HudStroke.Hairline, low);
            AvStrokes.Line(cb, x0 + w, y0, x0 + w, y0 + h, HudStroke.Hairline, low);
            AvStrokes.Bracket(cb, x0, y0, w, h, 18f, HudStroke.Standard, full);

            QuickMapMath.NorthEdgePoint(half, turnDeg, 36f, out float ex, out float ey, out float inX, out float inY, out float lblX, out float lblY);
            AvStrokes.Line(cb, ex, ey, inX, inY, HudStroke.Standard, hi);
            northLabel.rectTransform.anchoredPosition = new Vector2(lblX, lblY);

            AvQuadBuffer rb = ringsVector.Buffer;
            rb.Clear();
            // Ownship reticle ring + forward course vector inside masked ring buffer.
            AvStrokes.Ring(rb, 0f, ownY, 10f, 20, HudStroke.Standard, mid);
            float fwdRad = (northUp ? headingDeg : 0f) * Mathf.Deg2Rad;
            float fx = Mathf.Sin(fwdRad), fy = Mathf.Cos(fwdRad);
            AvStrokes.Line(rb, fx * 12f, ownY + fy * 12f, fx * sidePx, ownY + fy * sidePx, HudStroke.Hairline, low);

            for (int i = 1; i <= ringCount; i++)
            {
                float r = pxPerRing * i;
                int segs = Mathf.Clamp((int)(r * 0.25f), 28, 72);
                AvStrokes.DashedRing(rb, 0f, ownY, r, segs, 0.52f, HudStroke.Hairline, low);
            }

            chromeVector.Commit();
            ringsVector.Commit();
        }

        private void PlaceLabels(float sidePx, float pxPerRing, int ringCount, bool northUp, float turnDeg, float ringStepM, bool imperial, Color inkHi, Color inkDim)
        {
            float half = sidePx * 0.5f - Margin;
            float ownY = northUp ? 0f : -sidePx * QuickMapMath.DecenterShare;
            northLabel.color = inkHi;

            // Put range numeral along 45 deg upper-right of each ring when inside the frame with safe margin.
            const float cos45 = 0.70710678f;
            float stepUnit = imperial ? ringStepM / 1852f : ringStepM / 1000f;
            string suffix = imperial ? " NM" : " KM";
            for (int i = 0; i < MaxRings; i++)
            {
                TMP_Text lbl = ringLabels[i];
                if (i >= ringCount || pxPerRing <= 0f)
                {
                    if (lbl.gameObject.activeSelf) lbl.gameObject.SetActive(false);
                    continue;
                }
                float r = pxPerRing * (i + 1);
                float lx = r * cos45 + 3f;
                float ly = ownY + r * cos45 + 2f;
                bool inside = lx > -half + 32f && lx < half - 54f && ly > -half + 26f && ly < half - 28f;
                if (!inside)
                {
                    if (lbl.gameObject.activeSelf) lbl.gameObject.SetActive(false);
                    continue;
                }
                if (!lbl.gameObject.activeSelf) lbl.gameObject.SetActive(true);
                lbl.rectTransform.anchoredPosition = new Vector2(lx, ly);
                lbl.color = inkDim;
                float val = stepUnit * (i + 1);
                string text = (Mathf.Round(val)).ToString(CultureInfo.InvariantCulture) + (i == ringCount - 1 ? suffix : "");
                if (lbl.text != text) lbl.text = text;
            }
        }

        private void UpdateTelemetry(bool northUp, float ringStepM, bool imperial, float appliedScale, float headingDeg,
            string grid, string nearestBase, float baseDistM, int baseBearingDeg, Color ink, Color inkHi, Color inkDim)
        {
            string mode = northUp ? "NORTH UP" : "HDG UP";
            string stepText = QuickMapMath.FormatRingStep(ringStepM, imperial);
            string tl = string.IsNullOrEmpty(stepText)
                ? "QUICK MAP  ·  " + mode
                : "QUICK MAP  ·  " + mode + "  ·  " + stepText + "/RING";
            if (!string.Equals(tl, lastTopLeft, StringComparison.Ordinal)) { lastTopLeft = tl; topLeft.text = tl; }
            topLeft.color = inkHi;

            string zoomText = QuickMapMath.FormatZoom(appliedScale);
            string tr = string.IsNullOrEmpty(grid) ? zoomText : grid + "  ·  " + zoomText;
            if (!string.Equals(tr, lastTopRight, StringComparison.Ordinal)) { lastTopRight = tr; topRight.text = tr; }
            topRight.color = ink;

            int hdg = (((Mathf.RoundToInt(headingDeg) % 360) + 360) % 360);
            string hdgStr = "HDG " + hdg.ToString("000", CultureInfo.InvariantCulture) + "°";
            string bl = string.IsNullOrEmpty(nearestBase)
                ? hdgStr
                : hdgStr + "  ·  RTB " + nearestBase.ToUpperInvariant() + " " + QuickMapMath.FormatRange(baseDistM, imperial) + " " + baseBearingDeg.ToString("000", CultureInfo.InvariantCulture) + "°";
            if (!string.Equals(bl, lastBottomLeft, StringComparison.Ordinal)) { lastBottomLeft = bl; bottomLeft.text = bl; }
            bottomLeft.color = ink;

            const string br = "± ZOOM  ·  N NORTH";
            if (!string.Equals(br, lastBottomRight, StringComparison.Ordinal)) { lastBottomRight = br; bottomRight.text = br; }
            bottomRight.color = inkDim;
        }

        public void Hide()
        {
            nextTelemetryUpdate = 0f;
            if (group != null) group.alpha = 0f;
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            group = null;
            chromeVector = null;
            ringsVector = null;
            topLeft = topRight = bottomLeft = bottomRight = northLabel = null;
            for (int i = 0; i < ringLabels.Length; i++) ringLabels[i] = null;
            lastSide = lastRingPx = -1f;
            lastTurnDeg = lastHdgDeg = float.NaN;
            lastRings = -1;
            lastInkArgb = 0;
            nextTelemetryUpdate = lastTelemetryScale = 0f;
            lastTopLeft = lastTopRight = lastBottomLeft = lastBottomRight = "";
            LastRingStepMetres = 0f;
            LastNearestBase = "";
            LastGrid = "";
        }

        private static TMP_Text MakeText(Transform parent, string name, TMP_FontAsset font, Material mat, TextAlignmentOptions align, float size, bool bold)
        {
            TextMeshProUGUI t = HudText.Make(parent, name, font, mat, align, size);
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.characterSpacing = HudType.Tracking(HudType.StepOf(size));
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static RectTransform CenterOrigin(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        private static void PlaceEdgeLabel(RectTransform rt, float ax, float ay, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Anchor(RectTransform rt, float ax, float ay, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(ax, ay);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
