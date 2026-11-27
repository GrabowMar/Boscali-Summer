using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// On-map visual cues around the pointer: hover brackets on contacts and airbases,
    /// the 3D orbit pivot ring on the relief terrain, a live dashed ruler from the dropped
    /// range mark to the cursor, selection-box live count preview, and a compact contextual
    /// action pill when the cursor enters a non-trivial state. Zero raycast blocking.
    /// </summary>
    internal static class MapCursorCue
    {
        private const float PillHeight = 20f;
        private const float RulerLabelHeight = 18f;

        private static RectTransform host;
        private static AvVector vector;
        private static RectTransform pillRect;
        private static AvFrame pillFrame;
        private static TMP_Text pillText;
        private static RectTransform rulerRect;
        private static AvFrame rulerFrame;
        private static TMP_Text rulerText;
        private static string lastPill = "", lastRuler = "";
        private static Color lastPillTone, lastRulerTone;

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
            if (host == null || host.parent != chrome) Build(chrome);
            if (!host.gameObject.activeSelf) host.gameObject.SetActive(true);

            AvQuadBuffer b = vector.Buffer;
            b.Clear();

            Camera uiCam = MapUiPointer.UiCamera(viewport.GetComponentInParent<Canvas>());
            Rect bounds = viewport.rect;
            Vector2 mouseScreen = Input.mousePosition;
            bool hasMouse = RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, mouseScreen, uiCam, out Vector2 mouseLocal);

            Color accent = AvStyleHost.FuiColor("select", AvTheme.Accent);
            Color warn = AvStyleHost.FuiColor("caution", AvTheme.Warning);
            Color friendly = AvStyleHost.FuiColor("friendly", AvTheme.Friendly);
            Color hostile = AvStyleHost.FuiColor("danger", AvTheme.Alert);
            Color ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);

            bool showCue = CommandSettingsScope.Current?.MapCursor?.Value != false;

            // A. Hover target brackets on the icon under the cursor.
            if (showCue && MapCursor.OverMap && !ReliefNavigator.Turning && !ReliefNavigator.Grabbing &&
                MfdMapInteractions.BoxDragging == MapBoxMode.None && !MapPicker.IsBusy)
            {
                UnitMapIcon contact = MapCursor.HoveredContact;
                AirbaseMapIcon airbase = contact == null ? MapCursor.HoveredAirbase : null;
                if (contact != null && contact.iconImage != null &&
                    ToHost(viewport, uiCam, contact.iconImage.transform.position, out Vector2 iconPos))
                {
                    bool selected = map.selectedIcons != null && map.selectedIcons.Contains(contact);
                    Color bracketInk = selected ? warn : accent;
                    DrawBrackets(b, bounds, iconPos, 15f, 5.5f, 1.8f, bracketInk);
                }
                else if (airbase != null && airbase.iconImage != null &&
                    ToHost(viewport, uiCam, airbase.iconImage.transform.position, out Vector2 basePos))
                {
                    FactionMode side = airbase.airbase != null ? DynamicMap.GetFactionMode(airbase.airbase.CurrentHQ, false) : default;
                    Color baseInk = side == FactionMode.Friendly ? friendly : side == FactionMode.Enemy ? hostile : accent;
                    DrawBrackets(b, bounds, basePos, 18f, 6.5f, 1.8f, baseInk);
                }
            }

            // B. 3D orbit pivot ring projected onto the terrain point being orbited.
            if (ReliefNavigator.Turning && MfdTerrainRelief.IsDrawing &&
                MfdTerrainRelief.TryProjectModel(ReliefNavigator.OrbitAnchor, out Vector2 pivot))
            {
                float px = pivot.x - bounds.xMin, py = pivot.y - bounds.yMin;
                Rgba ringInk = accent.WithAlpha(.9f).ToRgba();
                AvStrokes.Ring(b, px, py, 12f, 24, 1.6f, ringInk);
                AvStrokes.DashedRing(b, px, py, 20f, 12, .5f, 1.2f, accent.WithAlpha(.55f).ToRgba());
                AvStrokes.Line(b, px - 16f, py, px - 8f, py, 1.4f, ringInk);
                AvStrokes.Line(b, px + 8f, py, px + 16f, py, 1.4f, ringInk);
                AvStrokes.Line(b, px, py - 16f, px, py - 8f, 1.4f, ringInk);
                AvStrokes.Line(b, px, py + 8f, px, py + 16f, 1.4f, ringInk);
            }

            // C. Live range-mark ruler line + midpoint distance/bearing badge.
            bool rulerShown = false;
            if (MfdMapInteractions.Mark.HasValue && MapCursor.OverMap && hasMouse &&
                !ReliefNavigator.Turning && !ReliefNavigator.Grabbing &&
                TryProjectWorld(map, viewport, MfdMapInteractions.Mark.Value, out Vector2 markLocal) &&
                map.TryGetCursorCoordinates(out GlobalPosition ground))
            {
                Vector2 a = new Vector2(markLocal.x - bounds.xMin, markLocal.y - bounds.yMin);
                Vector2 c = new Vector2(mouseLocal.x - bounds.xMin, mouseLocal.y - bounds.yMin);
                if ((c - a).sqrMagnitude > 144f)
                {
                    AvStrokes.DashedLine(b, a.x, a.y, c.x, c.y, 7f, 5f, 1.5f, accent.WithAlpha(.78f).ToRgba());
                    AvStrokes.Ring(b, c.x, c.y, 4.5f, 12, 1.4f, accent.ToRgba());
                    string leg = MfdMapInteractions.LegText(MfdMapInteractions.Mark.Value, ground);
                    PlaceBadge(rulerRect, rulerFrame, rulerText, ref lastRuler, ref lastRulerTone,
                        bounds, (markLocal + mouseLocal) * .5f, leg, accent, ink, RulerLabelHeight, centered: true);
                    rulerShown = true;
                }
            }
            if (!rulerShown && rulerRect.gameObject.activeSelf) rulerRect.gameObject.SetActive(false);

            // D & E. Live contextual cursor pill (box count, armed tool, orbit telemetry, hover action).
            bool pillShown = false;
            if (showCue && hasMouse)
            {
                string label = ResolvePill(map, accent, warn, friendly, out Color pillTone);
                if (!string.IsNullOrEmpty(label))
                {
                    PlaceBadge(pillRect, pillFrame, pillText, ref lastPill, ref lastPillTone,
                        bounds, mouseLocal + new Vector2(18f, -18f), label, pillTone, ink, PillHeight, centered: false);
                    pillShown = true;
                }
            }
            if (!pillShown && pillRect.gameObject.activeSelf) pillRect.gameObject.SetActive(false);

            vector.Commit();
        }

        private static string ResolvePill(DynamicMap map, Color accent, Color warn, Color friendly, out Color tone)
        {
            tone = accent;
            MapBoxMode dragging = MfdMapInteractions.BoxDragging;
            if (dragging != MapBoxMode.None)
            {
                int n = MfdMapInteractions.PreviewBoxCount(map, MfdMapInteractions.CurrentBoxScreenRect, dragging);
                tone = dragging == MapBoxMode.Drop ? warn : accent;
                return dragging == MapBoxMode.Drop ? "-" + n + " DROP" : "+" + n + " ADD";
            }
            if (ReliefNavigator.Turning)
            {
                int yaw = Mathf.RoundToInt(MfdTerrainRelief.Yaw);
                string hdg = (yaw > 0 ? "+" : yaw < 0 ? "-" : "") + AvNum.Fixed(Mathf.Abs(yaw), 0).PadLeft(3, '0');
                return "ORBIT  " + hdg + "° · " + MfdChromeLay.TwoDigits(Mathf.RoundToInt(MfdTerrainRelief.Pitch)) + "°";
            }
            if (!MapCursor.OverMap) return null;
            if (MapPicker.IsBusy)
            {
                tone = warn;
                string prompt = MapPicker.Prompt ?? "ARMED TOOL";
                int dot = prompt.IndexOf('·');
                string shortVerb = dot > 0 ? prompt.Substring(0, dot).Trim() : prompt;
                if (shortVerb.Length > 20) shortVerb = shortVerb.Substring(0, 20);
                return shortVerb + " · RMB PLACE";
            }
            if (MfdTerrainRelief.IsDrawing && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
                return "ALT+DRAG: ORBIT / TILT";
            MapBoxMode box = MfdMapInteractions.BoxModeNow();
            if (box != MapBoxMode.None)
            {
                tone = box == MapBoxMode.Drop ? warn : accent;
                return box == MapBoxMode.Drop ? "DRAG: BOX DROP" : "DRAG: BOX ADD";
            }
            if (MfdTerrainRelief.IsDrawing && MfdTerrainRelief.HoveredStackCount >= 3)
                return "STACK (" + MfdTerrainRelief.HoveredStackCount + ") · RMB STACK";

            UnitMapIcon contact = MapCursor.HoveredContact;
            if (contact != null)
            {
                bool selected = map.selectedIcons != null && map.selectedIcons.Contains(contact);
                bool adds = CommandSettingsScope.Current?.MapClickAdds?.Value != false ||
                    MfdMapInteractions.ShiftHeld() || MfdMapInteractions.ControlHeld();
                if (selected && adds) { tone = warn; return "LMB: DROP · RMB: MENU"; }
                return adds ? "LMB: +ADD · RMB: MENU" : "LMB: ONLY · RMB: MENU";
            }
            if (MapCursor.HoveredAirbase != null) return "LMB: BASE · RMB: MENU";
            MapRightClick right = MfdMapInteractions.RightRule(map);
            if (right == MapRightClick.Wing) { tone = friendly; return "RMB: MOVE WING"; }
            if (right == MapRightClick.Native) { tone = friendly; return "RMB: WAYPOINT"; }
            return null;
        }

        private static void DrawBrackets(AvQuadBuffer b, Rect bounds, Vector2 local, float half, float arm, float stroke, Color color)
        {
            float cx = local.x - bounds.xMin, cy = local.y - bounds.yMin;
            AvStrokes.Bracket(b, cx - half, cy - half, half * 2f, half * 2f, arm, stroke, color.ToRgba());
        }

        private static bool ToHost(RectTransform viewport, Camera uiCam, Vector3 worldPos, out Vector2 local)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCam, worldPos);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screen, uiCam, out local) &&
                viewport.rect.Contains(local);
        }

        private static bool TryProjectWorld(DynamicMap map, RectTransform viewport, GlobalPosition world, out Vector2 local)
        {
            local = default;
            RectTransform sheet = MfdTerrainRelief.MapImageRect(map.mapImage);
            if (sheet == null) return false;
            Vector2 sheetPoint;
            if (MfdTerrainRelief.IsDrawing)
            {
                if (!MfdTerrainRelief.TryProject(world.x, world.z, sheet.rect, out sheetPoint)) return false;
            }
            else if (map.mapDisplayFactor > 0f)
            {
                sheetPoint = new Vector2(world.x, world.z) * map.mapDisplayFactor;
            }
            else return false;
            Vector3 p = viewport.InverseTransformPoint(sheet.TransformPoint(sheetPoint));
            local = new Vector2(p.x, p.y);
            return true;
        }

        private static void PlaceBadge(RectTransform rect, AvFrame frame, TMP_Text text,
            ref string lastText, ref Color lastTone, Rect bounds, Vector2 targetLocal, string value,
            Color border, Color ink, float height, bool centered)
        {
            if (!rect.gameObject.activeSelf) rect.gameObject.SetActive(true);
            if (value != lastText)
            {
                lastText = value;
                text.text = value;
            }
            float w = Mathf.Clamp(Mathf.Ceil(AvText.Width(text)) + 14f, 44f, 220f);
            rect.sizeDelta = new Vector2(w, height);
            frame.rectTransform.sizeDelta = new Vector2(w, height);
            if (lastTone != border)
            {
                lastTone = border;
                frame.Paint(AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.90f), border.WithAlpha(.85f));
                text.color = ink;
            }
            float x = centered ? targetLocal.x - w * .5f : targetLocal.x;
            float y = centered ? targetLocal.y + height * .5f : targetLocal.y;
            x = Mathf.Clamp(x, bounds.xMin + 6f, bounds.xMax - w - 6f);
            y = Mathf.Clamp(y, bounds.yMin + height + 6f, bounds.yMax - 6f);
            rect.anchoredPosition = new Vector2(x, y);
        }

        private static void Build(RectTransform chrome)
        {
            Reset();
            host = AvLay.Child(chrome, "NOAvionics.CursorCue");
            AvLay.Fill(host);
            host.SetAsFirstSibling();

            vector = AvVector.Create(host, "Vector", 256, 1.2f, .22f);

            MakeBadge(host, "Pill", 120f, PillHeight, out pillRect, out pillFrame, out pillText);
            MakeBadge(host, "RulerLabel", 110f, RulerLabelHeight, out rulerRect, out rulerFrame, out rulerText);
            pillRect.gameObject.SetActive(false);
            rulerRect.gameObject.SetActive(false);
        }

        private static void MakeBadge(RectTransform parent, string name, float width, float height,
            out RectTransform rect, out AvFrame frame, out TMP_Text text)
        {
            rect = AvLay.Child(parent, name);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);

            frame = AvFrame.Add(rect, "Back", AvChamfer.Diagonal(3f));
            AvLay.Fill(frame.rectTransform);
            frame.Stroke = 1f;
            frame.raycastTarget = false;

            text = AvText.Make(rect, "Text", AvTextRole.Micro, "", TextAlignmentOptions.Center);
            AvLay.Fill(text.rectTransform);
            text.raycastTarget = false;
            AvText.Fit(text, false);
        }

        internal static void Hide()
        {
            if (host != null && host.gameObject.activeSelf) host.gameObject.SetActive(false);
            if (pillRect != null) pillRect.gameObject.SetActive(false);
            if (rulerRect != null) rulerRect.gameObject.SetActive(false);
            lastPill = lastRuler = "";
        }

        internal static void Reset()
        {
            if (host != null) { host.gameObject.SetActive(false); Object.Destroy(host.gameObject); }
            host = null;
            vector = null;
            pillRect = rulerRect = null;
            pillFrame = rulerFrame = null;
            pillText = rulerText = null;
            lastPill = lastRuler = "";
            lastPillTone = lastRulerTone = default;
        }
    }
}
