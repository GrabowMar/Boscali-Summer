using System;
using System.Globalization;
using System.Text;
using NOAvionics;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Progression.Runtime
{
    /// <summary>One editable layer of an <see cref="EmblemDesign"/>, in encoding order.</summary>
    internal enum EmblemLayer : byte { Shape, Division, Charge, Accent, Field, Second, Ink, Number }

    /// <summary>How an emblem is painted: full tinctures, or the two-grey low-visibility scheme of a combat finish.</summary>
    internal enum EmblemFinish : byte { Colour, LowVis }

    /// <summary>Whether, and how, the local emblem is painted on the player's own tail fin.</summary>
    internal enum EmblemPaint : byte { Off, Colour, LowVis }

    /// <summary>
    /// Local cosmetic squadron insignia, built in heraldic layers: an outer shape, a field divided
    /// between two tinctures, a centre charge, a trim accent and an optional squadron number.
    /// Engine-free so encoding, bounds, contrast and deterministic rolls are covered by the pure
    /// test suite; <see cref="EmblemRaster"/> draws it and the Unity wrapper lives in Presentation.
    /// </summary>
    internal readonly struct EmblemDesign : IEquatable<EmblemDesign>
    {
        public const int ShapeCount = 11;
        public const int DivisionCount = 10;
        public const int ChargeCount = 48;
        public const int AccentCount = 5;
        public const int TinctureCount = 12;
        public const int NumberCount = 100;
        public const int LayerCount = 8;

        public const int AccentRing = 1, AccentStars = 2, AccentBars = 3, AccentLaurel = 4;

        public static readonly string[] ShapeNames =
            { "SHIELD", "ROUNDEL", "DELTA", "BANNER", "HEX", "DIAMOND", "PATCH", "ARCH", "ARROWHEAD", "SAWBLADE", "COFFIN" };
        public static readonly string[] DivisionNames =
            { "PLAIN", "PALE", "FESS", "BEND", "QUARTERS", "CHEVRON", "CHIEF", "BARS", "RAYS", "CHECKS" };
        public static readonly string[] ChargeNames =
        {
            "REACTOR ROSE", "CASH BAG", "NIGHT VIXEN", "REACTOR CAT", "KESTREL", "CROWN", "SIREN", "JACKPOT",
            "RADAR RABBIT", "CRATER MOON", "LIGHTNING BOLT", "NUCLEAR TREFOIL", "DECK QUEEN", "PLAYING CARDS", "LITTLE GREMLIN", "MANTA",
            "MOURNING WOLF", "RED WIDOW", "EMBER", "COMET GIRL", "CIRCUIT HEART", "DEAD RECKONING", "LOADED DICE", "FIREFLY",
            "KINGFISHER", "DIAMONDBACK", "HORSESHOE", "BROKEN CHAIN", "ORBIT OWL", "RED TIDE", "DEAD MANS HAND", "ATOMIC WARHEAD",
            "ALL IN CHIPS", "LUCKY SEVEN", "CHUD INTERCEPTOR", "BOMB HEART", "DECK RACCOON", "ACE PIGEON", "CROSSED DAGGERS", "TARGET LOCK",
            "ROCKET RIDER", "HEATWAVE", "DEVIL DUCK", "FUEL VAMPIRE", "HANGAR GOBLIN", "CINDER DRAGON", "GHOST FLIGHT", "MISSILE MOTH",
        };

        // Charges read without an article: cards, dice, chips, daggers.
        private static readonly int[] Plural = { 13, 22, 32, 38 };

        /// <summary>What an enemy wing may fly: the predators, the weapons and the omens, never the pin-ups or the jokes.</summary>
        private static readonly int[] HostileCharges = { 4, 5, 9, 10, 11, 14, 15, 16, 17, 21, 25, 28, 29, 30, 31, 38, 39, 45, 47 };

        private static readonly int[] AnyCharge = Sequence(ChargeCount);
        public static readonly string[] AccentNames = { "NONE", "RING", "STARS", "BARS", "LAUREL" };
        public static readonly string[] TinctureNames =
        {
            "WHITE", "GOLD", "RED", "BLUE", "GREEN", "BLACK", "ORANGE", "PURPLE", "SKY", "OLIVE", "STEEL", "MAROON",
        };

        // The blazon speaks heraldry; the steppers speak plain colour.
        private static readonly string[] TinctureBlazon =
        {
            "ARGENT", "OR", "GULES", "AZURE", "VERT", "SABLE", "TENNE", "PURPURE", "CELESTE", "OLIVE", "CENDREE", "SANGUINE",
        };

        private static readonly Rgba[] Tinctures =
        {
            new Rgba(0.93f, 0.93f, 0.90f), new Rgba(0.95f, 0.76f, 0.18f), new Rgba(0.72f, 0.10f, 0.10f),
            new Rgba(0.10f, 0.26f, 0.60f), new Rgba(0.10f, 0.42f, 0.22f), new Rgba(0.06f, 0.06f, 0.07f),
            new Rgba(0.88f, 0.42f, 0.08f), new Rgba(0.40f, 0.16f, 0.52f), new Rgba(0.36f, 0.62f, 0.86f),
            new Rgba(0.36f, 0.38f, 0.20f), new Rgba(0.42f, 0.46f, 0.50f), new Rgba(0.38f, 0.07f, 0.12f),
        };

        private static readonly int[] AnyTincture = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        /// <summary>The caution/danger band: a hostile crest never reads as the player's own.</summary>
        private static readonly int[] HostileField = { 2, 5, 11, 6 };
        private static readonly int[] HostileInk = { 0, 1, 5, 2, 6 };

        // Pre-layer configs stored shape.charge.palette; each old palette maps to a field and an ink.
        private const int LegacyShapeCount = 4, LegacyChargeCount = 5, LegacyPaletteCount = 6;
        private static readonly byte[] LegacyField = { 4, 3, 1, 2, 5, 5 };
        private static readonly byte[] LegacyInk = { 0, 0, 5, 0, 8, 1 };

        /// <summary>Luminance gap below which a charge stops reading against its field.</summary>
        public const float LegibleContrast = 0.32f;

        public readonly byte Shape;
        public readonly byte Division;
        public readonly byte Charge;
        public readonly byte Accent;
        public readonly byte Field;
        public readonly byte Second;
        public readonly byte Ink;
        public readonly byte Number;

        public EmblemDesign(int shape, int division, int charge, int accent, int field, int second, int ink, int number)
        {
            Shape = (byte)Wrap(shape, ShapeCount);
            Division = (byte)Wrap(division, DivisionCount);
            Charge = (byte)Wrap(charge, ChargeCount);
            Accent = (byte)Wrap(accent, AccentCount);
            Field = (byte)Wrap(field, TinctureCount);
            Second = (byte)Wrap(second, TinctureCount);
            Ink = (byte)Wrap(ink, TinctureCount);
            Number = (byte)Wrap(number, NumberCount);
        }

        /// <summary>Blue shield, a white kestrel with red in its wings, inside a ring.</summary>
        public static EmblemDesign Default => new EmblemDesign(0, 0, 4, AccentRing, 3, 2, 0, 0);

        public static readonly string DefaultText = Default.Encode();

        public static int Count(EmblemLayer layer)
        {
            switch (layer)
            {
                case EmblemLayer.Shape: return ShapeCount;
                case EmblemLayer.Division: return DivisionCount;
                case EmblemLayer.Charge: return ChargeCount;
                case EmblemLayer.Accent: return AccentCount;
                case EmblemLayer.Number: return NumberCount;
                default: return TinctureCount;
            }
        }

        public int Value(EmblemLayer layer)
        {
            switch (layer)
            {
                case EmblemLayer.Shape: return Shape;
                case EmblemLayer.Division: return Division;
                case EmblemLayer.Charge: return Charge;
                case EmblemLayer.Accent: return Accent;
                case EmblemLayer.Field: return Field;
                case EmblemLayer.Second: return Second;
                case EmblemLayer.Ink: return Ink;
                default: return Number;
            }
        }

        /// <summary>The same design with one layer stepped, wrapping at either end of its catalogue.</summary>
        public EmblemDesign With(EmblemLayer layer, int delta)
        {
            EmblemDesign d = this;
            int Step(EmblemLayer l) => d.Value(l) + (l == layer ? delta : 0);
            return new EmblemDesign(Step(EmblemLayer.Shape), Step(EmblemLayer.Division), Step(EmblemLayer.Charge),
                Step(EmblemLayer.Accent), Step(EmblemLayer.Field), Step(EmblemLayer.Second), Step(EmblemLayer.Ink),
                Step(EmblemLayer.Number));
        }

        /// <summary>What a stepper shows for one layer.</summary>
        public string Name(EmblemLayer layer)
        {
            switch (layer)
            {
                case EmblemLayer.Shape: return ShapeNames[Shape];
                case EmblemLayer.Division: return DivisionNames[Division];
                case EmblemLayer.Charge: return ChargeNames[Charge];
                case EmblemLayer.Accent: return AccentNames[Accent];
                case EmblemLayer.Field: return TinctureNames[Field];
                case EmblemLayer.Second: return TinctureNames[Second];
                case EmblemLayer.Ink: return TinctureNames[Ink];
                default: return Number == 0 ? "NONE" : "NO. " + Number.ToString(CultureInfo.InvariantCulture);
            }
        }

        public string Encode()
        {
            var text = new StringBuilder(24);
            for (int i = 0; i < LayerCount; i++)
            {
                if (i > 0) text.Append('.');
                text.Append(Value((EmblemLayer)i).ToString(CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        /// <summary>Accepts the eight-layer form and the retired shape.charge.palette form, so a
        /// config written before the layered generator keeps its emblem.</summary>
        public static bool TryParse(string text, out EmblemDesign design)
        {
            design = default;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split('.');
            if (parts.Length != LayerCount && parts.Length != 3) return false;
            var values = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i])) return false;

            if (parts.Length == 3)
            {
                if (values[0] >= LegacyShapeCount || values[1] >= LegacyChargeCount || values[2] >= LegacyPaletteCount)
                    return false;
                byte field = LegacyField[values[2]];
                design = new EmblemDesign(values[0], 0, values[1], 0, field, field, LegacyInk[values[2]], 0);
                return true;
            }
            for (int i = 0; i < LayerCount; i++)
                if (values[i] >= Count((EmblemLayer)i)) return false;
            design = new EmblemDesign(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]);
            return true;
        }

        public static EmblemDesign Parse(string text, EmblemDesign fallback) =>
            TryParse(text, out EmblemDesign design) ? design : fallback;

        /// <summary>Deterministic so a seed can be stored and reproduced across sessions. A roll
        /// always comes out legible: the charge contrasts with every tincture it can sit on.</summary>
        public static EmblemDesign Random(int seed) => Roll(new Random(seed), AnyTincture, AnyTincture, AnyCharge);

        /// <summary>Deterministic crest for an enemy squadron, derived from its wing identity.
        /// Every layer varies; the tinctures stay in the caution/danger band so a hostile crest
        /// never reads as the player's own emblem.</summary>
        public static EmblemDesign Hostile(string identity) =>
            Roll(new Random((int)(Deterministic.HashString(identity) & 0x7FFFFFFF)), HostileField, HostileInk, HostileCharges);

        public static bool IsHostileTincture(int tincture) => Array.IndexOf(HostileField, tincture) >= 0;

        public static bool IsHostileCharge(int charge) => Array.IndexOf(HostileCharges, charge) >= 0;

        private static int[] Sequence(int count)
        {
            var all = new int[count];
            for (int i = 0; i < count; i++) all[i] = i;
            return all;
        }

        private static EmblemDesign Roll(Random random, int[] fields, int[] inks, int[] charges)
        {
            int shape = random.Next(ShapeCount);
            // Plain fields carry a charge best, so they stay the most common roll.
            int division = random.Next(100) < 30 ? 0 : 1 + random.Next(DivisionCount - 1);
            int charge = charges[random.Next(charges.Length)];
            int accent = random.Next(100) < 35 ? 0 : 1 + random.Next(AccentCount - 1);
            int field = fields[random.Next(fields.Length)];
            int ink = Pick(random, inks, field, LegibleContrast, field, LegibleContrast);
            int second = Pick(random, fields, ink, LegibleContrast, field, 0.10f);
            // Some pairs leave no second tincture the ink can sit on; those rolls keep a plain field.
            if (Contrast(ink, second) < LegibleContrast) division = 0;
            int number = random.Next(100) < 40 ? 1 + random.Next(NumberCount - 1) : 0;
            return new EmblemDesign(shape, division, charge, accent, field, second, ink, number);
        }

        /// <summary>First tincture from a random start that stands at least <paramref name="gapA"/> from
        /// <paramref name="a"/> and <paramref name="gapB"/> from <paramref name="b"/>; when the set has none,
        /// the one that comes closest.</summary>
        private static int Pick(Random random, int[] set, int a, float gapA, int b, float gapB)
        {
            int start = random.Next(set.Length), best = set[start];
            float bestClear = float.MinValue;
            for (int i = 0; i < set.Length; i++)
            {
                int candidate = set[(start + i) % set.Length];
                float clear = Math.Min(Contrast(candidate, a) - gapA, Contrast(candidate, b) - gapB);
                if (clear >= 0f) return candidate;
                if (clear > bestClear) { bestClear = clear; best = candidate; }
            }
            return best;
        }

        /// <summary>True when the charge reads against every tincture of the field it sits on.</summary>
        public bool Legible => Contrast(Ink, Field) >= LegibleContrast &&
            (Division == 0 || Contrast(Ink, Second) >= LegibleContrast);

        public static float Contrast(int a, int b) => Math.Abs(Luminance(Tinctures[a]) - Luminance(Tinctures[b]));

        public static float Luminance(Rgba c) => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;

        /// <summary>A tincture as paint. Low-vis collapses every tincture onto one grey ramp, so
        /// the design survives as tone alone.</summary>
        public static Rgba Paint(int tincture, EmblemFinish finish)
        {
            Rgba c = Tinctures[Wrap(tincture, TinctureCount)];
            if (finish != EmblemFinish.LowVis) return c;
            float grey = 0.30f + 0.34f * Luminance(c);
            return new Rgba(grey, grey, grey);
        }

        /// <summary>The design read aloud the way a herald would: field first, then the charge.</summary>
        public string Blazon()
        {
            string f = TinctureBlazon[Field], s = TinctureBlazon[Second];
            var text = new StringBuilder(96);
            switch (Division)
            {
                case 1: text.Append("PER PALE ").Append(f).Append(" AND ").Append(s); break;
                case 2: text.Append("PER FESS ").Append(f).Append(" AND ").Append(s); break;
                case 3: text.Append("PER BEND ").Append(f).Append(" AND ").Append(s); break;
                case 4: text.Append("QUARTERLY ").Append(f).Append(" AND ").Append(s); break;
                case 5: text.Append("PER CHEVRON ").Append(f).Append(" AND ").Append(s); break;
                case 6: text.Append(f).Append(", A CHIEF ").Append(s); break;
                case 7: text.Append("BARRY ").Append(f).Append(" AND ").Append(s); break;
                case 8: text.Append("GYRONNY ").Append(f).Append(" AND ").Append(s); break;
                case 9: text.Append("CHEQUY ").Append(f).Append(" AND ").Append(s); break;
                default: text.Append(f); break;
            }
            string charge = ChargeNames[Charge];
            bool plural = Array.IndexOf(Plural, Charge) >= 0;
            text.Append(plural ? ", " : "AEIOU".IndexOf(charge[0]) >= 0 ? ", AN " : ", A ").Append(charge)
                .Append(' ').Append(TinctureBlazon[Ink]);
            switch (Accent)
            {
                case AccentRing: text.Append(" WITHIN AN ANNULET"); break;
                case AccentStars: text.Append(", IN CHIEF THREE MULLETS"); break;
                case AccentBars: text.Append(", IN BASE TWO BARS"); break;
                case AccentLaurel: text.Append(" WITHIN A WREATH"); break;
            }
            if (Number > 0) text.Append(" · NO. ").Append(Number.ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static int Wrap(int value, int count) => ((value % count) + count) % count;

        public bool Equals(EmblemDesign other) =>
            Shape == other.Shape && Division == other.Division && Charge == other.Charge && Accent == other.Accent &&
            Field == other.Field && Second == other.Second && Ink == other.Ink && Number == other.Number;

        public override bool Equals(object obj) => obj is EmblemDesign other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Shape;
                hash = hash * 31 + Division;
                hash = hash * 31 + Charge;
                hash = hash * 31 + Accent;
                hash = hash * 31 + Field;
                hash = hash * 31 + Second;
                hash = hash * 31 + Ink;
                return hash * 101 + Number;
            }
        }

        public static bool operator ==(EmblemDesign a, EmblemDesign b) => a.Equals(b);

        public static bool operator !=(EmblemDesign a, EmblemDesign b) => !a.Equals(b);
    }
}
