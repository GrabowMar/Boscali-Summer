using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The stores side of the board: the weapon (HERO), the capacitor (PWR BUS) and the countermeasures (EXPENDABLES), drawn by StoresHeader into their rectangles. What it has to say to the board: which of the three blocks
    // exist (BoardBlocks.Hero / Pwr / Exp from the header's flags), the surface the header paints on, the images that carry the game's own line art (a sprite cannot go through the stroke canvas), and which native boxes
    // it stands in for (TickNatives). Nothing here touches the wing side.
    internal sealed partial class StatusPanel
    {
        /// <summary>The native weapon panel's picture: its material is the one the game draws the weapon pictures with (additive; the pictures are white on an opaque black ground).</summary>
        private static readonly AccessTools.FieldRef<WeaponStatus, Image> WeaponImage = NativeRefs.Field<WeaponStatus, Image>("weaponImage");

        private readonly StoresHeader header = new StoresHeader();
        /// <summary>The header's last paint threw: its native boxes are shown again until a paint succeeds.</summary>
        private bool headerFault;
        private RawImage[] artImages;

        /// <summary>The 10 Hz read of the stores feed; the THREAT page's countermeasure call says which dispenser pulses DISPENSE.</summary>
        private void PollStores() =>
            header.Poll(now, CuePriority.Has(allow.Voiced, ScopePage.Threat) ? ((ThreatPage)pages[(int)ScopePage.Threat]).CallMask : (byte)0);

        /// <summary>The header's own faults are reported once (its id sits past the pages').</summary>
        private void ReportHeaderOnce(Exception e)
        {
            if (failed.Add(CuePriority.PageCount)) BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Hud.Scope.StoresHeader", e);
        }

        /// <summary>
        /// Take the material the native weapon panel draws its picture with, so the board draws the same pictures the same way (<see cref="StoreIcons.Material"/>). Without it (the panel or its image is gone) the board
        /// has no pictures and HERO draws its class glyph: never the sprite on the default material, which would paint its black ground.
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
            if (bottom <= 0f) { Show(storesRect, false); header.Clear(); return; }
            Show(storesRect, true);
            float height = Whole(bottom * unit);
            PlaceOnGrid(storesRect, 0f, 0f, Whole(geo.Width * unit), height);
            ScopeCanvas g = storesCanvas;
            g.Furniture = allow.Furniture;
            g.Begin(pal, unit, height, pixel);
            try
            {
                header.Paint(g, geo, Options.Layout == BoardLayout.Stack, (int)Options.Declutter, StoreIcons.Material != null, now, flashOn);
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
        /// Put the pictures the header asked for where it said (<see cref="StoresHeader.Art"/>): each a RawImage over the part of its sprite the header named (the sprite's ink, so the art fills its box), tinted through its
        /// colour, on the game's own material. Written only when they change; an unused slot is hidden.
        /// </summary>
        private void PlaceArt(in ScopePalette pal)
        {
            EnsureArt();
            for (int i = 0; i < artImages.Length; i++)
            {
                ArtSlot s = header.Art[i];
                Sprite sprite = s.Icon > 0 && s.Place.W > 0f && StoreIcons.Material != null ? StoreIcons.Get(s.Icon) : null;
                RawImage image = artImages[i];
                Show(image, sprite != null);
                if (sprite == null) continue;
                Texture texture = sprite.texture;
                Rect at = sprite.rect;      // the sprite as it was cut (the whole texture for the game's): the fractions are of that, whatever mesh Unity made for it
                float tw = texture.width, th = texture.height;
                // The slot's part is in fractions of the sprite, y down; a texture's v runs up.
                var uv = new Rect((at.x + s.Place.U0 * at.width) / tw, (at.y + (1f - s.Place.V1) * at.height) / th, (s.Place.U1 - s.Place.U0) * at.width / tw, (s.Place.V1 - s.Place.V0) * at.height / th);
                if (image.texture != texture) image.texture = texture;
                if (image.uvRect != uv) image.uvRect = uv;
                if (image.material != StoreIcons.Material) image.material = StoreIcons.Material;
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
            if (corner == null) return;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            bool alive = root != null && hasBoard && !stale && !headerFault && root.gameObject.activeInHierarchy;
            bool hudUp = hud != null && hud.iconLayer != null && hud.iconLayer.gameObject.activeInHierarchy, batch = Application.isBatchMode;
            corner.Apply(NativeTakeover.Hide(alive, true, hudUp, batch, header.WeaponDrawn), NativeTakeover.Hide(alive, true, hudUp, batch, header.CmDrawn), NativeTakeover.Hide(alive, true, hudUp, batch, header.CapDrawn));
        }
    }
}
