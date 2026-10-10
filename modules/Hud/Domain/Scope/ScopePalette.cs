using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The scope's eight colour tokens, pure. Ink, caution (amber) and warning (red) are the player's HUD theme; hi, dim and faint are
    /// derived from the ink the way the prototype's greens relate (hi = ink toward white, dim and faint = ink over the plate), so a
    /// different HUD theme recolours the whole board. Plate and bg are the board's near-black glass.
    /// </summary>
    internal readonly struct ScopePalette
    {
        public static readonly Rgba PlateColor = new Rgba(0.043f, 0.110f, 0.157f), BgColor = new Rgba(0.020f, 0.051f, 0.078f);

        private readonly Rgba ink, hi, dim, faint, amber, red;

        public ScopePalette(Rgba ink, Rgba amber, Rgba red)
        {
            this.ink = ink.WithAlpha(1f);
            this.amber = amber.WithAlpha(1f);
            this.red = red.WithAlpha(1f);
            hi = Mix(this.ink, new Rgba(1f, 1f, 1f), 0.58f);
            dim = Mix(PlateColor, this.ink, 0.68f);
            faint = Mix(PlateColor, this.ink, 0.20f);
        }

        /// <summary>The token's colour at <paramref name="alpha"/>.</summary>
        public Rgba Of(ScopeTone tone, float alpha = 1f)
        {
            Rgba c;
            switch (tone)
            {
                case ScopeTone.Hi: c = hi; break;
                case ScopeTone.Dim: c = dim; break;
                case ScopeTone.Faint: c = faint; break;
                case ScopeTone.Amber: c = amber; break;
                case ScopeTone.Red: c = red; break;
                case ScopeTone.Plate: c = PlateColor; break;
                case ScopeTone.Bg: c = BgColor; break;
                default: c = ink; break;
            }
            return alpha >= 1f ? c : c.WithAlpha(alpha < 0f ? 0f : alpha);
        }

        private static Rgba Mix(Rgba a, Rgba b, float k) => new Rgba(a.R + (b.R - a.R) * k, a.G + (b.G - a.G) * k, a.B + (b.B - a.B) * k);
    }
}
