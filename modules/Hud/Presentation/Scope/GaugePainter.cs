using BoscaliSummer.Core.Contracts;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The glyph of a <see cref="GaugeSpec"/> on the gauge line (LAYOUT.md 3.6; the prototype's <c>gaugeGlyph</c>): a shape of radius <c>r</c> (6) about (cx, cy), strokes 2 units, the track DIM and the value in INK or
    /// BRIGHT (the gauge's own tone when it is a caution or a warning). arc: 270 degrees; ring: a full turn; needle: a bearing; bars: signal in quarters; bar: a 3 unit bar; mode: a dashed line with a chevron riding it.
    /// </summary>
    internal static class GaugePainter
    {
        public static void Glyph(ScopeCanvas g, in GaugeSpec s, float cx, float cy, float r, float alpha = 1f)
        {
            ScopeTone tone = s.Tone == Dim ? Ink : s.Tone, fig = tone == Amber || tone == Red ? tone : Hi;
            float v = ScopeCanvas.Clamp01(s.Value);
            switch (s.Kind)
            {
                case GaugeKind.Arc:
                    g.Arc(cx, cy, r, 225f, 135f, Dim, alpha, 2f);
                    if (v > 0.005f) g.Arc(cx, cy, r, 225f, 225f + 270f * v, fig, alpha, 2f);
                    break;
                case GaugeKind.Ring:
                    g.Ring(cx, cy, r, Dim, alpha, 2f);
                    if (v > 0.005f) g.Arc(cx, cy, r, 0f, 360f * v - 0.01f, v >= 1f ? Hi : tone, alpha, 2f);
                    break;
                case GaugeKind.Needle:
                    g.Ring(cx, cy, r, Dim, alpha);
                    ScopeCanvas.Polar(s.Angle, r - 1f, out float tx, out float ty);
                    ScopeCanvas.Polar(s.Angle + 180f, 2f, out float bx, out float by);
                    g.Line(cx + bx, cy + by, cx + tx, cy + ty, tone, alpha, 2f);
                    break;
                case GaugeKind.Bars:
                    int lit = (int)(v * 4f + 0.5f);
                    for (int i = 0; i < 4; i++)
                    {
                        float h = 3f + i * 3f, x = cx - r + 1f + i * 4f;
                        if (i < lit) g.Fill(x, cy + r - h, 3f, h, tone, alpha);
                        else g.Fill(x, cy + r - 1f, 3f, 1f, Dim, alpha);
                    }
                    break;
                case GaugeKind.Bar:
                    g.Fill(cx - r, cy - 1f, r * 2f, 3f, Dim, alpha);
                    g.Fill(cx - r, cy - 1f, (float)System.Math.Round(r * 2f * v), 3f, tone, alpha);
                    break;
                default:
                    g.Line(cx - r, cy, cx + r, cy, Dim, alpha, 1f, 3f, 2f);
                    g.Chevron(cx - r + r * 2f * (v * 3f % 1f), cy, 90f, 3f, tone, true, alpha);
                    break;
            }
        }
    }
}
