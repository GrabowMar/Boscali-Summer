using System;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using HarmonyLib;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The TAC BOARD (HUD v8, 2026-10-10; the visor layout, 2026-10-11): symbology with no plate, kept legible by a soft shade (<c>StatusPanel.Shade.cs</c>), anchored by its top-right corner near the top-right
    /// corner of the screen, a child of the native <c>HUDCanvas/HMDCenter/TopRightPanel</c> so it follows the native HUD's visibility. Two arrangements of the same blocks (<see cref="ScopeLayout"/>): WIDE, three
    /// columns (the stores side at the left: HERO weapon, PWR BUS, EXPENDABLES; the wing at the right: lamp line, the scope's page, gauge line, notice ticker), and the classic STACK column. A block with nothing to say
    /// takes no room. One design unit is a 1080p pixel scaled by the screen's height (never under 0.85) and the pilot's Scale. The board hides the native weapon, countermeasure and capacitor boxes it replaces through
    /// <see cref="NativeCorner"/> and nothing else native; every other Unity object it touches is its own. The class is partial: <c>StatusPanel.Board.cs</c> orchestrates (update, layout, placement),
    /// <c>StatusPanel.Stores.cs</c> is the stores side, <c>StatusPanel.Wing.cs</c> and <c>StatusPanel.Page.cs</c> the wing side, <c>StatusPanel.Shade.cs</c> the shade, <c>StatusPanel.Corners.cs</c> the kill feed's and the map's corners
    /// (the sway of the whole symbology is <see cref="ApplySway"/>, in <c>StatusPanel.Board.cs</c>).
    /// </summary>
    internal sealed partial class StatusPanel
    {
        private const int ShellQuads = 1800, PageQuads = 6000, OverlayQuads = 1200, FrameQuads = 300, StoresQuads = 3200;

        private static readonly FieldInfo TopRightPanelField = AccessTools.Field(typeof(CombatHUD), "topRightPanel");
        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo NameTextField = AccessTools.Field(typeof(WeaponStatus), "nameText");
        private static readonly FieldInfo SafetyField = AccessTools.Field(typeof(WeaponStatus), "safetyImage");

        private RectTransform dock;
        private Canvas rootCanvas;
        private RectTransform canvasRect;
        private RectTransform root;
        private CanvasGroup rootGroup;
        private RectTransform scopeRect, pageRect, overlayRect, frameRect, storesRect, statusRect;
        private AvVector shellVector, pageVector, overlayVector, frameVector, storesVector, statusVector;
        private ScopeText shellText, pageText, overlayText, storesText, statusText;
        private ScopeCanvas shellCanvas, pageCanvas, overlayCanvas, frameCanvas, storesCanvas, statusCanvas;
        private sealed class PageSurface
        {
            public RectTransform Scope, Body, Overlay, Frame;
            public AvVector BodyVector, OverlayVector, FrameVector;
            public ScopeCanvas BodyCanvas, OverlayCanvas, FrameCanvas;
        }
        private readonly PageSurface[] pageSurfaces = new PageSurface[ScopeLayout.MaxSlots];
        private TMP_FontAsset font;
        private Material fontMaterial;
        private Image nativeSafety;
        private NativeCorner corner;
        /// <summary>Canvas units per design unit: <see cref="baseUnit"/> is the screen's (what the margins and the offsets are in), <see cref="unit"/> that times the pilot's Scale (what the board itself is in).</summary>
        private float unit = 1f, baseUnit = 1f;
        /// <summary>The screen as the dock sees it, in dock-local units: its top-right corner and its size. Measured every update.</summary>
        private float screenRight, screenTop, screenW, screenH;
        /// <summary>Where the board's top-right corner is in dock-local units: worked out every repaint (it depends on the box's size).</summary>
        private float cornerX, cornerY;
        /// <summary>A native weapon box or capacitor bar is up that the header does not draw: the board hangs under the native panel (see <see cref="MeasureScreen"/>).</summary>
        private bool underNative;

        private readonly Vector3[] corners = new Vector3[4];

        /// <summary>Where a page's events go (the board's ticker): set by the owner of the board.</summary>
        public Action<string, HudTone, string, string> Notice;

        /// <summary>The pilot's options as of the last 10 Hz tick, set by the owner of the board before each <see cref="Present"/> (the defaults until then).</summary>
        public BoardOptions Options = BoardOptions.Default;

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
            WeaponStatus weaponStatus = WeaponStatusField?.GetValue(hud) as WeaponStatus;
            Build(panelRect,
                weaponStatus != null ? NameTextField?.GetValue(weaponStatus) as TextMeshProUGUI : null,
                weaponStatus != null ? SafetyField?.GetValue(weaponStatus) as Image : null);
            TakeArtMaterial(weaponStatus);
            // The native boxes the stores header replaces (a stand-in build for the offline check has none): hidden only while the header draws them.
            corner?.Dispose();
            corner = new NativeCorner();
            corner.Find(hud, panelRect, root);
            FindCorners();
        }

        /// <summary>The native pieces the board is fonted from are parameters, so an offline render can stand in for them.</summary>
        private void Build(RectTransform panelRect, TextMeshProUGUI nameLabel, Image safety)
        {
            dock = panelRect;
            Canvas canvas = dock.GetComponentInParent<Canvas>();
            rootCanvas = canvas != null ? canvas.rootCanvas : null;
            canvasRect = rootCanvas != null ? rootCanvas.transform as RectTransform : null;

            nativeSafety = safety;
            font = nameLabel != null ? nameLabel.font : null;
            if (font == null) font = AvType.Face(AvFace.Mono);
            fontMaterial = nameLabel != null ? nameLabel.fontSharedMaterial : null;
            BoscaliSummer.Core.Ui.LeftHudStack.Font = font;
            BoscaliSummer.Core.Ui.LeftHudStack.FontMaterial = fontMaterial;

            var rootObject = new GameObject("BoscaliStatusPanel", typeof(RectTransform));
            root = (RectTransform)rootObject.transform;
            root.SetParent(dock, false);
            // Anchored to the dock's bottom-left; Repaint() puts its top-right corner where the margins, the pilot's offsets and the screen put it.
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(1f, 1f);
            // The panel's own group (see NativeCorner) must never dim the board: it ignores every group above it.
            rootGroup = rootObject.AddComponent<CanvasGroup>();
            rootGroup.ignoreParentGroups = true;

            BuildShade();      // first: behind everything
            shellVector = AvVector.Create(root, "Shell", ShellQuads);
            shellText = new ScopeText(Holder(root, "ShellWords"), font, fontMaterial);
            shellCanvas = new ScopeCanvas(shellVector.Buffer, shellText, "scope.shell");

            // The stores header: one surface over the top of the board (board coordinates), above the shell, away from the scope.
            storesRect = Pinned(root, "Stores", false);
            storesVector = AvVector.Create(storesRect, "Strokes", StoresQuads);
            storesText = new ScopeText(Holder(storesRect, "Words"), font, fontMaterial);
            storesCanvas = new ScopeCanvas(storesVector.Buffer, storesText, "scope.stores");

            // The scope: a mask of its own (the page's strokes and words are clipped to it), the page body inside a second mask, then the header
            // and strip overlay, then the frame and the wipe edge on top of everything.
            scopeRect = Pinned(root, "Scope", true);
            pageRect = Pinned(scopeRect, "Page", true);
            pageVector = AvVector.Create(pageRect, "Strokes", PageQuads);
            pageText = new ScopeText(Holder(pageRect, "Words"), font, fontMaterial);
            pageCanvas = new ScopeCanvas(pageVector.Buffer, pageText, "scope.page");
            overlayRect = Pinned(scopeRect, "Overlay", false);
            overlayVector = AvVector.Create(overlayRect, "Strokes", OverlayQuads);
            overlayText = new ScopeText(Holder(overlayRect, "Words"), font, fontMaterial);
            overlayCanvas = new ScopeCanvas(overlayVector.Buffer, overlayText, "scope.overlay");
            frameRect = Pinned(scopeRect, "Frame", false);
            frameVector = AvVector.Create(frameRect, "Strokes", FrameQuads);
            frameCanvas = new ScopeCanvas(frameVector.Buffer, null, "scope.frame");
            pageSurfaces[0] = new PageSurface
            {
                Scope = scopeRect, Body = pageRect, Overlay = overlayRect, Frame = frameRect,
                BodyVector = pageVector, OverlayVector = overlayVector, FrameVector = frameVector,
                BodyCanvas = pageCanvas, OverlayCanvas = overlayCanvas, FrameCanvas = frameCanvas,
            };
            for (int i = 1; i < pageSurfaces.Length; i++) pageSurfaces[i] = BuildPageSurface(i);

            // STATUS: the aircraft's compact SYSTEMS strip under a warning page, so the RWR never takes the aircraft's state off the board. One masked surface: body, shelf and brackets.
            statusRect = Pinned(root, "Status", true);
            statusVector = AvVector.Create(statusRect, "Strokes", PageQuads);
            statusText = new ScopeText(Holder(statusRect, "Words"), font, fontMaterial);
            statusCanvas = new ScopeCanvas(statusVector.Buffer, statusText, "scope.status");

            BuildBoard();
        }

        private PageSurface BuildPageSurface(int slot)
        {
            var s = new PageSurface();
            s.Scope = Pinned(root, "Scope" + (slot + 1), true);
            s.Body = Pinned(s.Scope, "Page", true);
            s.BodyVector = AvVector.Create(s.Body, "Strokes", PageQuads);
            s.BodyCanvas = new ScopeCanvas(s.BodyVector.Buffer, new ScopeText(Holder(s.Body, "Words"), font, fontMaterial), "scope.page" + slot);
            s.Overlay = Pinned(s.Scope, "Overlay", false);
            s.OverlayVector = AvVector.Create(s.Overlay, "Strokes", OverlayQuads);
            s.OverlayCanvas = new ScopeCanvas(s.OverlayVector.Buffer, new ScopeText(Holder(s.Overlay, "Words"), font, fontMaterial), "scope.overlay" + slot);
            s.Frame = Pinned(s.Scope, "Frame", false);
            s.FrameVector = AvVector.Create(s.Frame, "Strokes", FrameQuads);
            s.FrameCanvas = new ScopeCanvas(s.FrameVector.Buffer, null, "scope.frame" + slot);
            Show(s.Scope, false);
            return s;
        }

        private static RectTransform Holder(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>A top-left anchored child whose size and place are set every paint, optionally masking what it holds.</summary>
        private static RectTransform Pinned(RectTransform parent, string name, bool mask)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            if (mask) go.AddComponent<RectMask2D>();
            return rect;
        }

        /// <summary>Place a surface: its top-left offset from its parent's top-left on the device pixel grid, its size already in whole pixels.</summary>
        private void PlaceOnGrid(RectTransform rect, float x, float y, float w, float h)
        {
            Vector2 position = new Vector2(Mathf.Round(x / pixel) * pixel, -Mathf.Round(y / pixel) * pixel), size = new Vector2(w, h);
            if (rect.anchoredPosition != position) rect.anchoredPosition = position;
            if (rect.sizeDelta != size) rect.sizeDelta = size;
        }

        private static void Show(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        /// <summary>
        /// The screen as the dock sees it: its top-right corner and size in dock-local units, and the board units (<see cref="baseUnit"/>: one design unit is the screen's height over 1080 of a device pixel, never
        /// under <see cref="ScopeLayout.MinScale"/>; <see cref="unit"/>: that times the pilot's Scale). Needs <see cref="pixel"/> measured first. Where the board's corner goes is <see cref="PlaceBoard"/>'s: it needs the
        /// box's size. The board owns the corner only while it stands in for what the native panel shows: with a native weapon box or capacitor bar up that the header does not draw (a header that threw, a station the
        /// feed could not read), it hangs under the native panel as it did before the takeover, so the box the pilot is given back is not under the board (<see cref="underNative"/>).
        /// </summary>
        private void MeasureScreen()
        {
            float scale = 1f;
            screenRight = dock.rect.xMax;
            screenTop = dock.rect.yMax;
            screenW = dock.rect.width;
            screenH = dock.rect.height;
            if (canvasRect != null)
            {
                canvasRect.GetWorldCorners(corners);
                Vector3 bottomLeft = dock.InverseTransformPoint(corners[0]), topRight = dock.InverseTransformPoint(corners[2]), bottomRight = dock.InverseTransformPoint(corners[3]);
                screenRight = topRight.x;
                screenTop = topRight.y;
                screenW = Mathf.Abs(topRight.x - bottomLeft.x);
                screenH = Mathf.Abs(topRight.y - bottomRight.y);
                scale = screenH / pixel / 1080f;
            }
            baseUnit = Mathf.Max(scale, ScopeLayout.MinScale) * pixel;
            unit = baseUnit * Mathf.Clamp(Options.Scale, BoardOptions.ScaleMin, BoardOptions.ScaleMax);
            // Restoring stores also restores native countermeasures, even on an airframe whose weapon/power boxes are inactive.
            underNative = !allow.ShowStores || headerFault || (corner != null && corner.Uncovered(header.HasWeapon, header.HasCap));
        }

        /// <summary>
        /// Where the board's top-right corner goes (<see cref="cornerX"/>, <see cref="cornerY"/>, dock-local): the margins from the screen's corner, the pilot's offsets, a box <paramref name="holdHeight"/> tall kept on the
        /// screen (<see cref="ScopeLayout.Anchor"/>). <paramref name="fromTop"/> is how far down from the screen's top the box ends up, in dock units (the shade's band hangs from there), also when the board hangs under the native panel.
        /// </summary>
        private void PlaceBoard(in BoardGeometry geo, float holdHeight, out float fromTop)
        {
            ScopeLayout.Anchor(screenW, screenH, baseUnit, unit, Options.OffsetX, Options.OffsetY, geo.Width, holdHeight, out float fromRight, out fromTop);
            boxFromRight = fromRight;
            cornerX = screenRight - fromRight;
            cornerY = screenTop - fromTop;
            if (underNative)
            {
                cornerY = Mathf.Min(cornerY, dock.rect.yMin);
                fromTop = screenTop - cornerY;
            }
        }

        /// <summary>Take our board out of view without destroying it, and give the native boxes back; the kill feed and the map stand down with it (the module is going away). Idempotent.</summary>
        public void Hide()
        {
            HideBoard();
            StandDownCorners();
            StandDownNav();
        }

        /// <summary>The board alone off the screen and the native boxes back, the other corners as they are: what the master switch does (the corners have a switch of their own, LAYOUT.md 8).</summary>
        private void HideBoard()
        {
            corner?.Release();
            BoardBox = default;
            ClearHeader();
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Drop every object this board created and give the native boxes back. Called on scene reset/teardown; also safe when the native dock
        /// already destroyed the tree for us (Unity fake-null).
        /// </summary>
        public void Destroy()
        {
            corner?.Dispose();
            corner = null;
            DestroyCorners();
            BoardBox = default;
            swayShift = rootBase = default;
            header.Clear();
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            rootGroup = null;
            dock = null;
            rootCanvas = null;
            canvasRect = null;
            scopeRect = pageRect = overlayRect = frameRect = storesRect = statusRect = null;
            shellVector = pageVector = overlayVector = frameVector = storesVector = statusVector = null;
            shellText = pageText = overlayText = storesText = statusText = null;
            shellCanvas = pageCanvas = overlayCanvas = frameCanvas = storesCanvas = statusCanvas = null;
            Array.Clear(pageSurfaces, 0, pageSurfaces.Length);
            nativeSafety = null;
            board = null;
            StoreIcons.Clear();      // the baked pictures are this scene's
            DestroyShade();
            DestroyBoard();
        }
    }
}
