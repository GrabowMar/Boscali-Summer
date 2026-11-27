using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Compact on-map tactical HUD chrome inside the map viewport:
    /// a top-right view pill (rotating north compass, TOP / 3D perspective toggle, FIT, zoom readout)
    /// and a bottom-right metric scale bar with live cursor fix / elevation / bearing readout.
    /// The top-right pill registers with <see cref="MapUiPointer"/> so its buttons never pass clicks
    /// to the map; the bottom-right scale bar is non-interactive (<c>raycastTarget = false</c>).
    /// </summary>
    internal static class ReliefMapHud
    {
        private const float PillW = 234f, PillH = 28f;
        private const float ScaleW = 360f, ScaleH = 42f;
        private static readonly float[] NiceKm = { .5f, 1f, 2f, 5f, 10f, 20f, 25f, 50f, 100f };

        private static RectTransform pillRoot, scaleRoot;
        private static AvFrame pillFrame;
        private static AvControl northBtn, perspBtn, fitBtn;
        private static TMP_Text needle, zoomLabel;
        private static AvVector scaleVector;
        private static TMP_Text scaleLabel, cursorLabel;
        private static float lastBarPx = -1f, nextRead;
        private static string lastScaleText = "", lastCursorText = "";
        private static int lastTheme = -1, lastRev = -1;
        private static bool lastDrawing, lastTopDown;

        internal static void Tick(DynamicMap map)
        {
            if (map == null || !DynamicMap.mapMaximized || map.mapBackground == null)
            {
                Hide();
                return;
            }
            RectTransform viewport = map.mapBackground.rectTransform;
            RectTransform chrome = MapSelectionDeck.ChromeLayer(viewport);
            if (chrome == null) { Hide(); return; }
            if (pillRoot == null || pillRoot.parent != chrome || scaleRoot == null || scaleRoot.parent != chrome)
                Build(chrome);

            bool showPill = MfdMapControls.Shown;
            if (pillRoot.gameObject.activeSelf != showPill)
            {
                pillRoot.gameObject.SetActive(showPill);
                if (showPill) MapUiPointer.Register(pillRoot);
                else MapUiPointer.Unregister(pillRoot);
            }
            if (!scaleRoot.gameObject.activeSelf) scaleRoot.gameObject.SetActive(true);

            bool restyle = AvStyleHost.FuiGeneration != lastTheme;
            if (restyle)
            {
                lastTheme = AvStyleHost.FuiGeneration;
                Restyle();
            }

            bool drawing = MfdTerrainRelief.IsDrawing;
            int rev = drawing ? MfdTerrainRelief.ViewRevision : -1;
            bool topDown = ReliefNavigator.IsTopDown;
            if (showPill && (restyle || rev != lastRev || drawing != lastDrawing || topDown != lastTopDown))
            {
                lastRev = rev;
                lastDrawing = drawing;
                lastTopDown = topDown;
                RefreshPill(map, drawing, topDown);
            }

            if (Time.unscaledTime >= nextRead)
            {
                nextRead = Time.unscaledTime + .08f;
                RefreshScaleAndReadout(map, drawing);
            }
        }

        private static void RefreshPill(DynamicMap map, bool drawing, bool topDown)
        {
            float yaw = drawing ? MfdTerrainRelief.Yaw : 0f;
            if (needle != null) needle.rectTransform.localEulerAngles = new Vector3(0f, 0f, yaw);
            int deg = Mathf.RoundToInt(yaw);
            northBtn.Label = deg == 0 ? "N" : (deg > 0 ? "+" : "-") + AvNum.Fixed(Mathf.Abs(deg), 0).PadLeft(3, '0') + "°";
            northBtn.Interactable = drawing;

            perspBtn.Interactable = drawing;
            perspBtn.Latched = drawing && !topDown;
            perspBtn.Label = !drawing ? "2D" : topDown ? "TOP" : "3D " + MfdChromeLay.TwoDigits(Mathf.RoundToInt(MfdTerrainRelief.Pitch)) + "°";

            float zoom = drawing ? MfdTerrainRelief.Zoom : map.GetZoomLevel();
            zoomLabel.text = "×" + AvNum.Fixed(zoom, 1);
        }

        private static void RefreshScaleAndReadout(DynamicMap map, bool drawing)
        {
            float mpp = drawing ? MfdTerrainRelief.MetresPerViewportPixel()
                : map.mapImage != null && map.mapDisplayFactor > 0f
                    ? 1f / Mathf.Max(1e-5f, map.mapDisplayFactor * Mathf.Abs(map.mapImage.transform.lossyScale.x))
                    : 0f;
            // Convert canvas-space lossyScale ratio so bar pixels match viewport local units.
            float canvasScale = scaleRoot.lossyScale.x > 0f && map.mapBackground.rectTransform.lossyScale.x > 0f
                ? scaleRoot.lossyScale.x / map.mapBackground.rectTransform.lossyScale.x : 1f;
            float mPerUnit = mpp * canvasScale;

            if (mPerUnit > 0f)
            {
                float kmPerUnit = mPerUnit / 1000f;
                float km = NiceKm[0];
                for (int i = 0; i < NiceKm.Length; i++)
                    if (NiceKm[i] / kmPerUnit <= 116f) km = NiceKm[i];
                float barPx = Mathf.Clamp(km / kmPerUnit, 24f, 120f);
                string text = km < 1f ? AvNum.Fixed(km * 1000f, 0) + " M" : AvNum.Fixed(km, 0) + " KM";
                if (Mathf.Abs(barPx - lastBarPx) > .4f || text != lastScaleText)
                {
                    lastBarPx = barPx;
                    lastScaleText = text;
                    scaleLabel.text = text;
                    RedrawScaleBar(barPx);
                }
            }
            else if (lastScaleText != "")
            {
                lastScaleText = "";
                scaleLabel.text = "";
                scaleVector.Buffer.Clear();
                scaleVector.Commit();
            }

            string readout = BuildCursorReadout(map, drawing);
            if (readout != lastCursorText)
            {
                lastCursorText = readout;
                cursorLabel.text = readout;
            }
        }

        private static string BuildCursorReadout(DynamicMap map, bool drawing)
        {
            if (MapUiPointer.OverControls()) return "";
            GlobalPosition pos;
            float elev = 0f;
            bool hasElev = false;
            if (drawing)
            {
                if (!MfdTerrainRelief.TryInspectCursor(out pos, out elev)) return "";
                hasElev = true;
            }
            else if (!map.TryGetCursorCoordinates(out pos))
            {
                return "";
            }

            string fix = "X " + AvNum.Signed(pos.x / 1000f, 1) + "  Z " + AvNum.Signed(pos.z / 1000f, 1) + " KM";
            string alt = hasElev ? "  ·  " + AvNum.Fixed(elev, 0) + " M" : "";
            string leg = MfdMapInteractions.FromMark(pos);
            if (leg != null) return fix + alt + "  ·  MARK " + leg;
            if (GameManager.GetLocalAircraft(out Aircraft own) && own != null && !own.disabled)
                return fix + alt + "  ·  " + MfdMapInteractions.LegText(own.GlobalPosition(), pos);
            return fix + alt;
        }

        private static void RedrawScaleBar(float barPx)
        {
            AvQuadBuffer b = scaleVector.Buffer;
            b.Clear();
            Color ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            Color back = AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.82f);
            float right = ScaleW - 6f, left = right - barPx, y = 7f;
            Rgba inkR = ink.WithAlpha(.92f).ToRgba();
            Rgba backR = back.ToRgba();
            AvStrokes.Line(b, left - 2f, y, right + 2f, y, 4.5f, backR);
            AvStrokes.Line(b, left, y, right, y, 1.8f, inkR);
            AvStrokes.Line(b, left, y - 1f, left, y + 6f, 1.6f, inkR);
            AvStrokes.Line(b, (left + right) * .5f, y - 1f, (left + right) * .5f, y + 4f, 1.2f, inkR);
            AvStrokes.Line(b, right, y - 1f, right, y + 6f, 1.6f, inkR);
            scaleVector.Commit();
        }

        private static void FitView()
        {
            if (MfdTerrainRelief.IsDrawing) { ReliefNavigator.Fit(); return; }
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;
            map.SetZoomLevel(1f);
            MapFocus.To(0f, 0f, false);
        }

        private static void Build(RectTransform chrome)
        {
            Reset();

            // Top-right view pill
            pillRoot = AvLay.Child(chrome, "NOAvionics.ReliefViewPill");
            pillRoot.anchorMin = pillRoot.anchorMax = pillRoot.pivot = new Vector2(1f, 1f);
            pillRoot.anchoredPosition = new Vector2(-10f, -10f);
            pillRoot.sizeDelta = new Vector2(PillW, PillH);

            pillFrame = AvFrame.Add(pillRoot, "Back", AvChamfer.Diagonal(4f));
            AvLay.Fill(pillFrame.rectTransform);
            pillFrame.Stroke = 1f;
            pillFrame.raycastTarget = true;

            float x = 5f;
            northBtn = PillBtn("N", AvIcon.ArrowUp, ReliefNavigator.North,
                "Compass: needle points north. Click for North-Up; click again while North-Up for top-down.", ref x, 66f);
            needle = northBtn.Glyph;
            perspBtn = PillBtn("TOP", AvIcon.None, ReliefNavigator.TogglePerspective,
                "Switch between top-down chart view and tilted 3D relief.", ref x, 62f);
            fitBtn = PillBtn("FIT", AvIcon.Maximize, FitView,
                "Fit the whole mission map (Home key).", ref x, 52f);

            zoomLabel = AvText.Make(pillRoot, "Zoom", AvTextRole.DataSmall, "×1.0", TextAlignmentOptions.MidlineRight);
            MfdChromeLay.Place(zoomLabel.rectTransform, new Rect(x + 2f, -4f, PillW - x - 8f, 20f));
            zoomLabel.raycastTarget = false;
            AvText.Fit(zoomLabel, false);

            // Bottom-right scale bar + cursor readout (non-interactive, seated cleanly above native Current grid label)
            scaleRoot = AvLay.Child(chrome, "NOAvionics.ReliefScaleHud");
            scaleRoot.anchorMin = scaleRoot.anchorMax = scaleRoot.pivot = new Vector2(1f, 0f);
            scaleRoot.anchoredPosition = new Vector2(-12f, 48f);
            scaleRoot.sizeDelta = new Vector2(ScaleW, ScaleH);

            scaleVector = AvVector.Create(scaleRoot, "ScaleBar", 24);
            scaleLabel = AvText.Make(scaleRoot, "ScaleText", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
            scaleLabel.rectTransform.anchorMin = scaleLabel.rectTransform.anchorMax = scaleLabel.rectTransform.pivot = new Vector2(1f, 0f);
            scaleLabel.rectTransform.anchoredPosition = new Vector2(-6f, 14f);
            scaleLabel.rectTransform.sizeDelta = new Vector2(120f, 14f);
            scaleLabel.raycastTarget = false;
            AvText.Fit(scaleLabel, false);

            cursorLabel = AvText.Make(scaleRoot, "CursorReadout", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
            cursorLabel.rectTransform.anchorMin = cursorLabel.rectTransform.anchorMax = cursorLabel.rectTransform.pivot = new Vector2(1f, 0f);
            cursorLabel.rectTransform.anchoredPosition = new Vector2(-6f, 28f);
            cursorLabel.rectTransform.sizeDelta = new Vector2(ScaleW - 12f, 14f);
            cursorLabel.raycastTarget = false;
            AvText.Fit(cursorLabel, false);

            lastTheme = -1;
            lastRev = -1;
            lastBarPx = -1f;
            lastScaleText = lastCursorText = "";
        }

        private static AvControl PillBtn(string label, AvIcon icon, System.Action onClick, string help, ref float x, float width)
        {
            AvControl c = AvControl.Make(pillRoot, new AvControl.Spec(label, onClick, AvButtonStyle.Quiet, icon));
            MfdChromeLay.Place(c.Rect, new Rect(x, -4f, width, 20f));
            c.SingleLine();
            c.Bracket = 0f;
            c.Help = help;
            x += width + 3f;
            return c;
        }

        private static void Restyle()
        {
            pillFrame?.Paint(AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.90f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame).WithAlpha(.88f));
            Color ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            Color dim = AvStyleHost.FuiColor("muted", AvTheme.Dim);
            if (zoomLabel != null) zoomLabel.color = dim;
            if (scaleLabel != null) scaleLabel.color = ink;
            if (cursorLabel != null) cursorLabel.color = ink.WithAlpha(.92f);
            northBtn?.Restyle();
            perspBtn?.Restyle();
            fitBtn?.Restyle();
            lastBarPx = -1f;
        }

        internal static void Hide()
        {
            if (pillRoot != null)
            {
                MapUiPointer.Unregister(pillRoot);
                if (pillRoot.gameObject.activeSelf) pillRoot.gameObject.SetActive(false);
            }
            if (scaleRoot != null && scaleRoot.gameObject.activeSelf)
                scaleRoot.gameObject.SetActive(false);
        }

        internal static void Reset()
        {
            if (pillRoot != null)
            {
                MapUiPointer.Unregister(pillRoot);
                pillRoot.gameObject.SetActive(false);
                Object.Destroy(pillRoot.gameObject);
            }
            if (scaleRoot != null)
            {
                scaleRoot.gameObject.SetActive(false);
                Object.Destroy(scaleRoot.gameObject);
            }
            pillRoot = scaleRoot = null;
            pillFrame = null;
            northBtn = perspBtn = fitBtn = null;
            needle = zoomLabel = scaleLabel = cursorLabel = null;
            scaleVector = null;
            lastBarPx = -1f;
            lastScaleText = lastCursorText = "";
            lastTheme = lastRev = -1;
        }
    }
}
