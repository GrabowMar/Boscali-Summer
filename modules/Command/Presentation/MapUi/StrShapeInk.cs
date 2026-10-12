using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The glyph shapes of the STR overlay as ink: the one drawer behind the map's marks and the legend's swatches, so the key can never show a
    /// shape the map does not draw. Lengths are canvas pixels times <c>px</c> (map-local units per canvas pixel); a non-finite input adds nothing.
    /// </summary>
    internal static class StrShapeInk
    {
        /// <summary>Half the width of a glyph in canvas pixels, and the stroke width.</summary>
        internal const float GlyphRadius = 7f, StrokeWidth = 1.5f;

        /// <summary>
        /// A glyph centred on (cx, cy): cross, hollow or solid diamond, hex, box, or the lane notch (legend only). A solid diamond carries a dark
        /// under-plate so it reads over any terrain. Returns false once the buffer is full.
        /// </summary>
        internal static bool Glyph(AvQuadBuffer b, StrShape shape, float cx, float cy, float px, Rgba ink, Rgba under)
        {
            float r = GlyphRadius * px, w = StrokeWidth * px;
            switch (shape)
            {
                case StrShape.Cross: return AvStrokes.Cross(b, cx, cy, r * 0.85f, w, ink);
                case StrShape.Diamond: return AvStrokes.Diamond(b, cx, cy, r, w, ink);
                case StrShape.DiamondSolid: return Plate(b, cx, cy, r + 1.5f * px, under) & Plate(b, cx, cy, r, ink);
                case StrShape.Hex: return StrStrokes.Hex(b, cx, cy, r * 1.3f, w, ink);
                case StrShape.Box:
                {
                    float hw = r * 1.2f, hh = r * 0.8f;
                    return AvStrokes.Line(b, cx - hw, cy - hh, cx + hw, cy - hh, w, ink) & AvStrokes.Line(b, cx + hw, cy - hh, cx + hw, cy + hh, w, ink) &
                           AvStrokes.Line(b, cx + hw, cy + hh, cx - hw, cy + hh, w, ink) & AvStrokes.Line(b, cx - hw, cy + hh, cx - hw, cy - hh, w, ink);
                }
                case StrShape.Notch:
                    return AvStrokes.Line(b, cx, cy - r, cx, cy + r * 0.6f, w * 1.6f, ink) &
                           b.Add(cx, cy + r * 0.6f, cx + r * 0.5f, cy + r * 1.3f, cx - r * 0.5f, cy + r * 1.3f, cx, cy + r * 0.6f, ink, ink, ink, ink);
                default: return true;
            }
        }

        private static bool Plate(AvQuadBuffer b, float cx, float cy, float r, Rgba c) =>
            b.Add(cx - r, cy, cx, cy + r, cx + r, cy, cx, cy - r, c, c, c, c);
    }
}
