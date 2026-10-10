using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>Draws one <see cref="GaugeSpec"/> into its 109 x 64 cell of the gauge row (the prototype's <c>drawGauge</c>).</summary>
    internal static class GaugePainter
    {
        public static void Draw(ScopeCanvas g, in GaugeSpec s, float x, float y, float w, float h, bool flashOn, float now)
        {
            ScopeTone tone = s.Tone;
            bool warn = tone == Amber || tone == Red, flash = s.Flash && flashOn;
            g.Box(x, y, w, h, warn ? tone : Dim, warn ? 1f : 0.8f, 1f, warn ? tone : Bg, warn ? (flash ? 0.4f : 0.14f) : 0.5f);
            g.Text(s.Label, x + 6f, y + 10f, warn ? tone : Dim);
            float v = ScopeCanvas.Clamp01(s.Value), cx = x + 24f, cy = y + 41f, r = 15f;
            ScopeTone fig = warn ? tone : Hi;
            switch (s.Kind)
            {
                case GaugeKind.Arc:
                    g.Arc(cx, cy, r, 225f, 135f, Faint, 1f, 4f);
                    if (v > 0.005f) g.Arc(cx, cy, r, 225f, 225f + 270f * v, tone, 1f, 4f);
                    ScopeCanvas.Polar(225f + 270f * v, r + 4f, out float ox, out float oy);
                    ScopeCanvas.Polar(225f + 270f * v, r - 4f, out float ix, out float iy);
                    g.Line(cx + ix, cy + iy, cx + ox, cy + oy, Hi);
                    if (s.Ready && (int)(now * 2f) % 2 == 0) g.Ring(cx, cy, r + 5f, Hi, 0.6f);
                    Figure(g, s, x + 46f, y + 33f, w - 49f, fig);
                    break;
                case GaugeKind.Ring:
                    g.Ring(cx, cy, r, Faint, 1f, 4f);
                    if (v > 0.005f) g.Arc(cx, cy, r, 0f, 360f * v - 0.01f, v >= 1f ? Hi : tone, 1f, 4f);
                    g.Text(s.Inner, cx, cy + 1f, Hi, 12f, ScopeAlign.Center, true);
                    Figure(g, s, x + 46f, y + 33f, w - 49f, fig);
                    break;
                case GaugeKind.Bars:
                    int n = (int)(v * 4f + 0.5f);
                    for (int i = 0; i < 4; i++)
                    {
                        float bh = 6f + i * 4f, bx = x + 8f + i * 8f;
                        if (i < n) g.Fill(bx, y + 56f - bh, 5f, bh, tone);
                        else g.Box(bx, y + 56f - bh, 5f, bh, Faint, 0.9f);
                    }
                    Figure(g, s, x + 46f, y + 33f, w - 49f, fig);
                    break;
                case GaugeKind.Needle:
                    g.Ring(cx, cy, r, Faint);
                    g.Line(cx, cy - r - 2f, cx, cy - r + 3f, Dim);
                    ScopeCanvas.Polar(s.Angle, r - 1f, out float tx, out float ty);
                    ScopeCanvas.Polar(s.Angle + 180f, 5f, out float bx2, out float by2);
                    g.Line(cx + bx2, cy + by2, cx + tx, cy + ty, tone, 1f, 2f);
                    g.Chevron(cx + tx, cy + ty, s.Angle, 4f, tone, true);
                    Figure(g, s, x + 46f, y + 33f, w - 49f, fig);
                    break;
                case GaugeKind.Bar:
                    Figure(g, s, x + 6f, y + 29f, w - 12f, fig);
                    g.Bar(x + 6f, y + 40f, w - 12f, 8f, v, tone);
                    for (int i = 0; s.Ticks != null && i < s.TickCount && i < s.Ticks.Length; i++) g.BarTick(x + 6f, y + 40f, w - 12f, 8f, s.Ticks[i], s.TickTone);
                    g.FitText(s.Sub, x + 6f, y + 56f, w - 12f, Dim);
                    break;
                default:    // Mode: the state word and a progress rail with a chevron riding it toward the diamond
                    g.FitText(s.Text, x + 6f, y + 30f, w - 12f, warn ? tone : Hi, 18f, 12f, ScopeAlign.Left, true);
                    float ry = y + 44f;
                    g.Dashed(x + 6f, ry, x + w - 22f, ry, Dim, 1f, 1f, -now * 10f);
                    g.Chevron(GaugeRow.RailX(x, w, v), ry, 90f, 4f, tone, true);
                    g.Diamond(x + w - 14f, ry, 4f, Hi);
                    g.FitText(s.Sub, x + 6f, y + 57f, w - 12f, Dim);
                    break;
            }
        }

        /// <summary>The figure (16 px bold) and the short word under it.</summary>
        private static void Figure(ScopeCanvas g, in GaugeSpec s, float x, float y, float maxW, ScopeTone tone)
        {
            g.FitText(s.Text, x, y, maxW, tone, 16f, 12f, ScopeAlign.Left, true);
            if (s.Kind != GaugeKind.Bar) g.FitText(s.Sub, x, y + 18f, maxW, Dim);
        }
    }
}
