using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The TAC BOARD (HUD v8, 2026-10-10) under the native weapons panel (<c>HUDCanvas/HMDCenter/TopRightPanel</c>): an opaque chamfered plate in
    /// the base game's language, sized from the native weapon cell (one design unit = that cell's height over 95) and right-aligned flush
    /// under the visible native block. Top to bottom: the lamp strip (the aircraft's switches, threat lamps at the right end), the scope
    /// (ONE page chosen by context, see <see cref="ScopePageBase"/>; the compact SYSTEMS strip in cruise), the gauge row (up to four
    /// <see cref="GaugeSpec"/>s) and the notice ticker. A row with nothing to say takes no room. Native's own child: never touches a
    /// native object. The class is partial: <c>StatusPanel.Board.cs</c> holds the update and the painters.
    /// </summary>
    internal sealed partial class StatusPanel
    {
        // Design units at native scale 1 (native weapon cell 95 tall).
        private const float NativeCellRef = 95f;
        private const int ShellQuads = 1800, PageQuads = 6000, OverlayQuads = 1200, FrameQuads = 300;

        private static readonly FieldInfo TopRightPanelField = AccessTools.Field(typeof(CombatHUD), "topRightPanel");
        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo NameTextField = AccessTools.Field(typeof(WeaponStatus), "nameText");
        private static readonly FieldInfo AmmoTextField = AccessTools.Field(typeof(WeaponStatus), "ammoText");
        private static readonly FieldInfo CellPanelField = AccessTools.Field(typeof(WeaponStatus), "panel");
        private static readonly FieldInfo SafetyField = AccessTools.Field(typeof(WeaponStatus), "safetyImage");

        private RectTransform dock;
        private Canvas rootCanvas;
        private RectTransform canvasRect;
        private RectTransform root;
        private Image plate;
        private RectTransform scopeRect, pageRect, overlayRect, frameRect;
        private AvVector shellVector, pageVector, overlayVector, frameVector;
        private ScopeText shellText, pageText, overlayText;
        private ScopeCanvas shellCanvas, pageCanvas, overlayCanvas, frameCanvas;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private RectTransform nativeCell;
        private Image nativeSafety;
        private float unit = 1f;

        private readonly Vector3[] corners = new Vector3[4];
        private readonly Dictionary<int, bool> graphicCache = new Dictionary<int, bool>();

        public bool Available => root != null;

        /// <summary>Where a page's events go (the board's ticker): set by the owner of the board.</summary>
        public Action<string, HudTone, string, string> Notice;

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
                weaponStatus != null ? CellPanelField?.GetValue(weaponStatus) as RectTransform : null,
                weaponStatus != null ? SafetyField?.GetValue(weaponStatus) as Image : null);
        }

        /// <summary>The native pieces the board is sized and fonted from are parameters, so an offline render can stand in for them.</summary>
        private void Build(RectTransform panelRect, TextMeshProUGUI nameLabel, RectTransform cell, Image safety)
        {
            dock = panelRect;
            Canvas canvas = dock.GetComponentInParent<Canvas>();
            rootCanvas = canvas != null ? canvas.rootCanvas : null;
            canvasRect = rootCanvas != null ? rootCanvas.transform as RectTransform : null;

            nativeCell = cell;
            nativeSafety = safety;
            font = nameLabel != null ? nameLabel.font : null;
            if (font == null) font = AvType.Face(AvFace.Mono);
            fontMaterial = nameLabel != null ? nameLabel.fontSharedMaterial : null;
            unit = MeasureUnit();

            var rootObject = new GameObject("BoscaliStatusPanel", typeof(RectTransform));
            root = (RectTransform)rootObject.transform;
            root.SetParent(dock, false);
            // Anchored to the dock's bottom-left; Present() right-aligns it to the visible screen edge.
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(1f, 1f);

            plate = HudSprites.Child(root, "Plate", HudSprites.Chamfer, sliced: true);
            plate.color = new Color(ScopePalette.PlateColor.R, ScopePalette.PlateColor.G, ScopePalette.PlateColor.B, 1f);
            shellVector = AvVector.Create(root, "Shell", ShellQuads);
            shellText = new ScopeText(Holder(root, "ShellWords"), font, fontMaterial);
            shellCanvas = new ScopeCanvas(shellVector.Buffer, shellText, "scope.shell");

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
            graphicCache.Clear();
        }

        /// <summary>Native weapon cell height over the design reference: how big one design unit is here.</summary>
        private float MeasureUnit()
        {
            if (nativeCell == null) return 1f;
            float h = Mathf.Abs(dock.InverseTransformVector(nativeCell.TransformVector(new Vector3(0f, nativeCell.rect.height, 0f))).y);
            return h > 10f ? h / NativeCellRef : 1f;
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
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            dock = null;
            rootCanvas = null;
            canvasRect = null;
            plate = null;
            scopeRect = pageRect = overlayRect = frameRect = null;
            shellVector = pageVector = overlayVector = frameVector = null;
            shellText = pageText = overlayText = null;
            shellCanvas = pageCanvas = overlayCanvas = frameCanvas = null;
            nativeCell = null;
            nativeSafety = null;
            board = null;
            graphicCache.Clear();
            DestroyBoard();
        }
    }
}
