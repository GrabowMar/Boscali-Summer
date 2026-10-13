using System;
using System.Text;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The words of a cue, pure: the game's rich text read down to plain capitals, the class and severity of the common English messages the game shows on its grey boxes, and the seed of the decode. The game's own messages
    /// are the ones this was written from (<c>Weapon safety lock &lt;b&gt;Disabled&lt;/b&gt;</c>, <c>Radar Armed</c>, <c>Nav Lights &lt;b&gt;On&lt;/b&gt;</c>, <c>Tail Hook Deployed</c>, <c>Turrets set to hold fire</c>, a damage failure wrapped in the
    /// alert colour, <c>Rearmed X ... insufficient funds</c>); a message it does not know is SYS with its text kept.
    /// </summary>
    internal static class CueText
    {
        /// <summary>The message with every tag taken out (<c>&lt;b&gt;</c>, <c>&lt;color=...&gt;</c>), line breaks and runs of spaces as one space, trimmed, in capitals. Empty when nothing is left.</summary>
        public static string Plain(string rich)
        {
            if (string.IsNullOrEmpty(rich)) return "";
            var sb = new StringBuilder(rich.Length);
            bool tag = false, gap = false;
            for (int i = 0; i < rich.Length; i++)
            {
                char c = rich[i];
                if (tag) { if (c == '>') tag = false; continue; }
                if (c == '<') { tag = true; continue; }
                if (char.IsWhiteSpace(c)) { gap = sb.Length > 0; continue; }
                if (gap) { sb.Append(' '); gap = false; }
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>Whether the game coloured the message (its alert colour marks a failure): a <c>&lt;color</c> tag anywhere in it.</summary>
        public static bool Coloured(string rich) => rich != null && rich.IndexOf("<color", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The words of <paramref name="plain"/> (capitals) with the first letter of any of them <paramref name="stem"/>: "FAIL" is in FAILURE and FAILED, "ARMED" is not in DISARMED or REARMED.</summary>
        public static bool HasStem(string plain, string stem)
        {
            int at = 0;
            while ((at = plain.IndexOf(stem, at, StringComparison.Ordinal)) >= 0)
            {
                if (at == 0 || !char.IsLetterOrDigit(plain[at - 1])) return true;
                at++;
            }
            return false;
        }

        private static bool HasAny(string plain, params string[] stems)
        {
            for (int i = 0; i < stems.Length; i++) if (HasStem(plain, stems[i])) return true;
            return false;
        }

        /// <summary>The weapons are live: the safety off, a weapon armed, the turrets free to engage.</summary>
        public static bool IsArmed(string plain) =>
            plain.IndexOf("SAFETY LOCK DISABLED", StringComparison.Ordinal) >= 0 || plain.IndexOf("SAFETY OFF", StringComparison.Ordinal) >= 0 ||
            plain.IndexOf("ENGAGE AT WILL", StringComparison.Ordinal) >= 0 || HasStem(plain, "ARMED");

        private static bool IsFailure(string plain)
        {
            if (plain.IndexOf("HOLD FIRE", StringComparison.Ordinal) >= 0 || plain.IndexOf("CEASE FIRE", StringComparison.Ordinal) >= 0) return false;
            return HasAny(plain, "FAIL", "FIRE", "LEAK", "DAMAGE", "DESTROY", "DETACH", "LOST", "OVERHEAT", "STALL", "MALFUNCTION", "UNABLE", "JAMMED", "SEVERED", "INOPERATIVE");
        }

        private static bool IsMoney(string plain) =>
            plain.IndexOf('$') >= 0 || HasAny(plain, "FUNDS", "COST", "CREDIT", "SCORE", "ALLOCATION", "BONUS", "REWARD", "REFUND", "SORTIE") || IsScoreLine(plain);

        /// <summary>A score line the game writes: words and a plus sign and a number ("DETECTED UNITS +1,0", "SUCCESSFUL SORTIE + 3.5").</summary>
        private static bool IsScoreLine(string plain)
        {
            int plus = plain.LastIndexOf('+');
            if (plus < 0 || plus >= plain.Length - 1) return false;
            for (int i = plus + 1; i < plain.Length; i++)
            {
                char c = plain[i];
                if (c == ' ' && i == plus + 1) continue;
                return char.IsDigit(c);
            }
            return false;
        }

        /// <summary>
        /// The class word of a message and, in <paramref name="severity"/>, how loud it is (in: what the game's own colour says; out: raised by what the words say). The box a message came from decides first: a reserve notice is RSV, the
        /// funds line and the score line FUNDS. An action report: a failure word (or the alert colour) is DMG, money and score words FUNDS, then radar / jammer / gear / assist words stay SYS, then weapon words are WPN, and anything
        /// else SYS. A failure without the alert colour is a caution; weapons ARMED (the safety off) is a caution; SAFE and a disarm are not.
        /// </summary>
        public static CueClass Classify(string plain, CueSource source, ref CueSeverity severity)
        {
            if (source == CueSource.Reserve) return CueClass.Rsv;
            if (source == CueSource.Allocation || source == CueSource.Score) return CueClass.Funds;
            if (severity == CueSeverity.Alert || IsFailure(plain))
            {
                if (severity == CueSeverity.Normal) severity = CueSeverity.Caution;
                return CueClass.Dmg;
            }
            if (HasAny(plain, "INSUFFICIENT") && severity == CueSeverity.Normal) severity = CueSeverity.Caution;
            if (IsMoney(plain)) return CueClass.Funds;
            if (HasAny(plain, "RADAR", "JAMMER", "JAMMING", "ECM", "GEAR", "ASSIST", "AUTOPILOT", "HOVER", "LIGHTS", "HOOK", "FLAPS")) return CueClass.Sys;
            if (HasAny(plain, "WEAPON", "SAFETY", "ARMED", "DISARMED", "ARMING", "REARM", "GUN", "TURRET", "MISSILE", "BOMB", "ROCKET", "AMMO", "CHAFF", "FLARE", "FIRE"))
            {
                if (severity == CueSeverity.Normal && IsArmed(plain)) severity = CueSeverity.Caution;
                return CueClass.Wpn;
            }
            return CueClass.Sys;
        }

        /// <summary>A stable number from the words (FNV-1a): what the decode's glyphs are seeded from, so the same message scrambles the same way in every render.</summary>
        public static int Seed(string text)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < text.Length; i++) h = (h ^ text[i]) * 16777619u;
                return (int)h;
            }
        }
    }

    /// <summary>
    /// How a cue's words fit the stack, pure. The stack is 420 design units wide at most; the mono face advances 0.65 em a character, so 20 units holds 32 characters and 16 holds 40. A message too long for 20 drops to 16; one too
    /// long for 16 loses whole words from its end (never dots, never half a word unless it is one word). A message that is cut ends on a word, not on a dash or a comma.
    /// </summary>
    internal static class CueFit
    {
        public const float MaxWidth = 420f, BigSize = 20f, SmallSize = 16f, Advance = 0.65f;
        public static readonly int BigChars = (int)(MaxWidth / (Advance * BigSize)), SmallChars = (int)(MaxWidth / (Advance * SmallSize));

        public static string Fit(string plain, out float size)
        {
            if (plain.Length <= BigChars) { size = BigSize; return plain; }
            size = SmallSize;
            if (plain.Length <= SmallChars) return plain;
            int cut = SmallChars;
            while (cut > 0 && plain[cut] != ' ') cut--;      // plain[cut] is the first character that does not fit: a space there means the words before it do
            if (cut == 0) return plain.Substring(0, SmallChars);      // one word as long as the line: cut it
            return plain.Substring(0, cut).TrimEnd(' ', '-', ',', ':', ';', '(', '/');
        }
    }
}
