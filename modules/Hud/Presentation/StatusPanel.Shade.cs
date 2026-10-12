using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The TAC BOARD's shade (LAYOUT.md 2): all that keeps the ink legible now there is no plate. A sun band hung from the top of the screen (a generated sprite, HudSprites) and ONE cloud field behind all the blocks
    // (ShadeField, baked into a small texture of its own), both tinted rgb(2, 12, 8), their alpha the pilot's Shade (0 switches the objects off). They are children of the board's root, so they follow its scale and offsets,
    // but in a group of their own that ignores the root's: the pilot's Opacity dims the ink and the line art, not the shade.
    internal sealed partial class StatusPanel
    {
        private static readonly Color ShadeColor = new Color(2f / 255f, 12f / 255f, 8f / 255f, 1f);

        private RectTransform shadeRect;
        private Image shadeBand;
        private Vector2 shadeBandBase;
        private RawImage shadeField;
        private Texture2D fieldTexture;
        /// <summary>The blocks <see cref="fieldTexture"/> was last baked for: it is baked again only when they change (a block coming or going, the layout, the page's height once it has settled).</summary>
        private BoardGeometry fieldFor;
        /// <summary>The field's alpha as <see cref="ShadeField.Bake"/> writes it; grown when the area is, never per bake.</summary>
        private byte[] fieldBytes;

        private void BuildShade()
        {
            shadeRect = Pinned(root, "Shade", false);
            CanvasGroup group = shadeRect.gameObject.AddComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.interactable = group.blocksRaycasts = false;
            shadeBand = ShadeChild<Image>(shadeRect, "SunBand");
            shadeBand.sprite = HudSprites.SunBand(0f);
            shadeBand.type = Image.Type.Sliced;
            shadeField = ShadeChild<RawImage>(shadeRect, "CloudField");
        }

        /// <summary>A top-left anchored, input-transparent child of a shade (the board's, or the feed's) whose size and place are set every paint (off until the first one: a graphic no one has placed would be a white square).</summary>
        private static T ShadeChild<T>(RectTransform parent, string name) where T : MaskableGraphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(T));
            go.SetActive(false);
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            T graphic = go.GetComponent<T>();
            graphic.raycastTarget = false;
            return graphic;
        }

        /// <summary>
        /// Place and tint the shade for this paint, in board units (<paramref name="geo"/>) of the board's unit. The cloud field is one stretched texture over <see cref="ShadeField.Area"/>, its core alpha
        /// <see cref="ScopeLayout.CloudAlpha"/> x Shade. The band is <see cref="ScopeLayout.BandHeight"/> high and falls from <see cref="ScopeLayout.BandAlpha"/> x Shade at its top to nothing at its foot; it hangs from the top
        /// of the screen, or from the board's offset (which then also fades its top in), starts <see cref="ScopeLayout.BandRamp"/> left of the box with a ramp up, and runs to the screen's right edge (or to the board's
        /// anchor, with a fade, when the board is moved left).
        /// </summary>
        private void PaintShade(in BoardGeometry geo, float fromTop)
        {
            float strength = Mathf.Clamp01(Options.Shade);
            bool on = strength > 0.001f;
            Show(shadeRect, on);
            if (!on) return;

            BoardRect area = ShadeField.Area(geo);
            if (area.Present)
            {
                if (fieldTexture == null || !ShadeField.SameBlocks(fieldFor, geo))
                {
                    BakeField(geo, area, ref fieldTexture, shadeField, ref fieldBytes);
                    fieldFor = geo;
                }
                PlaceBox(shadeField.rectTransform, area);
                var cloud = new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, ScopeLayout.CloudAlpha * strength);
                if (shadeField.color != cloud) shadeField.color = cloud;
            }
            Show(shadeField, area.Present);

            float margin = ScopeLayout.Margin * baseUnit / unit;      // board units: the screen's right edge is this far right of the box with no offset (the anchor, 8 base units in, when the board is moved)
            bool fadedTop = fromTop > (ScopeLayout.MarginTop + 0.5f) * baseUnit;
            Show(shadeBand, true);
            Sprite sprite = HudSprites.SunBand(fadedTop ? ScopeLayout.BandFadeTop / ScopeLayout.BandHeight : 0f);
            if (shadeBand.sprite != sprite) shadeBand.sprite = sprite;
            PlaceBox(shadeBand.rectTransform, new BoardRect(-ScopeLayout.BandRamp, -ScopeLayout.MarginTop * baseUnit / unit, geo.Width + margin + ScopeLayout.BandRamp * 2f, ScopeLayout.BandHeight));
            shadeBandBase = shadeBand.rectTransform.anchoredPosition;      // the band stays hung from the top of the screen while the board sways
            shadeBand.rectTransform.anchoredPosition = shadeBandBase - new Vector2(0f, swayShift.y);
            float ramp = HudSprites.BandRamp / (ScopeLayout.BandRamp * unit);
            if (shadeBand.pixelsPerUnitMultiplier != ramp) shadeBand.pixelsPerUnitMultiplier = ramp;
            var band = new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, ScopeLayout.BandAlpha * strength);
            if (shadeBand.color != band) shadeBand.color = band;
        }

        /// <summary>
        /// Bake the cloud field over <paramref name="area"/> into <paramref name="texture"/> (the board's, or the feed's): white, the field as its alpha, a texel <see cref="ScopeLayout.CloudCell"/> box units across (bilinear
        /// when stretched), shown by <paramref name="image"/>. The texture is made once, resized when the area is and written in place: nothing is allocated per bake. Once per change of the blocks (<see cref="PaintShade"/>
        /// is given where the page is going, not each frame of its wipe), a few milliseconds for a live WIDE board.
        /// </summary>
        private static void BakeField(in BoardGeometry geo, in BoardRect area, ref Texture2D texture, RawImage image, ref byte[] bytes)
        {
            ShadeField.Texels(area, out int w, out int h);
            if (texture == null)
            {
                texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "BoscaliHud_CloudField", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                image.texture = texture;
            }
            else if (texture.width != w || texture.height != h) texture.Reinitialize(w, h);
            if (bytes == null || bytes.Length < w * h) bytes = new byte[w * h];
            ShadeField.Bake(geo, area, bytes);
            NativeArray<Color32> texels = texture.GetRawTextureData<Color32>();
            for (int j = 0; j < h; j++)      // texel row 0 is the bottom, the bake's the top
            {
                int from = (h - 1 - j) * w, to = j * w;
                for (int i = 0; i < w; i++) texels[to + i] = new Color32(255, 255, 255, bytes[from + i]);
            }
            texture.Apply(false, false);
        }

        /// <summary>A surface placed over a rectangle of the box (board units): its top-left on the device pixel grid, its size in whole pixels.</summary>
        private void PlaceBox(RectTransform rect, in BoardRect r) => PlaceBoxAt(rect, r, unit);

        /// <summary>A surface placed over a rectangle in design units of <paramref name="u"/> canvas units each.</summary>
        private void PlaceBoxAt(RectTransform rect, in BoardRect r, float u) => PlaceOnGrid(rect, r.X * u, r.Y * u, Whole(r.W * u), Whole(r.H * u));

        private void DestroyShade()
        {
            if (fieldTexture != null) UnityEngine.Object.Destroy(fieldTexture);
            fieldTexture = null;
            fieldBytes = null;
            fieldFor = default;
            shadeRect = null;
            shadeBand = null;
            shadeField = null;
        }
    }
}
