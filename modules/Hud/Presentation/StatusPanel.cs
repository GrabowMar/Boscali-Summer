using System.Collections.Generic;
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
    /// game's own cell language and sized from the native panel itself (2026-10-07). Rows, top to bottom, each present only
    /// when it has something to say (<see cref="HudBoardLayout"/> places them):
    /// <list type="bullet">
    /// <item>systems: always-on annunciator lamps of the aircraft's switches (<see cref="HudLamps"/>);</item>
    /// <item>threat: LOCK / SPKE / MSL lamps (<see cref="HudThreats"/>), a threat dial with a chevron at the nearest inbound
    /// missile (else the locking emitter) and HI/LO, and the shot tracker's pips (<see cref="ShotLedger"/>);</item>
    /// <item>feed cells: one outlined cell per live feed, figure, line-art symbol (<see cref="HudGlyphs"/>), label;</item>
    /// <item>cue strip: a capacitor-style bar carrying the most relevant line's detail and progress.</item>
    /// </list>
    /// Each tick the board is handed one <see cref="BoardFrame"/>. Every new aircraft gets a lamp test
    /// (<see cref="LampTest"/>). Sized from the native weapon cell and labels, right-aligned to the visible screen edge,
    /// flush under the visible native block (content bounds measured live); native's own child, never touches a native
    /// object. The class is partial: <c>StatusPanel.Rows.cs</c> holds the row writers and painters.
    /// </summary>
    internal sealed partial class StatusPanel
    {
        // Design units at native scale 1 (native weapon cell 95 tall); row sizes live in HudBoardLayout.
        private const float NativeCellRef = 95f;
        private const float CellLine = 1.5f;
        private const float FlashSeconds = 1.6f;
        /// <summary>One text line is about this many times its point size tall.</summary>
        private const float LineShare = 1.35f;
        private const int VectorCapacity = 6000;
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
        private TextMeshProUGUI stripLabel, dialLabel, shotLegend;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private float legendSize;
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
        private readonly Dictionary<int, bool> graphicCache = new Dictionary<int, bool>();

        private struct Cell
        {
            public TextMeshProUGUI Value;
            public TextMeshProUGUI Label;
            public HudTone LastTone;
            public string LastText;
            public int LastBarPct;
            public float FlashUntil;
        }

        /// <summary>One tick's derived state, shared by the row writers (text) and the painters (mesh).</summary>
        private struct Frame
        {
            public BoardGeometry G;
            public int LampCount, Count;
            public bool Threats, Booting, FlashOn, Strip;
            public float Now, BootAge, U;
            public string Caption;
            public HudMessage Top;
        }

        // Per-tick working state, set by Present and read by the row writers in the partial files.
        private Frame f;
        private BoardFrame board;
        private ShotLedger shots;
        private int signature;
        private bool beat;   // something on the board blinks, so the flash phase is part of what it draws

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
            legendSize = Share(nameLabel, LegendShare, FallbackLegend);
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

            BuildRows(valueSize, labelSize, detailSize);
            lastSignature = 0;
            graphicCache.Clear();
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

        /// <summary>Let a text that is too wide shrink to 70 % of its size before it elides.</summary>
        private static void Shrink(TextMeshProUGUI t, float size)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = size * 0.7f;
        }

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
            for (int i = 0; i < dock.childCount; i++)
            {
                var child = dock.GetChild(i) as RectTransform;
                if (child == null || child == root || !child.gameObject.activeInHierarchy) continue;
                if (!HasGraphic(child)) continue;
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

        /// <summary>Whether a dock child draws anything, looked up once per child instead of every tick.</summary>
        private bool HasGraphic(RectTransform child)
        {
            int id = child.GetInstanceID();
            if (!graphicCache.TryGetValue(id, out bool has)) graphicCache[id] = has = child.GetComponentInChildren<Graphic>() != null;
            return has;
        }

        /// <summary>
        /// Refresh the board (10 Hz from the board service). Text writes only on change; the symbol mesh is rebuilt only
        /// when something it draws changed.
        /// </summary>
        public void Present(HudMessage[] snapshot, int count, BoardFrame frame, ShotLedger shotLedger)
        {
            if (root == null) return;
            board = frame;
            shots = shotLedger;
            SystemStates systems = frame.Systems;
            if (nativeSafety != null) systems.WeaponSafe = nativeSafety.enabled;
            HudLamps.Fill(systems, lamps);
            HudThreats.Fill(frame.Threat, threatLamps);

            f = new Frame { Now = Time.unscaledTime, BootAge = frame.BootAge, U = unit };
            for (int i = 0; i < lamps.Length; i++) if (lamps[i].Present) lampOrder[f.LampCount++] = i;
            f.Count = Mathf.Min(count, cells.Length);
            f.Threats = f.LampCount > 0;
            f.Booting = f.Threats && LampTest.Running(f.BootAge);
            f.Caption = f.Threats ? LampTest.Caption(f.BootAge) : null;
            f.Top = f.Count > 0 ? snapshot[0] : default;
            f.Strip = f.Caption != null || (f.Count > 0 && (!string.IsNullOrEmpty(f.Top.Detail) || f.Top.Bar > 0f));
            f.FlashOn = (int)(f.Now * 5f) % 2 == 0;

            damageCount = HudDamage.Fill(board.Damage, damageLamps);
            var rows = new BoardRows
            {
                Systems = f.LampCount > 0, Threat = f.Threats, DetailRows = board.ContactCount, Cm = board.CmCount > 0,
                Damage = damageCount > 0, Fuel = board.Fuel.Valid, Cells = f.Count > 0, Strip = f.Strip, Ticker = board.HasTicker,
            };
            ContentBounds(out float contentLeft, out float contentRight, out float contentBottom);
            f.G = HudBoardLayout.Compute(rows, contentRight - contentLeft, unit);
            if (f.G.Height <= 0f)
            {
                Show(root, false);
                return;
            }
            Show(root, true);

            Vector2 rootPosition = new Vector2(contentRight - dock.rect.xMin, contentBottom - dock.rect.yMin);
            if (root.anchoredPosition != rootPosition) root.anchoredPosition = rootPosition;
            Vector2 rootSize = new Vector2(f.G.Width, f.G.Height);
            if (root.sizeDelta != rootSize) root.sizeDelta = rootSize;

            signature = (int)f.G.Width * 31 + f.Count * 7 + f.LampCount + (f.Strip ? 3 : 0) +
                (f.Booting ? (int)(f.BootAge * 20f) * 101 : 0) + shots.Version * 7919 + ThemeKey();
            beat = false;

            PresentSystems();
            PresentThreat();
            PresentDetail();
            PresentCm();
            PresentDamage();
            PresentFuel();
            PresentCells(snapshot);
            PresentStrip();
            PresentTicker();

            if (beat) signature = signature * 3 + (f.FlashOn ? 1 : 2);
            if (signature == lastSignature) return;
            lastSignature = signature;
            Paint(snapshot);
        }

        /// <summary>A lit lamp that flashes (state in transition, or a warning) needs the beat in the mesh key.</summary>
        private static bool Blinks(Lamp lamp) => lamp.State != LampState.Dark && (lamp.Flash || lamp.State == LampState.Warning);

        /// <summary>Changes when the player's HUD theme (ink, caution, alert) does, so the mesh recolours.</summary>
        private static int ThemeKey()
        {
            unchecked { return Ink.GetHashCode() * 31 + ThemeManager.Active.ColorTheme.Warning.GetHashCode() * 7 + Alert.GetHashCode(); }
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
            nativeCell = null;
            nativeSafety = null;
            board = null;
            shots = null;
            lastSignature = 0;
            graphicCache.Clear();
            DestroyRows();
        }
    }
}
