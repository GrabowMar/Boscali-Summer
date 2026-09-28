using NOAvionics;

namespace BoscaliSummer.Features.Progression.Presentation
{
    /// <summary>
    /// Kit v2 icon lookup for SQD chrome (spec §5.4: per-module glyph classes that are chrome
    /// move to <see cref="AvIcon"/>). Replaces the old vector <c>SqdGlyph</c> mesh class; every
    /// call site now draws a Tabler glyph through <c>AvIcons</c> instead of a hand-drawn mark.
    /// The catalogue icon keys (<c>PerkDefinition.Icon</c>: fuel, combat, ground, objective,
    /// logistics, recon, fortify, strike, ew) each get the closest available Tabler glyph — the
    /// kit has no bespoke fuel/wrench/EW iconography, so this is a deliberate nearest-match.
    /// </summary>
    internal static class SqdIcons
    {
        public static AvIcon ForKey(string key)
        {
            switch (key)
            {
                case "fuel": return AvIcon.Gauge;
                case "ground": return AvIcon.Settings;
                case "objective": return AvIcon.Flag;
                case "logistics": return AvIcon.Stack2;
                case "recon": return AvIcon.Eye;
                case "fortify": return AvIcon.ShieldLock;
                case "strike": return AvIcon.Bolt;
                case "ew": return AvIcon.WaveSine;
                case "aircraft": return AvIcon.Plane;
                default: return AvIcon.Target; // "combat"
            }
        }
    }
}
