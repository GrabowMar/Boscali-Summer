using System;
using System.Globalization;
using NOAvionics;

namespace BoscaliSummer.Features.Progression.Runtime
{
    /// <summary>
    /// Local cosmetic squadron emblem: one silhouette, one centre charge and a two-colour
    /// palette. Engine-free so encoding, bounds and deterministic randomisation are covered
    /// by the pure test suite; the renderer lives in Presentation.
    /// </summary>
    internal readonly struct EmblemDesign : IEquatable<EmblemDesign>
    {
        public const int ShapeCount = 4;
        public const int ChargeCount = 5;
        public const int PaletteCount = 6;
        public const string DefaultText = "0.2.3";

        public static readonly string[] ShapeNames = { "SHIELD", "ROUNDEL", "DELTA", "BANNER" };
        public static readonly string[] ChargeNames = { "STAR", "BOLT", "WINGS", "CROSS", "CHEVRON" };

        public readonly byte Shape;
        public readonly byte Charge;
        public readonly byte Palette;

        public EmblemDesign(byte shape, byte charge, byte palette)
        {
            Shape = (byte)Wrap(shape, ShapeCount);
            Charge = (byte)Wrap(charge, ChargeCount);
            Palette = (byte)Wrap(palette, PaletteCount);
        }

        public static EmblemDesign Default => new EmblemDesign(0, 2, 3);

        public EmblemDesign Cycle(int shapeDelta, int chargeDelta, int paletteDelta) =>
            new EmblemDesign((byte)(Shape + shapeDelta), (byte)(Charge + chargeDelta),
                (byte)(Palette + paletteDelta));

        public string Encode() => Shape + "." + Charge + "." + Palette;

        public static bool TryParse(string text, out EmblemDesign design)
        {
            design = default;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split('.');
            if (parts.Length != 3) return false;
            if (!byte.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte shape) ||
                !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out byte charge) ||
                !byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out byte palette) ||
                shape >= ShapeCount || charge >= ChargeCount || palette >= PaletteCount) return false;
            design = new EmblemDesign(shape, charge, palette);
            return true;
        }

        public static EmblemDesign Parse(string text, EmblemDesign fallback) =>
            TryParse(text, out EmblemDesign design) ? design : fallback;

        /// <summary>Deterministic so a seed can be stored and reproduced across sessions.</summary>
        public static EmblemDesign Random(int seed)
        {
            var random = new Random(seed);
            return new EmblemDesign((byte)random.Next(ShapeCount), (byte)random.Next(ChargeCount),
                (byte)random.Next(PaletteCount));
        }

        /// <summary>Deterministic crest for an enemy squadron, derived from its wing identity.
        /// Shape and charge vary; the palette stays in the caution/danger band so a hostile
        /// crest never reads as the player's own emblem.</summary>
        public static EmblemDesign Hostile(string identity)
        {
            int seed = Seed(identity);
            var random = new Random(seed);
            return new EmblemDesign((byte)random.Next(ShapeCount), (byte)random.Next(ChargeCount),
                (byte)(2 + (seed & 1)));
        }

        /// <summary>FNV-1a over ordinal characters: stable across sessions and clients,
        /// unlike <see cref="string.GetHashCode"/>.</summary>
        private static int Seed(string identity)
        {
            uint hash = 2166136261u;
            if (!string.IsNullOrEmpty(identity))
                for (int i = 0; i < identity.Length; i++) hash = (hash ^ identity[i]) * 16777619u;
            return (int)(hash & 0x7FFFFFFF);
        }

        public static Rgba Primary(byte palette)
        {
            switch (palette % PaletteCount)
            {
                case 0: return AvTokens.RailReady;
                case 1: return AvTokens.RailInfo;
                case 2: return AvTokens.RailCaution;
                case 3: return AvTokens.RailDanger;
                case 4: return AvTokens.SurfaceRaised;
                default: return AvTokens.Frame;
            }
        }

        public static Rgba Secondary(byte palette)
        {
            switch (palette % PaletteCount)
            {
                case 0: return AvTokens.TextPrimary;
                case 1: return AvTokens.TextPrimary;
                case 2: return AvTokens.Ground;
                case 3: return AvTokens.TextPrimary;
                case 4: return AvTokens.RailReady;
                default: return AvTokens.RailCaution;
            }
        }

        private static int Wrap(int value, int count) => ((value % count) + count) % count;

        public bool Equals(EmblemDesign other) =>
            Shape == other.Shape && Charge == other.Charge && Palette == other.Palette;

        public override bool Equals(object obj) => obj is EmblemDesign other && Equals(other);

        public override int GetHashCode() => (Shape * 31 + Charge) * 31 + Palette;

        public static bool operator ==(EmblemDesign a, EmblemDesign b) => a.Equals(b);

        public static bool operator !=(EmblemDesign a, EmblemDesign b) => !a.Equals(b);
    }
}
