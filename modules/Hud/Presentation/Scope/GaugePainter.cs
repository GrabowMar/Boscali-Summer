using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The glyph of a <see cref="GaugeSpec"/> on the gauge line (LAYOUT.md 3.6; the prototype's <c>gaugeGlyph</c>): a shape of radius <c>r</c> (6) about (cx, cy), strokes 2 units, the track FAINT at 1 unit and the value in INK or
    /// BRIGHT (the gauge's own tone when it is a caution or a warning). arc: 270 degrees; ring: a full turn (with TickCount segments: a phase dial); needle: a bearing; bars: signal in quarters; bar: a 3 unit bar (notches at Ticks); mode: a dashed line with a chevron riding it.
    /// </summary>
    internal static class GaugePainter
    {
        public static void Glyph(ScopeCanvas g, in GaugeSpec s, float cx, float cy, float r, float alpha = 1f)
        {
            ScopeTone tone = s.Tone == Dim ? Ink : s.Tone, fig = tone == Amber || tone == Red ? tone : Hi;
            float v = Scalar.Clamp01(s.Value);
            switch (s.Kind)
            {
                case GaugeKind.Arc:
                    g.Arc(cx, cy, r, 225f, 135f, Faint, alpha, 1f);
                    if (v > 0.005f) g.Arc(cx, cy, r, 225f, 225f + 270f * v, fig, alpha, 2f);
                    break;
                case GaugeKind.Ring when s.TickCount > 1:
                    // a phase dial: TickCount equal segments from the top, clockwise, lit up to Value (the last lit one BRIGHT), the rest DIM
                    int segs = s.TickCount, litSeg = (int)System.Math.Round(v * segs);
                    for (int i = 0; i < segs; i++)
                    {
                        float a0 = i * 360f / segs + 9f, a1 = (i + 1) * 360f / segs - 9f;
                        g.Arc(cx, cy, r, a0, a1, i >= litSeg ? Faint : i == litSeg - 1 ? fig : tone, alpha, i >= litSeg ? 1f : 2f);
                    }
                    break;
                case GaugeKind.Ring:
                    g.Ring(cx, cy, r, Faint, alpha, 1f);
                    if (v > 0.005f) g.Arc(cx, cy, r, 0f, 360f * v - 0.01f, v >= 1f ? Hi : tone, alpha, 2f);
                    break;
                case GaugeKind.Needle:
                    g.Ring(cx, cy, r, Faint, alpha, 1f);
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
                        else g.Fill(x, cy + r - 1f, 3f, 1f, Faint, alpha);
                    }
                    break;
                case GaugeKind.Bar:
                    g.Bar(cx - r, cy - 1f, r * 2f, 3f, v, tone);
                    for (int i = 0; i < s.TickCount && s.Ticks != null && i < s.Ticks.Length; i++)      // notches: segment gaps / thresholds
                        g.Fill((float)System.Math.Round(cx - r + r * 2f * Scalar.Clamp01(s.Ticks[i])), cy - 1f, 1f, 3f, s.TickTone, alpha);
                    break;
                default:
                    g.Line(cx - r, cy, cx + r, cy, Faint, alpha, 1f, 3f, 2f);
                    g.Chevron(cx - r + r * 2f * (v * 3f % 1f), cy, 90f, 3f, tone, true, alpha);
                    break;
            }
        }
    }
}
