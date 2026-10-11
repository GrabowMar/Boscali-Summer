using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Where the ink of a picture lives inside its sprite, as fractions of the sprite: left, top, right, bottom, y down. <see cref="StoreArtBounds"/> knows the vanilla pictures'.</summary>
    internal readonly struct ArtBounds
    {
        public readonly float X0, Y0, X1, Y1;

        public ArtBounds(float x0, float y0, float x1, float y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }

        /// <summary>The whole sprite: what a picture the table does not know is drawn with.</summary>
        public static ArtBounds Whole => new ArtBounds(0f, 0f, 1f, 1f);
    }

    /// <summary>
    /// A picture placed in a box (board units) and the part of its sprite it shows (fractions of the sprite, y down): what the board hands to a RawImage, whose uvRect is the part and whose rectangle is the place.
    /// The sprites' textures cannot be read at run time, so the board never looks at their pixels: it draws only the box the ink lives in (<see cref="ArtBounds"/>) and so the art fills its box.
    /// </summary>
    internal readonly struct ArtPlace
    {
        public readonly float X, Y, W, H;
        public readonly float U0, V0, U1, V1;

        public ArtPlace(float x, float y, float w, float h, float u0, float v0, float u1, float v1) { X = x; Y = y; W = w; H = h; U0 = u0; V0 = v0; U1 = u1; V1 = v1; }

        /// <summary>The part between <paramref name="from"/> and <paramref name="to"/> (0..1) of the picture's width, both the rectangle and the part of the sprite (a magazine's spent share is the right of it).</summary>
        public ArtPlace Slice(float from, float to)
        {
            from = from < 0f ? 0f : from > 1f ? 1f : from;
            to = to < from ? from : to > 1f ? 1f : to;
            return new ArtPlace(X + W * from, Y, W * (to - from), H, U0 + (U1 - U0) * from, V0, U0 + (U1 - U0) * to, V1);
        }
    }

    internal static class StoreArt
    {
        /// <summary>
        /// The ink of a sprite <paramref name="spriteW"/> x <paramref name="spriteH"/> pixels (the part <paramref name="ink"/> of it) fitted in the box (<paramref name="x"/>, <paramref name="y"/>, <paramref name="w"/> x <paramref name="h"/>):
        /// as large as the box allows with its aspect kept, centred. A sprite with no size or no ink in it, or an empty box, places nothing (<c>W</c> is 0).
        /// </summary>
        public static ArtPlace Fit(in ArtBounds ink, float spriteW, float spriteH, float x, float y, float w, float h)
        {
            float iw = (ink.X1 - ink.X0) * spriteW, ih = (ink.Y1 - ink.Y0) * spriteH;
            if (!(iw > 0f && ih > 0f && w > 0f && h > 0f)) return default;
            float k = Math.Min(w / iw, h / ih), dw = iw * k, dh = ih * k;
            return new ArtPlace(x + (w - dw) / 2f, y + (h - dh) / 2f, dw, dh, ink.X0, ink.Y0, ink.X1, ink.Y1);
        }
    }
}
