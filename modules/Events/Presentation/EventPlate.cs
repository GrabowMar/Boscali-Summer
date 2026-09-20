using UnityEngine;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// The no-poster plate: a generated diagonal-stripe film over the event's category mark.
    /// Every event will normally carry a poster PNG, so the missing-image state has to be a
    /// designed plate rather than a blank rectangle — the stripes say "plate, no exposure
    /// yet" and the mark says which category the story is. One 16x16 texture, generated once
    /// and tiled; nothing is bundled or downloaded.
    /// </summary>
    internal static class EventPlate
    {
        private const int Size = 16;
        private const int Period = 8;
        private const int Band = 3;

        private static Sprite stripes;

        public static Sprite Stripes()
        {
            if (stripes != null) return stripes;

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false)
            {
                name = "BoscaliEvents.Plate",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[Size * Size];
            Color32 ink = Color.white;
            Color32 clear = new Color32(255, 255, 255, 0);
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int band = (x + y) % Period;
                    pixels[y * Size + x] = band < Band ? ink : clear;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            stripes = Sprite.Create(texture, new Rect(0f, 0f, Size, Size),
                new Vector2(0.5f, 0.5f), 100f);
            stripes.name = "BoscaliEvents.PlateSprite";
            stripes.hideFlags = HideFlags.HideAndDontSave;
            return stripes;
        }
    }
}
