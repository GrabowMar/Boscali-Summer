using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The stores side of the board: the weapon (HERO), the capacitor (PWR BUS) and the countermeasures (EXPENDABLES), drawn by StoresHeader into their rectangles. What it has to say to the board: which of the three blocks
    // exist (BoardBlocks.Hero / Pwr / Exp from the header's flags), the surface the header paints on, the images that carry the game's own line art (a sprite cannot go through the stroke canvas: it is baked into the
    // mod's alpha mask, StoreIcons), and which native boxes it stands in for (TickNatives). Nothing here touches the wing side.
    internal sealed partial class StatusPanel
    {
        /// <summary>The native weapon panel's picture: its material is the one the game draws the weapon pictures with (additive; the pictures are white on an opaque black ground), the fallback for a picture that cannot be baked.</summary>
        private static readonly AccessTools.FieldRef<WeaponStatus, Image> WeaponImage = NativeRefs.Field<WeaponStatus, Image>("weaponImage");

        private readonly StoresHeader header = new StoresHeader();
        /// <summary>The header's last paint threw: its native boxes are shown again until a paint succeeds.</summary>
        private bool headerFault;
        private RawImage[] artImages;

        /// <summary>The 10 Hz read of the stores feed; the THREAT page's countermeasure call says which dispenser pulses DISPENSE. Also where a picture the header asked for is baked: one per tick (<see cref="StoreIcons.Bake"/>).</summary>
        private void PollStores()
        {
            StoreIcons.Bake();
            header.Poll(now, CuePriority.Has(allow.Voiced, ScopePage.Threat) ? ((ThreatPage)pages[(int)ScopePage.Threat]).CallMask : (byte)0);
        }

        /// <summary>Nothing of the header is drawn: forget what it held, and its fault with it (a fault is the last paint's, and there is no paint to be at fault while no stores block is drawn).</summary>
        private void ClearHeader()
        {
            header.Clear();
            headerFault = false;
        }

        /// <summary>The header's own faults are reported once (its id sits past the pages').</summary>
        private void ReportHeaderOnce(Exception e)
        {
            if (failed.Add(CuePriority.PageCount)) BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Hud.Scope.StoresHeader", e);
        }

        /// <summary>
        /// Take the material the native weapon panel draws its picture with (<see cref="StoreIcons.Material"/>): the fallback for a picture the board cannot bake into its own mask. Without it (the panel or its image is
        /// gone) such a picture is the class glyph: never the sprite on the default material, which would paint its black ground.
        /// </summary>
        private static void TakeArtMaterial(WeaponStatus weaponStatus)
        {
            Image image = NativeRefs.Get(WeaponImage, weaponStatus);
            Material material = image != null ? image.material : null;
            StoreIcons.Material = material != null && material != Canvas.GetDefaultCanvasMaterial() ? material : null;
        }

        /// <summary>
        /// The stores blocks on their own surface (board coordinates, from the top of the box, as far down as the lowest of them goes), each in the rectangle <see cref="ScopeLayout"/> gave it, then the images that carry
        /// the line art. A header that throws is reported once and its native boxes come back.
        /// </summary>
        private void PaintStores(in ScopePalette pal, in BoardGeometry geo, bool flashOn)
        {
            float bottom = Math.Max(geo.Hero.Present ? geo.Hero.Bottom : 0f, Math.Max(geo.Pwr.Present ? geo.Pwr.Bottom : 0f, geo.Exp.Present ? geo.Exp.Bottom : 0f));
            if (bottom <= 0f) { Show(storesRect, false); ClearHeader(); return; }
            Show(storesRect, true);
            float height = Whole(bottom * unit);
            PlaceOnGrid(storesRect, 0f, 0f, Whole(geo.Width * unit), height);
            ScopeCanvas g = storesCanvas;
            g.Furniture = allow.Furniture;
            g.Begin(pal, unit, height, pixel);
            try
            {
                header.Paint(g, geo, Options.Layout == BoardLayout.Stack, (int)Options.Declutter, now, flashOn);
                headerFault = false;
            }
            catch (Exception e)
            {
                ReportHeaderOnce(e);
                header.Clear();
                headerFault = true;
            }
            g.End();
            storesVector.Commit();
            PlaceArt(pal);
        }

        /// <summary>
        /// Put the pictures the header asked for where it said (<see cref="StoresHeader.Art"/>): each a RawImage over the part of its picture the header named (the picture's ink, so the art fills its box), tinted through its
        /// colour like the rest of the ink, on the ordinary UI material over the board's own mask; a picture that could not be baked is its sprite on the game's additive material instead. Written only when they change; an
        /// unused slot is hidden.
        /// </summary>
        private void PlaceArt(in ScopePalette pal)
        {
            EnsureArt();
            for (int i = 0; i < artImages.Length; i++)
            {
                ArtSlot s = header.Art[i];
                RawImage image = artImages[i];
                // The mask is exactly the sprite's part; the sprite itself is the whole texture the game cut it from (an atlas): the fractions are of the sprite's rect, whatever mesh Unity made for it.
                Sprite sprite = s.Mask || s.Icon <= 0 || s.Place.W <= 0f || StoreIcons.Material == null ? null : StoreIcons.Get(s.Icon);
                Texture texture = s.Mask && s.Place.W > 0f ? StoreIcons.MaskOf(s.Icon) : sprite != null ? sprite.texture : null;
                Show(image, texture != null);
                if (texture == null) continue;
                Rect at = sprite != null ? sprite.rect : new Rect(0f, 0f, texture.width, texture.height);
                float tw = texture.width, th = texture.height;
                // The slot's part is in fractions of the picture, y down; a texture's v runs up.
                var uv = new Rect((at.x + s.Place.U0 * at.width) / tw, (at.y + (1f - s.Place.V1) * at.height) / th, (s.Place.U1 - s.Place.U0) * at.width / tw, (s.Place.V1 - s.Place.V0) * at.height / th);
                if (image.texture != texture) image.texture = texture;
                if (image.uvRect != uv) image.uvRect = uv;
                if (s.Mask) image.material = null;
                else if (image.material != StoreIcons.Material) image.material = StoreIcons.Material;
                var c = pal.Of(s.Tone, s.Alpha);
                var color = new Color(c.R, c.G, c.B, c.A);
                if (image.color != color) image.color = color;
                PlaceOnGrid(image.rectTransform, s.Place.X * unit, s.Place.Y * unit, Whole(s.Place.W * unit), Whole(s.Place.H * unit));
            }
        }

        /// <summary>The images of the line art, children of the stores surface under its strokes and words (made on first use, and again if the board was rebuilt).</summary>
        private void EnsureArt()
        {
            if (artImages != null && artImages[0] != null && artImages[0].transform.parent == storesRect) return;
            artImages = new RawImage[StoresHeader.ArtSlots];
            for (int i = 0; i < artImages.Length; i++)
            {
                var go = new GameObject("Art" + i, typeof(RectTransform), typeof(RawImage));
                go.SetActive(false);
                var rect = (RectTransform)go.transform;
                rect.SetParent(storesRect, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.SetSiblingIndex(i);
                RawImage image = go.GetComponent<RawImage>();
                image.raycastTarget = false;
                artImages[i] = image;
            }
        }

        /// <summary>
        /// Every frame, after <see cref="Ensure"/>: hide the native weapon, countermeasure and capacitor boxes whose replacement the header is drawing, show the rest (<see cref="NativeCorner"/>,
        /// <see cref="NativeTakeover"/>). Fail open: no board, a hidden or inactive board, a header that threw, a hidden HUD or a batch run all give the native boxes back this frame; and a
        /// board that has stopped being presented (its update threw: what it shows is frozen) is taken off the screen, so the native boxes show live data in its place.
        /// </summary>
        public void TickNatives()
        {
            bool stale = NativeTakeover.Stale(Time.unscaledTime, presentedAt);
            if (stale && root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);      // Repaint shows it again when Present resumes
            // The other corners are the board's too: they follow its life (a stale or hidden board stands them down) and the board's gate (a pause, the maximised map, cinematic mode).
            cornersSuppressed = !CockpitOverlayGate.NotSuppressed();
            CornersFrame(root != null && hasBoard && !stale, false);
            if (corner == null) return;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            bool alive = Options.Enabled && Options.ShowStores && root != null && hasBoard && !stale && !headerFault && root.gameObject.activeInHierarchy;
            bool hudUp = hud != null && hud.iconLayer != null && hud.iconLayer.gameObject.activeInHierarchy, batch = Application.isBatchMode;
            corner.Apply(NativeTakeover.Hide(alive, true, hudUp, batch, header.WeaponDrawn), NativeTakeover.Hide(alive, true, hudUp, batch, header.CmDrawn), NativeTakeover.Hide(alive, true, hudUp, batch, header.CapDrawn));
        }
    }
}
