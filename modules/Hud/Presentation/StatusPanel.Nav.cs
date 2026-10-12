using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The NAV DATA block (HUD holo pass C): the cockpit view's bottom-right corner. A captioned shelf (mirrored for the right side: node and stub at its left end) and right-aligned rows, each a DIM label and an INK / BRIGHT
    // value (HOME, TGT, CLS, FUEL with BINGO / JOKER, CLOCK; NavBlock holds the words, CornerLayout.Nav the box), over the same soft cloud the kill feed's corner has (baked once per height step, never per tick). Two roots
    // of its own under the HUD canvas, like the other corners: the cloud (first in the canvas, behind every native mark) and the ink (last, with a group whose alpha is the pilot's Opacity). Follows Shade, Declutter (Lean: no
    // shelf; Minimal: hidden), its own switch (ShowNavData) and the master switch (it is a block of the board, so the board off takes it too); the Corners option is the kill feed's and the map's. Text is written
    // only when its value changes (ScopeText). The cluster's target card owns this corner in third person, so the block is composed only in the cockpit view (BoardFrame.CockpitView).
    internal sealed partial class StatusPanel
    {
        private const int NavQuads = 48;
        /// <summary>The cloud is baked as if the block stood this far down the box (design units): the field's core falls with box y, and a block at the foot of the screen would otherwise get the thin end of it.</summary>
        private const float NavBakeTop = 40f;
        /// <summary>
        /// The block's cloud is the kill feed's, but it sits low on a screen whose bottom is often pale haze: under it a tight feathered patch (<see cref="HudSprites.Feather"/>, 44 units wider than the block all round) takes the
        /// ink the rest of the way, at <see cref="NavPatchAlpha"/> x the pilot's Shade (so Shade 0 draws neither, and the default 0.45 gives 0.4 at the core).
        /// </summary>
        private const float NavPatchMargin = 44f, NavPatchAlpha = 0.9f, NavPatchMax = 0.8f;

        private readonly NavBlock navBlock = new NavBlock();
        private int navRows;
        private RectTransform navShade, navFurniture, navSurface;
        private RawImage navCloud;
        private Image navPatch;
        private CanvasGroup navGroup;
        private Texture2D navTexture;
        private byte[] navBytes;
        private BoardGeometry navFor;
        private AvVector navVector;
        private ScopeText navText;
        private ScopeCanvas navCanvas;
        private Vector2 seenNavCanvas;
        /// <summary>The block as last laid out (design units from the screen's top-left; what the offline check reads).</summary>
        private CornerLayout.NavPlan navPlan;

        /// <summary>The 10 Hz present: compose this tick's rows (none outside the cockpit view, with the switch off, at Minimal or with the board off).</summary>
        private void PresentNav(BoardFrame frame) => navRows = Options.Enabled && allow.ShowNavData && frame.CockpitView ? navBlock.Compose(frame.Nav) : 0;

        /// <summary>Every frame (<paramref name="tick"/> false) and at the end of every present (true): draw the block again on a tick or when the canvas changed size; stand it down whenever there is nothing to show, the board is stale or the game's gate is shut.</summary>
        private void NavFrame(bool alive, bool tick)
        {
            if (!(alive && navRows > 0 && !cornersSuppressed && canvasRect != null)) { StandDownNav(); return; }
            Vector2 canvasSize = canvasRect.rect.size;
            if (!tick && canvasSize == seenNavCanvas && navFurniture != null && navFurniture.gameObject.activeSelf) return;
            seenNavCanvas = canvasSize;
            PaintNav();
        }

        /// <summary>Nothing of the block is up.</summary>
        private void StandDownNav()
        {
            if (navShade != null) Show(navShade, false);
            if (navFurniture != null) Show(navFurniture, false);
        }

        private bool EnsureNavTree()
        {
            if (navFurniture != null) return true;
            if (canvasRect == null) return false;
            navShade = CornerRoot("BoscaliNavShade");
            navShade.SetAsFirstSibling();
            navCloud = ShadeChild<RawImage>(navShade, "NavCloud");
            navPatch = ShadeChild<Image>(navShade, "NavPatch");
            HudSprites.Slice(navPatch, HudSprites.Feather);
            navFurniture = CornerRoot("BoscaliNavData");
            navGroup = navFurniture.gameObject.AddComponent<CanvasGroup>();
            navGroup.ignoreParentGroups = true;      // the pilot's opacity is this group's alone, as the board's is
            navGroup.interactable = navGroup.blocksRaycasts = false;
            navSurface = Pinned(navFurniture, "Nav", false);
            navVector = AvVector.Create(navSurface, "Strokes", NavQuads);
            navText = new ScopeText(Holder(navSurface, "Words"), font, fontMaterial);
            navCanvas = new ScopeCanvas(navVector.Buffer, navText, "scope.nav");
            PlaceCornerRoots();
            return true;
        }

        private void PaintNav()
        {
            if (!EnsureNavTree()) return;
            float k = 1f / baseUnit, canvasW = canvasRect.rect.width, canvasH = canvasRect.rect.height;
            navPlan = CornerLayout.Nav(canvasW * k, canvasH * k, navRows, allow.Furniture);
            CornerLayout.NavPlan plan = navPlan;
            float strength = Mathf.Clamp01(Options.Shade);
            bool shade = strength > 0.001f;
            Show(navShade, shade);
            if (shade) PaintNavShade(plan, strength);
            Show(navFurniture, true);
            if (navGroup.alpha != Options.Opacity) navGroup.alpha = Options.Opacity;
            PlaceOnGrid(navSurface, 0f, 0f, Whole(canvasW), Whole(canvasH));
            ScopeCanvas g = navCanvas;
            g.Furniture = allow.Furniture;
            g.Begin(Palette(), baseUnit, Whole(canvasH), pixel);
            g.CaptionShelfRight(plan.Left, plan.Top, CornerLayout.NavWidth, "NAV DATA");
            for (int i = 0; i < navBlock.Count; i++) DrawNavRow(g, navBlock.Rows[i], plan.Right, plan.RowY(i));
            g.End();
            navVector.Commit();
        }

        /// <summary>One row against the right edge: the flag (bold amber), the value in its tone, then the DIM label, a character's gap between them.</summary>
        private static void DrawNavRow(ScopeCanvas g, NavRow row, float right, float cy)
        {
            const float Gap = 7.8f;      // one character of the 12 unit mono face
            float x = right;
            if (row.Flag != null)
            {
                g.Text(row.Flag, x, cy, Amber, 12f, ScopeAlign.Right, true);
                x -= ScopeCanvas.TextW(row.Flag) + Gap;
            }
            var value = new ReadOnlySpan<char>(row.Value, 0, row.Length);
            g.Text(value, x, cy, row.Tone, 12f, ScopeAlign.Right);
            x -= ScopeCanvas.TextW(value) + Gap;
            g.Text(row.Label, x, cy, Dim, 12f, ScopeAlign.Right);
        }

        /// <summary>
        /// The block's cloud: the kill feed's field for one rectangle, baked over the block's rectangle (<see cref="CornerLayout.NavShade"/>: its height steps in 16s) as if it stood at <see cref="NavBakeTop"/>, then laid where the block is.
        /// Baked again only when the rectangle changes.
        /// </summary>
        private void PaintNavShade(in CornerLayout.NavPlan plan, float strength)
        {
            BoardRect r = CornerLayout.NavShade(plan);
            var geo = new BoardGeometry { Hero = new BoardRect(r.X, NavBakeTop, r.W, r.H) };
            BoardRect area = ShadeField.Area(geo);
            if (navTexture == null || !ShadeField.SameBlocks(navFor, geo))
            {
                BakeField(geo, area, ref navTexture, navCloud, ref navBytes);
                navFor = geo;
            }
            if (navCloud.texture != navTexture) navCloud.texture = navTexture;
            PlaceBoxAt(navCloud.rectTransform, new BoardRect(area.X, area.Y + r.Y - NavBakeTop, area.W, area.H), baseUnit);
            var cloud = new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, ScopeLayout.CloudAlpha * strength);
            if (navCloud.color != cloud) navCloud.color = cloud;
            Show(navCloud, true);
            // The tight patch behind the words: the block's box grown by the margin.
            PlaceBoxAt(navPatch.rectTransform, new BoardRect(plan.Left - NavPatchMargin, plan.Top - NavPatchMargin, CornerLayout.NavWidth + 2f * NavPatchMargin, plan.Height + 2f * NavPatchMargin), baseUnit);
            var patch = new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, Mathf.Min(NavPatchMax, NavPatchAlpha * strength));
            if (navPatch.color != patch) navPatch.color = patch;
            Show(navPatch, true);
        }

        private void DestroyNav()
        {
            if (navShade != null) UnityEngine.Object.Destroy(navShade.gameObject);
            if (navFurniture != null) UnityEngine.Object.Destroy(navFurniture.gameObject);
            if (navTexture != null) UnityEngine.Object.Destroy(navTexture);
            navShade = navFurniture = navSurface = null;
            navCloud = null;
            navPatch = null;
            navGroup = null;
            navTexture = null;
            navBytes = null;
            navFor = default;
            navVector = null;
            navText = null;
            navCanvas = null;
            navRows = 0;
            navPlan = default;
            seenNavCanvas = default;
        }
    }
}
