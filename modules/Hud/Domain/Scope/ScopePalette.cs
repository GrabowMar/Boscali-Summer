using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The scope's colour tokens, pure. Ink, caution (amber) and warning (red) are the player's HUD theme; hi and dim are
    /// derived from the ink (hi = ink toward white, dim = the ink at <see cref="DimAlpha"/>, furniture only), so a
    /// different HUD theme recolours the whole board. Faint is dim (LAYOUT.md 1: there is no fourth ink level; the name stays for the painters that still say it).
    /// Plate and bg are near-black: there is no plate to draw them on, so only text that sits on a chip and the shade use them.
    /// </summary>
    internal readonly struct ScopePalette
    {
        public static readonly Rgba PlateColor = new Rgba(0.043f, 0.110f, 0.157f), BgColor = new Rgba(0.020f, 0.051f, 0.078f);

        /// <summary>LAYOUT.md 1: DIM is the ink at 0.70 over whatever is behind (there is no plate); DIM text never drops under 0.89 of it (0.623 of the ink).</summary>
        public const float DimAlpha = 0.70f, DimTextFloor = 0.89f;

        private readonly Rgba ink, hi, dim, amber, red;

        public ScopePalette(Rgba ink, Rgba amber, Rgba red)
        {
            this.ink = ink.WithAlpha(1f);
            this.amber = amber.WithAlpha(1f);
            this.red = red.WithAlpha(1f);
            hi = Mix(this.ink, new Rgba(1f, 1f, 1f), 0.58f);
            dim = this.ink.WithAlpha(DimAlpha);
        }

        /// <summary>The token's colour at <paramref name="alpha"/> (a token that has an alpha of its own, dim, is multiplied by it).</summary>
        public Rgba Of(ScopeTone tone, float alpha = 1f)
        {
            Rgba c;
            switch (tone)
            {
                case ScopeTone.Hi: c = hi; break;
                case ScopeTone.Dim:
                case ScopeTone.Faint: c = dim; break;
                case ScopeTone.Amber: c = amber; break;
                case ScopeTone.Red: c = red; break;
                case ScopeTone.Plate: c = PlateColor; break;
                case ScopeTone.Bg: c = BgColor; break;
                default: c = ink; break;
            }
            return alpha >= 1f ? c : c.WithAlpha(c.A * (alpha < 0f ? 0f : alpha));
        }

        private static Rgba Mix(Rgba a, Rgba b, float k) => new Rgba(a.R + (b.R - a.R) * k, a.G + (b.G - a.G) * k, a.B + (b.B - a.B) * k);
    }
}
