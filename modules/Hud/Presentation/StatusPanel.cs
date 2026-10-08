using System.Reflection;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;
using HarmonyLib;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The status board under the native weapons panel (<c>HUDCanvas/HMDCenter/TopRightPanel</c>), drawn in the base
    /// game's own cell language and sized from the native panel itself (2026-10-07):
    /// <list type="bullet">
    /// <item>systems row: always-on annunciator lamps of the aircraft's switches (<see cref="HudLamps"/>);</item>
    /// <item>threat row (after KaceyTronic-RWR): LOCK / SPKE / MSL lamps (<see cref="HudThreats"/>), a threat dial with a
    /// chevron at the nearest inbound missile (else the locking emitter) and HI/LO, and the shot tracker's pips (after
    /// NO_Tactitools: in flight, green hit, red miss; <see cref="ShotLedger"/>);</item>
    /// <item>one outlined cell per live feed: figure, line-art symbol (<see cref="HudGlyphs"/>), label;</item>
    /// <item>a capacitor-style bar carrying the most severe line's detail and progress.</item>
    /// </list>
    /// Every new aircraft gets a lamp test (<see cref="LampTest"/>): lamps sweep on, flash together, then go live, with
    /// LAMP TEST / SYSTEMS ONLINE on the bar. Sized from the native weapon cell and labels, right-aligned to the visible
    /// screen edge, flush under the visible native block (cells, capacitor bar) with no gap and
    /// spanning it edge to edge (content bounds measured live; the container carries
    /// padding); native's own child, never touches a native object.
    /// </summary>
    internal sealed class StatusPanel
    {
        // Design units at native scale 1 (native weapon cell 95 tall).
        private const float NativeCellRef = 95f;
        private const float Pad = 4f;
        private const float LampHeight = 24f;
        private const float LampGap = 3f;
        private const float CellHeight = 68f;
        private const float CellGap = 4f;
        private const float CellLine = 1.5f;
        private const float BarHeight = 6f;
        private const float StripTextHeight = 20f;
        private const float PipSize = 11f;
        private const float FlashSeconds = 1.6f;
        private const int VectorCapacity = 4000;
        // Green-glass ground (AvTokens.Ground) at HUD transparency: the MFD plate hue, see-through.
        private static readonly Color PlateColor = new Color(0.020f, 0.039f, 0.059f, 0.88f);

        // Text sizes as a share of the native labels' own point sizes; fixed fallbacks when there is no native label.
        private const float ValueShare = 0.72f, LabelShare = 0.62f, LegendShare = 0.56f, DetailShare = 0.56f;
        private const float FallbackValue = 17f, FallbackLabel = 12f, FallbackLegend = 11f, FallbackDetail = 12f;

        private static readonly FieldInfo TopRightPanelField = AccessTools.Field(typeof(CombatHUD), "topRightPanel");
        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo NameTextField = AccessTools.Field(typeof(WeaponStatus), "nameText");
        private static readonly FieldInfo AmmoTextField = AccessTools.Field(typeof(WeaponStatus), "ammoText");
        private static readonly FieldInfo CellPanelField = AccessTools.Field(typeof(WeaponStatus), "panel");
        private static readonly FieldInfo SafetyField = AccessTools.Field(typeof(WeaponStatus), "safetyImage");

        private RectTransform dock;
        private RectTransform canvasRect;
        private RectTransform root;
        private Image plate;
        private AvVector vector;
        private RectTransform barTrack;
        private Image barTrackImage, barFill;
        private TextMeshProUGUI stripLabel, dialLabel;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private RectTransform nativeCell;
        private Image nativeSafety;
        private float unit = 1f;
        private int lastSignature;

        private readonly Cell[] cells = new Cell[HudLayout.MaxRows];
        private readonly TextMeshProUGUI[] legends = new TextMeshProUGUI[HudLamps.Count];
        private readonly TextMeshProUGUI[] threatLegends = new TextMeshProUGUI[HudThreats.Count];
        private readonly Lamp[] lamps = new Lamp[HudLamps.Count];
        private readonly Lamp[] threatLamps = new Lamp[HudThreats.Count];
        private readonly int[] lampOrder = new int[HudLamps.Count];
        private readonly Vector3[] corners = new Vector3[4];

        private struct Cell
        {
            public TextMeshProUGUI Value;
            public TextMeshProUGUI Label;
            public HudTone LastTone;
            public string LastText;
            public float FlashUntil;
        }

        /// <summary>One tick's geometry, shared by Present (text) and Paint (mesh).</summary>
        private struct Frame
        {
            public float Width, Height, LampsY, ThreatY, CellsY, LampW, CellW;
            public int LampCount, Count;
            public bool Threats, Booting, FlashOn;
            public float Now, BootAge;
        }

        public bool Available => root != null;

        /// <summary>
        /// Find the native dock and, on first success or after it is lost, (re)build our own
        /// tree under it. Cheap when already built (two null checks).
        /// </summary>
        public bool Ensure()
        {
            if (root != null && dock != null) return true;
            root = null;
            dock = null;

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.iconLayer == null || !hud.iconLayer.gameObject.activeInHierarchy) return false;
            Canvas hudCanvas = hud.iconLayer.GetComponentInParent<Canvas>();
            if (hudCanvas == null) return false;

            GameObject panelObject = TopRightPanelField?.GetValue(hud) as GameObject;
            RectTransform panelRect = panelObject != null ? panelObject.transform as RectTransform : null;
            if (panelRect == null || panelRect.GetComponentInParent<Canvas>() != hudCanvas) return false;

            Build(hud, panelRect);
            return root != null;
        }

        private void Build(CombatHUD hud, RectTransform panelRect)
        {
            dock = panelRect;
            Canvas canvas = dock.GetComponentInParent<Canvas>();
            canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;

            WeaponStatus weaponStatus = WeaponStatusField?.GetValue(hud) as WeaponStatus;
            TextMeshProUGUI nameLabel = weaponStatus != null ? NameTextField?.GetValue(weaponStatus) as TextMeshProUGUI : null;
            TextMeshProUGUI ammoLabel = weaponStatus != null ? AmmoTextField?.GetValue(weaponStatus) as TextMeshProUGUI : null;
            nativeCell = weaponStatus != null ? CellPanelField?.GetValue(weaponStatus) as RectTransform : null;
            nativeSafety = weaponStatus != null ? SafetyField?.GetValue(weaponStatus) as Image : null;
            font = nameLabel != null ? nameLabel.font : null;
            fontMaterial = nameLabel != null ? nameLabel.fontSharedMaterial : null;
            unit = MeasureUnit();

            float valueSize = Share(ammoLabel != null ? ammoLabel : nameLabel, ValueShare, FallbackValue);
            float labelSize = Share(nameLabel, LabelShare, FallbackLabel);
            float legendSize = Share(nameLabel, LegendShare, FallbackLegend);
            float detailSize = Share(nameLabel, DetailShare, FallbackDetail);

            var rootObject = new GameObject("BoscaliStatusPanel", typeof(RectTransform));
            root = (RectTransform)rootObject.transform;
            root.SetParent(dock, false);
            // Anchored to the dock's bottom-left; Present() right-aligns it to the visible screen edge.
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(1f, 1f);

            plate = HudSprites.Child(root, "Plate", HudSprites.Chamfer, sliced: true);
            plate.color = PlateColor;
            vector = AvVector.Create(root, "Symbols", VectorCapacity);

            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].Value = Text("Value" + i, TextAlignmentOptions.TopLeft, valueSize, TextOverflowModes.Overflow);
                cells[i].Label = Text("Label" + i, TextAlignmentOptions.Bottom, labelSize, TextOverflowModes.Ellipsis);
                cells[i].Value.gameObject.SetActive(false);
                cells[i].Label.gameObject.SetActive(false);
            }
            for (int i = 0; i < legends.Length; i++)
            {
                legends[i] = Text("Lamp" + i, TextAlignmentOptions.Center, legendSize, TextOverflowModes.Overflow);
                legends[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < threatLegends.Length; i++)
            {
                threatLegends[i] = Text("Threat" + i, TextAlignmentOptions.Center, legendSize, TextOverflowModes.Overflow);
                threatLegends[i].gameObject.SetActive(false);
            }
            dialLabel = Text("HiLo", TextAlignmentOptions.MidlineLeft, legendSize, TextOverflowModes.Overflow);

            // Capacitor-style readout: a rounded bar with its fill, the words under it.
            var trackObject = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            barTrack = (RectTransform)trackObject.transform;
            barTrack.SetParent(root, false);
            Pin(barTrack);
            barTrackImage = trackObject.GetComponent<Image>();
            barTrackImage.raycastTarget = false;
            HudSprites.Slice(barTrackImage, HudSprites.Plate);
            barFill = HudSprites.Child(barTrack, "Fill", HudSprites.Plate, sliced: true);
            barFill.rectTransform.offsetMin = new Vector2(1.5f, 1.5f);
            stripLabel = Text("Detail", TextAlignmentOptions.Center, detailSize, TextOverflowModes.Ellipsis);
            stripLabel.characterSpacing = 4f;
            lastSignature = 0;
        }

        /// <summary>Native weapon cell height over the design reference: how big one design unit is here.</summary>
        private float MeasureUnit()
        {
            if (nativeCell == null) return 1f;
            float h = Mathf.Abs(dock.InverseTransformVector(nativeCell.TransformVector(new Vector3(0f, nativeCell.rect.height, 0f))).y);
            return h > 10f ? h / NativeCellRef : 1f;
        }

        private static float Share(TMP_Text native, float share, float fallback) =>
            native != null && native.fontSize > 1f ? native.fontSize * share : fallback;

        private TextMeshProUGUI Text(string name, TextAlignmentOptions alignment, float size, TextOverflowModes overflow)
        {
            TextMeshProUGUI t = HudText.Make(root, name, font, fontMaterial, alignment, size, overflow);
            t.enableWordWrapping = false;
            Pin(t.rectTransform);
            return t;
        }

        /// <summary>Top-left anchored, so positions are plain (x, -y) offsets inside the board.</summary>
        private static void Pin(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            Vector2 position = new Vector2(x, -y), size = new Vector2(w, h);
            if (rect.anchoredPosition != position) rect.anchoredPosition = position;
            if (rect.sizeDelta != size) rect.sizeDelta = size;
        }

        private static void Show(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        /// <summary>The dock-local x of the visible right edge: the dock's own, or the screen's if the dock runs past it.</summary>
        private float VisibleRight()
        {
            float right = dock.rect.xMax;
            if (canvasRect == null) return right;
            canvasRect.GetWorldCorners(corners);
            return Mathf.Min(right, dock.InverseTransformPoint(corners[2]).x);
        }

        /// <summary>The visible native block (weapon cells, flares, capacitor bar), in dock-local
        /// coords: the union of the dock's visual children, excluding our own board and any
        /// full-container backdrop. The container itself carries padding (~44 px left at 1080p),
        /// so matching it leaves a step; matching content does not. Falls back to the dock rect
        /// when nothing usable is found.</summary>
        private void ContentBounds(out float left, out float right, out float bottom)
        {
            left = dock.rect.xMin;
            right = Mathf.Min(dock.rect.xMax, VisibleRight());
            bottom = dock.rect.yMin;
            float cMinX = float.MaxValue, cMaxX = float.MinValue, cMinY = float.MaxValue;
            float dockArea = Mathf.Max(1f, dock.rect.width * dock.rect.height);
            var corners = new Vector3[4];
            for (int i = 0; i < dock.childCount; i++)
            {
                var child = dock.GetChild(i) as RectTransform;
                if (child == null || child == root || !child.gameObject.activeInHierarchy) continue;
                if (child.GetComponentInChildren<Graphic>() == null) continue;
                child.GetWorldCorners(corners);
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = dock.InverseTransformPoint(corners[k]);
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }
                // A child blanketing the container is a backdrop, not content.
                if ((maxX - minX) * (maxY - minY) >= dockArea * 0.98f) continue;
                if (minX < cMinX) cMinX = minX;
                if (maxX > cMaxX) cMaxX = maxX;
                if (minY < cMinY) cMinY = minY;
            }
            if (cMinX > cMaxX || cMaxX - cMinX < 10f) return;
            left = cMinX;
            right = Mathf.Min(cMaxX, VisibleRight());
            bottom = cMinY;
            if (right - left < 10f) { left = dock.rect.xMin; right = Mathf.Min(dock.rect.xMax, VisibleRight()); }
        }

        /// <summary>
        /// Refresh the board (10 Hz from the board service). <paramref name="bootAge"/> is seconds since the current
        /// aircraft appeared (negative = no test). Text writes only on change; the symbol mesh is rebuilt only when
        /// something it draws changed.
        /// </summary>
        public void Present(HudMessage[] snapshot, int count, SystemStates systems, in ThreatPicture threat,
            ShotLedger shots, float bootAge)
        {
            if (root == null) return;
            if (nativeSafety != null) systems.WeaponSafe = nativeSafety.enabled;
            HudLamps.Fill(systems, lamps);
            HudThreats.Fill(threat, threatLamps);
            var f = new Frame { Now = Time.unscaledTime, BootAge = bootAge };
            for (int i = 0; i < lamps.Length; i++) if (lamps[i].Present) lampOrder[f.LampCount++] = i;
            f.Count = Mathf.Min(count, cells.Length);
            f.Threats = f.LampCount > 0;
            f.Booting = f.Threats && LampTest.Running(bootAge);
            string caption = f.Threats ? LampTest.Caption(bootAge) : null;
            if (f.Count <= 0 && !f.Threats)
            {
                Show(root, false);
                return;
            }
            Show(root, true);

            float u = unit, pad = Pad * u;
            f.FlashOn = (int)(f.Now * 5f) % 2 == 0;
            ContentBounds(out float contentLeft, out float contentRight, out float contentBottom);
            f.Width = contentRight - contentLeft;
            HudMessage top = f.Count > 0 ? snapshot[0] : default;
            bool strip = caption != null || (f.Count > 0 && (!string.IsNullOrEmpty(top.Detail) || top.Bar > 0f));

            float row = (LampHeight + CellGap) * u;
            f.LampsY = pad;
            f.ThreatY = f.LampsY + (f.LampCount > 0 ? row : 0f);
            f.CellsY = f.ThreatY + (f.Threats ? row : 0f);
            float stripY = f.CellsY + (f.Count > 0 ? (CellHeight + CellGap) * u : 0f);
            f.Height = stripY + (strip ? (BarHeight + StripTextHeight) * u : 0f) + pad - CellGap * u;
            Vector2 rootPosition = new Vector2(contentRight - dock.rect.xMin, contentBottom - dock.rect.yMin);
            if (root.anchoredPosition != rootPosition) root.anchoredPosition = rootPosition;
            Vector2 rootSize = new Vector2(f.Width, f.Height);
            if (root.sizeDelta != rootSize) root.sizeDelta = rootSize;
            f.LampW = f.LampCount > 0 ? (f.Width - pad * 2f - LampGap * u * (f.LampCount - 1)) / f.LampCount : 0f;

            int signature = (int)f.Width * 31 + f.Count * 7 + f.LampCount + (strip ? 3 : 0) + (f.FlashOn ? 1 : 0) +
                (f.Booting ? (int)(bootAge * 20f) * 101 : 0) + shots.Version * 7919;

            // Systems row.
            int total = f.LampCount + HudThreats.Count;
            for (int i = 0; i < legends.Length; i++)
            {
                bool on = i < f.LampCount;
                Show(legends[i], on);
                if (!on) continue;
                Lamp lamp = lamps[lampOrder[i]];
                if (legends[i].text != lamp.Legend) legends[i].text = lamp.Legend;
                legends[i].color = LampInk(lamp, LampOn(f, lamp, i, total), f.Booting);
                Place(legends[i].rectTransform, pad + i * (f.LampW + LampGap * u), f.LampsY, f.LampW, LampHeight * u);
                signature = signature * 13 + (int)lamp.State * 5 + lampOrder[i];
            }

            // Threat row: three lamps the width of system lamps, then the dial, then shot pips.
            for (int i = 0; i < threatLegends.Length; i++)
            {
                Show(threatLegends[i], f.Threats);
                if (!f.Threats) continue;
                Lamp lamp = threatLamps[i];
                if (threatLegends[i].text != lamp.Legend) threatLegends[i].text = lamp.Legend;
                threatLegends[i].color = LampInk(lamp, LampOn(f, lamp, f.LampCount + i, total), f.Booting);
                Place(threatLegends[i].rectTransform, pad + i * (f.LampW + LampGap * u), f.ThreatY, f.LampW, LampHeight * u);
                signature = signature * 13 + (int)lamp.State * 5 + lamp.Legend.Length;
            }
            string hiLo = f.Threats && threat.HasBearing ? HudThreats.HighLow(threat.ElevationDeg) : "";
            Show(dialLabel, hiLo.Length > 0);
            if (hiLo.Length > 0)
            {
                if (dialLabel.text != hiLo) dialLabel.text = hiLo;
                dialLabel.color = ThreatColor(threat);
                Place(dialLabel.rectTransform, DialX(f) + LampHeight * u * 0.5f + 3f * u, f.ThreatY, 26f * u, LampHeight * u);
            }
            if (f.Threats && threat.HasBearing)
                signature = signature * 7 + Mathf.RoundToInt(threat.BearingDeg / 5f) * 3 + (threat.BearingIsMissile ? 1 : 0);

            // Feed cells.
            f.CellW = f.Count > 0 ? (f.Width - pad * 2f - CellGap * u * (f.Count - 1)) / f.Count : 0f;
            for (int i = 0; i < cells.Length; i++)
            {
                bool on = i < f.Count;
                Show(cells[i].Value, on);
                Show(cells[i].Label, on);
                if (!on) continue;

                HudMessage m = snapshot[i];
                if (m.Tone == HudTone.Warning && (cells[i].LastTone != HudTone.Warning || cells[i].LastText != m.Text))
                    cells[i].FlashUntil = f.Now + FlashSeconds;
                if (cells[i].LastText != m.Text || cells[i].LastTone != m.Tone)
                {
                    HudCellText.Split(m.Text, out string label, out string value);
                    if (value.Length == 0 && m.Bar > 0f) value = Mathf.RoundToInt(m.Bar * 100f) + "%";
                    cells[i].Value.text = value;
                    cells[i].Label.text = label;
                    cells[i].LastText = m.Text;
                    cells[i].LastTone = m.Tone;
                }
                Color tone = ToneColor(m.Tone);
                cells[i].Value.color = tone;
                cells[i].Label.color = tone;
                float x = pad + i * (f.CellW + CellGap * u);
                Place(cells[i].Value.rectTransform, x + 5f * u, f.CellsY + 2f * u, f.CellW - 8f * u, 24f * u);
                Place(cells[i].Label.rectTransform, x + 3f * u, f.CellsY + (CellHeight - 19f) * u, f.CellW - 6f * u, 17f * u);

                bool flashing = f.Now < cells[i].FlashUntil;
                signature = signature * 17 + (m.Text?.GetHashCode() ?? 0) + (int)m.Tone * 3 + (m.Notice ? 1 : 0)
                    + (m.Channel?.GetHashCode() ?? 0) + (flashing ? 5 : 0);
            }

            // Capacitor bar: the lamp test caption while it runs, else the top line's detail.
            Show(barTrack, strip);
            Show(stripLabel, strip);
            if (strip)
            {
                Place(barTrack, pad, stripY, f.Width - pad * 2f, BarHeight * u);
                Place(stripLabel.rectTransform, pad, stripY + BarHeight * u, f.Width - pad * 2f, StripTextHeight * u);
                Color tone = caption != null ? Ink : ToneColor(top.Tone);
                barTrackImage.color = new Color(tone.r * 0.12f, tone.g * 0.12f, tone.b * 0.12f, 0.9f);
                float fill = caption != null ? Mathf.Clamp01(bootAge / LampTest.Online) : Mathf.Clamp01(top.Bar);
                barFill.enabled = fill > 0f;
                barFill.rectTransform.anchorMax = new Vector2(Mathf.Max(fill, 0.02f), 1f);
                barFill.rectTransform.offsetMax = new Vector2(-1.5f, -1.5f);
                barFill.color = new Color(tone.r, tone.g, tone.b, 0.85f);
                string words = caption ?? (string.IsNullOrEmpty(top.Detail) ? "" : top.Detail.ToUpperInvariant());
                if (stripLabel.text != words) stripLabel.text = words;
                stripLabel.color = tone;
            }

            if (signature == lastSignature) return;
            lastSignature = signature;
            Paint(snapshot, f, threat, shots);
        }

        private float DialX(in Frame f) => Pad * unit + 3f * (f.LampW + LampGap * unit) + LampHeight * unit * 0.5f + 2f * unit;

        /// <summary>Lamp tiles, threat dial, shot pips, cell outlines and line-art in one mesh. Origin bottom-left, +Y up.</summary>
        private void Paint(HudMessage[] snapshot, in Frame f, in ThreatPicture threat, ShotLedger shots)
        {
            float u = unit, pad = Pad * u;
            AvQuadBuffer b = vector.Buffer;
            b.Clear();
            int total = f.LampCount + HudThreats.Count;

            for (int i = 0; i < f.LampCount; i++)
                LampTile(b, lamps[lampOrder[i]], LampOn(f, lamps[lampOrder[i]], i, total), f.Booting,
                    pad + i * (f.LampW + LampGap * u), f.Height - f.LampsY, f.LampW);

            if (f.Threats)
            {
                float rowTop = f.Height - f.ThreatY, rowMid = rowTop - LampHeight * u * 0.5f;
                for (int i = 0; i < HudThreats.Count; i++)
                    LampTile(b, threatLamps[i], LampOn(f, threatLamps[i], f.LampCount + i, total), f.Booting,
                        pad + i * (f.LampW + LampGap * u), rowTop, f.LampW);

                // Threat dial: own aircraft at the centre, nose up, a chevron on the rim at the threat's bearing.
                float cx = DialX(f), r = LampHeight * u * 0.46f;
                AvStrokes.Ring(b, cx, rowMid, r, 24, 1f * u, Ink.ToRgba().WithAlpha(0.45f));
                AvStrokes.Line(b, cx, rowMid - r * 0.35f, cx, rowMid + r * 0.35f, 1f * u, Ink.ToRgba().WithAlpha(0.6f));
                AvStrokes.Line(b, cx - r * 0.3f, rowMid + r * 0.05f, cx + r * 0.3f, rowMid + r * 0.05f, 1f * u, Ink.ToRgba().WithAlpha(0.6f));
                if (threat.HasBearing)
                {
                    float a = threat.BearingDeg * Mathf.Deg2Rad;
                    float px = cx + Mathf.Sin(a) * r, py = rowMid + Mathf.Cos(a) * r;
                    AvStrokes.Chevron(b, px, py, 7f * u, 270f - threat.BearingDeg, 1.75f * u, ThreatColor(threat).ToRgba()); // tip inward
                }

                // Shot pips: diamonds for missiles, circles for bombs; hollow in flight, filled green hit, red cross miss.
                float pipX = DialX(f) + r + 30f * u, step = PipSize * u + 3f * u;
                int slot = 0;
                for (int i = 0; i < ShotLedger.Capacity && pipX + slot * step < f.Width - pad; i++)
                {
                    ShotState state = shots.State(i);
                    if (state == ShotState.Empty) continue;
                    float x = pipX + slot * step + PipSize * u * 0.5f, s = PipSize * u * 0.5f;
                    Color c = state == ShotState.Hit ? Ink : state == ShotState.Miss ? Alert : Ink;
                    Rgba ink = c.ToRgba();
                    if (shots.IsBomb(i)) AvStrokes.Ring(b, x, rowMid, s * 0.8f, 12, (state == ShotState.Hit ? s * 0.8f : 1.25f * u), ink);
                    else if (state == ShotState.Hit) AvStrokes.Fill(b, x - s * 0.55f, rowMid - s * 0.55f, s * 1.1f, s * 1.1f, ink);
                    else AvStrokes.Diamond(b, x, rowMid, s, 1.25f * u, ink);
                    if (state == ShotState.Miss)
                    {
                        AvStrokes.Line(b, x - s * 0.7f, rowMid - s * 0.7f, x + s * 0.7f, rowMid + s * 0.7f, 1.5f * u, ink);
                        AvStrokes.Line(b, x - s * 0.7f, rowMid + s * 0.7f, x + s * 0.7f, rowMid - s * 0.7f, 1.5f * u, ink);
                    }
                    slot++;
                }
            }

            float top = f.Height - f.CellsY;
            for (int i = 0; i < f.Count; i++)
            {
                HudMessage m = snapshot[i];
                Color toneColor = ToneColor(m.Tone);
                bool flashing = f.Now < cells[i].FlashUntil;
                Rgba ink = toneColor.ToRgba().WithAlpha(flashing && !f.FlashOn ? 0.3f : 1f);
                float x0 = pad + i * (f.CellW + CellGap * u), x1 = x0 + f.CellW;
                float y1 = top, y0 = top - CellHeight * u;

                if (m.Tone != HudTone.Info)
                    AvStrokes.Fill(b, x0, y0, f.CellW, CellHeight * u, toneColor.ToRgba().WithAlpha(flashing && f.FlashOn ? 0.28f : 0.12f));
                float line = (m.Tone == HudTone.Warning ? CellLine + 1f : CellLine) * u;
                if (m.Notice) DashedRect(b, x0, y0, x1, y1, line, ink, u);
                else Rect4(b, x0, y0, x1, y1, line, ink);
                // MFD card rail: 2 px tone strip across the cell top, the green-glass idiom.
                AvStrokes.Fill(b, x0, y1 - 2f * u, f.CellW, 2f * u, ink);

                float size = Mathf.Min(f.CellW - 26f * u, (CellHeight - 38f) * u);
                float gx = x0 + (f.CellW - size) * 0.5f + 6f * u, gy = y0 + 18f * u;
                foreach (float[] poly in HudGlyphs.For(m.Channel))
                    for (int p = 0; p + 3 < poly.Length; p += 2)
                        AvStrokes.Line(b, gx + poly[p] * size, gy + poly[p + 1] * size,
                            gx + poly[p + 2] * size, gy + poly[p + 3] * size, 1.75f * u, ink, 0.6f * u);
            }
            vector.Commit();
        }

        private void LampTile(AvQuadBuffer b, Lamp lamp, bool lit, bool booting, float x0, float y1, float w)
        {
            float u = unit, y0 = y1 - LampHeight * u;
            Color tone = booting ? Ink : LampTone(lamp.State);
            if (lit) AvStrokes.Fill(b, x0, y0, w, LampHeight * u, tone.ToRgba().WithAlpha(0.30f));
            Rect4(b, x0, y0, x0 + w, y1, 1f * u, (lit ? tone : Ink).ToRgba().WithAlpha(lit ? 0.95f : 0.3f));
        }

        /// <summary>Lit for the lamp test's pattern while it runs, else by its own state (blinking on the off beat when flashing).</summary>
        private static bool LampOn(in Frame f, Lamp lamp, int index, int total) =>
            f.Booting ? LampTest.Lit(index, total, f.BootAge)
            : lamp.State != LampState.Dark && !((lamp.Flash || lamp.State == LampState.Warning) && !f.FlashOn);

        private static void Rect4(AvQuadBuffer b, float x0, float y0, float x1, float y1, float w, Rgba c)
        {
            AvStrokes.Line(b, x0, y0, x1, y0, w, c);
            AvStrokes.Line(b, x1, y0, x1, y1, w, c);
            AvStrokes.Line(b, x1, y1, x0, y1, w, c);
            AvStrokes.Line(b, x0, y1, x0, y0, w, c);
        }

        private static void DashedRect(AvQuadBuffer b, float x0, float y0, float x1, float y1, float width, Rgba c, float u)
        {
            float dash = 6f * u, gap = 4f * u;
            AvStrokes.DashedLine(b, x0, y0, x1, y0, dash, gap, width, c);
            AvStrokes.DashedLine(b, x1, y0, x1, y1, dash, gap, width, c);
            AvStrokes.DashedLine(b, x1, y1, x0, y1, dash, gap, width, c);
            AvStrokes.DashedLine(b, x0, y1, x0, y0, dash, gap, width, c);
        }

        private static Color Ink => ThemeManager.Active.ColorTheme.AllClear;
        private static Color Alert => ThemeManager.Active.ColorTheme.Alert;

        private static Color ThreatColor(in ThreatPicture t) => t.BearingIsMissile ? Alert : ThemeManager.Active.ColorTheme.Warning;

        private static Color LampTone(LampState state) =>
            state == LampState.Warning ? Alert
            : state == LampState.Caution ? ThemeManager.Active.ColorTheme.Warning
            : Ink;

        /// <summary>Legend ink: dim when dark, its tone when lit (plain ink during the lamp test).</summary>
        private static Color LampInk(Lamp lamp, bool lit, bool booting)
        {
            Color c = lit ? (booting ? Ink : LampTone(lamp.State)) : Ink;
            return new Color(c.r, c.g, c.b, lit ? 1f : 0.38f);
        }

        private static Color ToneColor(HudTone tone)
        {
            ColorTheme theme = ThemeManager.Active.ColorTheme;
            switch (tone)
            {
                case HudTone.Caution: return theme.Warning;
                case HudTone.Warning: return theme.Alert;
                default: return theme.AllClear;
            }
        }

        /// <summary>Take our board out of view without destroying it. Idempotent.</summary>
        public void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Drop every object this board created. Called on scene reset/teardown; also safe when the native dock
        /// already destroyed the tree for us (Unity fake-null).
        /// </summary>
        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            dock = null;
            canvasRect = null;
            plate = null;
            vector = null;
            barTrack = null;
            barTrackImage = barFill = null;
            stripLabel = dialLabel = null;
            nativeCell = null;
            nativeSafety = null;
            lastSignature = 0;
            for (int i = 0; i < cells.Length; i++) cells[i] = default;
            for (int i = 0; i < legends.Length; i++) legends[i] = null;
            for (int i = 0; i < threatLegends.Length; i++) threatLegends[i] = null;
        }
    }
}
