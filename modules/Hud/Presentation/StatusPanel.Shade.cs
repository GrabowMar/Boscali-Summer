using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The TAC BOARD's shade (LAYOUT.md 2): all that keeps the ink legible now there is no plate. A sun band hung from the top of the screen and one soft dark cloud behind each block, both from generated sprites
    // (HudSprites) tinted rgb(2, 12, 8), their alpha the pilot's Shade (0 switches the objects off). They are children of the board's root, so they follow its scale and offsets, but in a group of their own that
    // ignores the root's: the pilot's Opacity dims the ink and the line art, not the shade.
    internal sealed partial class StatusPanel
    {
        private static readonly Color ShadeColor = new Color(2f / 255f, 12f / 255f, 8f / 255f, 1f);
        private static readonly string[] CloudNames = { "HeroCloud", "PwrBusCloud", "ExpendablesCloud", "LampsCloud", "PageCloud", "GaugesCloud", "TickerCloud" };

        private RectTransform shadeRect;
        private Image shadeBand;
        private readonly Image[] shadeClouds = new Image[BoardGeometry.BlockCount];

        private void BuildShade()
        {
            shadeRect = Pinned(root, "Shade", false);
            CanvasGroup group = shadeRect.gameObject.AddComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.interactable = group.blocksRaycasts = false;
            shadeBand = ShadeImage("SunBand", HudSprites.SunBand(0f));
            for (int i = 0; i < shadeClouds.Length; i++) shadeClouds[i] = ShadeImage(CloudNames[i], HudSprites.ShadeCloud);
        }

        /// <summary>A top-left anchored, 9-sliced, input-transparent child of the shade whose size and place are set every paint (off until the first one: an image no one has placed would be a white square).</summary>
        private Image ShadeImage(string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.SetActive(false);
            var rect = (RectTransform)go.transform;
            rect.SetParent(shadeRect, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            return image;
        }

        /// <summary>
        /// Place and tint the shade for this paint, in board units (<paramref name="geo"/>) of the board's unit. A cloud is its block grown by <see cref="ScopeLayout.CloudGrow"/> on every side with its edges fading
        /// to nothing over that much (the sprite's 9-slice border, scaled to the unit by <c>pixelsPerUnitMultiplier</c>), its core alpha <see cref="ScopeLayout.CloudAlpha"/> x Shade. The band is
        /// <see cref="ScopeLayout.BandHeight"/> high and falls from <see cref="ScopeLayout.BandAlpha"/> x Shade at its top to nothing at its foot; it hangs from the top of the screen, or from the board's offset (which
        /// then also fades its top in), starts <see cref="ScopeLayout.BandRamp"/> left of the box with a ramp up, and runs to the screen's right edge (or to the board's anchor, with a fade, when the board is moved left).
        /// </summary>
        private void PaintShade(in BoardGeometry geo, float fromTop)
        {
            float strength = Mathf.Clamp01(Options.Shade);
            bool on = strength > 0.001f;
            Show(shadeRect, on);
            if (!on) return;

            float cut = 1f / unit;
            var cloud = new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, ScopeLayout.CloudAlpha * strength);
            for (int i = 0; i < shadeClouds.Length; i++)
            {
                BoardRect r = geo.Cloud(i);
                Image image = shadeClouds[i];
                Show(image, r.Present);
                if (!r.Present) continue;
                PlaceBox(image.rectTransform, r);
                if (image.pixelsPerUnitMultiplier != cut) image.pixelsPerUnitMultiplier = cut;
                if (image.color != cloud) image.color = cloud;
            }

            float margin = ScopeLayout.Margin * baseUnit / unit;      // board units: the screen's right edge is this far right of the box with no offset (the anchor, 8 base units in, when the board is moved)
            bool fadedTop = fromTop > (ScopeLayout.MarginTop + 0.5f) * baseUnit;
            Show(shadeBand, true);
            Sprite sprite = HudSprites.SunBand(fadedTop ? ScopeLayout.BandFadeTop / ScopeLayout.BandHeight : 0f);
            if (shadeBand.sprite != sprite) shadeBand.sprite = sprite;
            PlaceBox(shadeBand.rectTransform, new BoardRect(-ScopeLayout.BandRamp, -ScopeLayout.MarginTop * baseUnit / unit, geo.Width + margin + ScopeLayout.BandRamp * 2f, ScopeLayout.BandHeight));
            float ramp = HudSprites.BandRamp / (ScopeLayout.BandRamp * unit);
            if (shadeBand.pixelsPerUnitMultiplier != ramp) shadeBand.pixelsPerUnitMultiplier = ramp;
            var band = new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, ScopeLayout.BandAlpha * strength);
            if (shadeBand.color != band) shadeBand.color = band;
        }

        /// <summary>A surface placed over a rectangle of the box (board units): its top-left on the device pixel grid, its size in whole pixels.</summary>
        private void PlaceBox(RectTransform rect, in BoardRect r) => PlaceOnGrid(rect, r.X * unit, r.Y * unit, Whole(r.W * unit), Whole(r.H * unit));

        private void DestroyShade()
        {
            shadeRect = null;
            shadeBand = null;
            System.Array.Clear(shadeClouds, 0, shadeClouds.Length);
        }
    }
}
