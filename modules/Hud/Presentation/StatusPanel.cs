using System;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using HarmonyLib;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The TAC BOARD (HUD v8, 2026-10-10): an opaque chamfered plate that owns the whole top-right corner of the screen (8 px from the edges at 1080p), a child of the native
    /// <c>HUDCanvas/HMDCenter/TopRightPanel</c> so it follows the native HUD's visibility. Top to bottom: the stores header (weapon cell with its station rail, countermeasure cell, capacitor row:
    /// <see cref="StoresHeader"/>), the lamp strip (the aircraft's switches, threat lamps at the right end), the scope (ONE page chosen by context, see <see cref="ScopePageBase"/>; the compact
    /// SYSTEMS strip in cruise), the gauge row (up to four <see cref="GaugeSpec"/>s) and the notice ticker. A row with nothing to say takes no room. One design unit is a 1080p pixel scaled by
    /// the screen's height (never under 0.85). The board hides the native weapon, countermeasure and capacitor boxes it replaces through <see cref="NativeCorner"/> and nothing else native; every
    /// other Unity object it touches is its own. The class is partial: <c>StatusPanel.Board.cs</c> holds the update and the painters.
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
        private Image plate;
        private RectTransform scopeRect, pageRect, overlayRect, frameRect, storesRect;
        private AvVector shellVector, pageVector, overlayVector, frameVector, storesVector;
        private ScopeText shellText, pageText, overlayText, storesText;
        private ScopeCanvas shellCanvas, pageCanvas, overlayCanvas, frameCanvas, storesCanvas;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private Image nativeSafety;
        private NativeCorner corner;
        /// <summary>Canvas units per design unit, and where the screen's top-right corner (less the margin) is in dock-local units: both measured every update.</summary>
        private float unit = 1f, cornerX, cornerY;

        private readonly Vector3[] corners = new Vector3[4];

        public bool Available => root != null;

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
            // The native boxes the stores header replaces (a stand-in build for the offline check has none): hidden only while the header draws them.
            corner?.Dispose();
            corner = new NativeCorner();
            corner.Find(hud, panelRect, root);
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

            var rootObject = new GameObject("BoscaliStatusPanel", typeof(RectTransform));
            root = (RectTransform)rootObject.transform;
            root.SetParent(dock, false);
            // Anchored to the dock's bottom-left; Repaint() puts its top-right corner at the screen's.
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(1f, 1f);
            // The panel's own group (see NativeCorner) must never dim the board: it ignores every group above it.
            rootGroup = rootObject.AddComponent<CanvasGroup>();
            rootGroup.ignoreParentGroups = true;

            plate = HudSprites.Child(root, "Plate", HudSprites.Slab, sliced: true);
            plate.color = new Color(ScopePalette.PlateColor.R, ScopePalette.PlateColor.G, ScopePalette.PlateColor.B, 1f);
            shellVector = AvVector.Create(root, "Shell", ShellQuads);
            shellText = new ScopeText(Holder(root, "ShellWords"), font, fontMaterial);
            shellCanvas = new ScopeCanvas(shellVector.Buffer, shellText, "scope.shell");

            // The stores header: one surface over the top of the plate (board coordinates), above the shell, away from the scope.
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

            BuildBoard();
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
        /// The screen as the dock sees it: the board unit (one design unit is the screen's height over 1080 of a device pixel, never under <see cref="ScopeLayout.MinScale"/>) and the
        /// dock-local place of the screen's top-right corner, <see cref="ScopeLayout.Margin"/> in. Needs <see cref="pixel"/> measured first. The board owns the corner only while it stands
        /// in for what the native panel shows: with a native weapon box or capacitor bar up that the header does not draw (a header that threw, a station the feed could not read), it hangs
        /// under the native panel as it did before the takeover, so the box the pilot is given back is not under the board's plate.
        /// </summary>
        private void MeasureScreen()
        {
            float scale = 1f;
            cornerX = dock.rect.xMax;
            cornerY = dock.rect.yMax;
            if (canvasRect != null)
            {
                canvasRect.GetWorldCorners(corners);
                Vector3 topRight = dock.InverseTransformPoint(corners[2]), bottomRight = dock.InverseTransformPoint(corners[3]);
                cornerX = topRight.x;
                cornerY = topRight.y;
                scale = Mathf.Abs(topRight.y - bottomRight.y) / pixel / 1080f;
            }
            unit = Mathf.Max(scale, ScopeLayout.MinScale) * pixel;
            cornerX -= ScopeLayout.Margin * unit;
            cornerY -= ScopeLayout.Margin * unit;
            if (headerFault || (corner != null && corner.Uncovered(header.HasWeapon, header.HasCap))) cornerY = Mathf.Min(cornerY, dock.rect.yMin);
        }

        /// <summary>Take our board out of view without destroying it, and give the native boxes back. Idempotent.</summary>
        public void Hide()
        {
            corner?.Release();
            header.Clear();
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
            header.Clear();
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            rootGroup = null;
            dock = null;
            rootCanvas = null;
            canvasRect = null;
            plate = null;
            scopeRect = pageRect = overlayRect = frameRect = storesRect = null;
            shellVector = pageVector = overlayVector = frameVector = storesVector = null;
            shellText = pageText = overlayText = storesText = null;
            shellCanvas = pageCanvas = overlayCanvas = frameCanvas = storesCanvas = null;
            nativeSafety = null;
            board = null;
            DestroyBoard();
        }
    }
}
