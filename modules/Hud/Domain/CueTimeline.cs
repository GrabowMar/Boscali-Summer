using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    internal enum CuePhase : byte { Show, Hold, Fade, Gone }

    /// <summary>
    /// The life of one cue as a function of its age, pure (so a render at any frame rate, or at a fixed age, draws the same). SHOW (0 to 0.32 s): the two bracket halves snap in from <see cref="BracketRun"/> outside over 0.14 s, the
    /// message decodes, the underline draws, the whole cue is a tone brighter for the first 0.12 s. HOLD: steady until the game's own time is up (at least <see cref="CueLog.MinLife"/>). FADE (the last 0.35 s): the underline retracts,
    /// the brackets spread out again, and the words break up in five equal steps of alpha (<see cref="Steps"/>), a flicker and not a smooth fade.
    /// </summary>
    internal static class CueTimeline
    {
        public const float ShowSeconds = 0.32f, SnapSeconds = 0.14f, FlashSeconds = 0.12f, FadeSeconds = 0.35f, SlideSeconds = 0.12f;
        /// <summary>Cues accepted within this many seconds of the one before are one burst, and a burst's cues start their SHOW <see cref="StaggerSeconds"/> apart, one after another, so they do not draw over each other.</summary>
        public const float GroupSeconds = 0.05f, StaggerSeconds = 0.09f;
        /// <summary>How far outside its resting place each bracket half starts (and ends) in design units.</summary>
        public const float BracketRun = 46f;
        /// <summary>About 0.025 s a character to lock, never over 0.3 s for the whole message.</summary>
        public const float CharSeconds = 0.025f, DecodeSeconds = 0.3f;
        /// <summary>The alpha of the five equal steps of the fade (<see cref="FadeSeconds"/> / 5 each).</summary>
        public static readonly float[] Steps = { 1f, 0.55f, 0.85f, 0.3f, 0f };

        /// <summary>The cue's life in seconds: the game's own time, at least <see cref="CueLog.MinLife"/> so a very short line is still readable. A time that is not a number, or absurd, is five seconds.</summary>
        public static float LifeOf(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) seconds = CueLog.DefaultSeconds;
            return Math.Min(CueLog.MaxSeconds, Math.Max(CueLog.MinLife, seconds));
        }

        /// <summary>
        /// When a cue's SHOW starts: the instant it arrives, unless it arrives within <see cref="GroupSeconds"/> of the cue before it (<paramref name="previousArrival"/>), when it waits until <see cref="StaggerSeconds"/> after that
        /// one started (<paramref name="previousStart"/>): three cues of one instant start 0, 0.09 and 0.18 s after it, and one that comes 0.2 s later is not held back. Until it starts a cue is invisible, but its slot is
        /// already its own (the slides follow the arrivals), so nothing slides under it when it appears.
        /// </summary>
        public static float StartOf(float arrival, float previousArrival, float previousStart)
        {
            float later = previousStart + StaggerSeconds;
            return arrival - previousArrival <= GroupSeconds && later > arrival ? later : arrival;
        }

        public static CuePhase PhaseOf(float age, float life) =>
            age >= life ? CuePhase.Gone : age >= life - FadeSeconds ? CuePhase.Fade : age < ShowSeconds ? CuePhase.Show : CuePhase.Hold;

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>0 to 1 over the fade, 0 before it.</summary>
        public static float FadeT(float age, float life) => Clamp01((age - (life - FadeSeconds)) / FadeSeconds);

        private static float EaseOut(float t) { t = 1f - Clamp01(t); return 1f - t * t * t; }

        /// <summary>How far outside their resting place the two bracket halves are, design units: 46 at the first instant, in at 0.14 s (ease-out); out again over the fade (ease-in).</summary>
        public static float BracketOffset(float age, float life)
        {
            if (age >= life - FadeSeconds) { float t = FadeT(age, life); return BracketRun * t * t; }
            return BracketRun * (1f - EaseOut(age / SnapSeconds));
        }

        /// <summary>How many characters of a message of <paramref name="length"/> have locked on their real glyph, left to right: 0.025 s each, the whole message in no more than 0.3 s.</summary>
        public static int Locked(float age, int length)
        {
            if (length <= 0) return 0;
            if (age <= 0f) return 0;
            float per = Math.Min(CharSeconds, DecodeSeconds / length);
            return Math.Min(length, (int)((age + 0.002f) / per));      // 2 ms of slack: the game's clock is a float of hundreds of seconds, a few tenths of a millisecond coarse, and the last character is locked AT 0.3 s
        }

        /// <summary>
        /// The underline as fractions of the width, left to right: it draws from the left over the show (ease-out), is whole through the hold, and retracts to the right over the fade (its left end runs to the right end).
        /// </summary>
        public static void Underline(float age, float life, out float from, out float to)
        {
            if (age >= life - FadeSeconds) { float t = FadeT(age, life); from = t * t; to = 1f; return; }
            from = 0f;
            to = age >= ShowSeconds ? 1f : EaseOut(age / ShowSeconds);
        }

        /// <summary>The alpha of the cue's words and furniture: 1 until the fade, then the five steps, 0 once the game's time is up.</summary>
        public static float Alpha(float age, float life)
        {
            if (age < 0f || age >= life) return 0f;
            if (age < life - FadeSeconds) return 1f;
            int step = (int)((age - (life - FadeSeconds)) / (FadeSeconds / Steps.Length));
            return Steps[Math.Min(Steps.Length - 1, Math.Max(0, step))];
        }

        /// <summary>The first 0.12 s: the whole cue is one tone brighter.</summary>
        public static bool Flash(float age) => age >= 0f && age < FlashSeconds;

        /// <summary>0 to 1 over 0.12 s from the instant a NEWER cue arrived: how far down its slot an older cue has slid (ease-out).</summary>
        public static float Slide(float newerAge) => EaseOut(newerAge / SlideSeconds);

        /// <summary>A number that is 0 while the cue stands still and otherwise steps every sixtieth of a second: the stack is drawn again when it changes (and not between).</summary>
        public static int Stage(float age, float life)
        {
            if (age < 0f) return -2;      // not started yet (held back behind the cue before it)
            if (age >= life) return -1;
            bool moving = age < ShowSeconds || age >= life - FadeSeconds;
            return moving ? 1 + (int)(Math.Max(0f, age) * 60f) : 0;
        }

        /// <summary>A number that steps every sixtieth of a second while a cue that arrived <paramref name="sinceArrival"/> ago is still sliding the older ones down, and is 0 after.</summary>
        public static int SlideStage(float sinceArrival) => sinceArrival >= 0f && sinceArrival < SlideSeconds + 0.017f ? 1 + (int)(sinceArrival * 60f) : 0;

        /// <summary>One tone brighter: DIM to INK, INK to BRIGHT, BRIGHT to white; AMBER and RED to BRIGHT.</summary>
        public static ScopeTone Raise(ScopeTone tone)
        {
            switch (tone)
            {
                case ScopeTone.Dim:
                case ScopeTone.Faint: return ScopeTone.Ink;
                case ScopeTone.Ink: return ScopeTone.Hi;
                default: return ScopeTone.White;
            }
        }

        /// <summary>The words' tone: BRIGHT, a caution AMBER, an alert RED.</summary>
        public static ScopeTone ToneOf(CueSeverity severity) => severity == CueSeverity.Alert ? ScopeTone.Red : severity == CueSeverity.Caution ? ScopeTone.Amber : ScopeTone.Hi;
    }

    /// <summary>The decode, pure: each character first shows a glyph from A-Z 0-9 and locks to the real one left to right; a space never scrambles. The glyph is a fixed hash of (the cue's seed, the place, the tick), so a render at a given age is always the same.</summary>
    internal static class CueDecode
    {
        public const string Glyphs = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        /// <summary>The glyphs change this many times a second.</summary>
        public const float TickHz = 30f;

        /// <summary>The message at <paramref name="age"/> into <paramref name="into"/> (reused: nothing is allocated); how many characters. Past the buffer the message is cut.</summary>
        public static int Fill(char[] into, string text, float age, int seed)
        {
            int n = Math.Min(into.Length, text.Length), locked = CueTimeline.Locked(age, text.Length), tick = (int)(Math.Max(0f, age) * TickHz);
            for (int i = 0; i < n; i++)
            {
                char c = text[i];
                into[i] = i < locked || c == ' ' ? c : Glyphs[(int)(Mix(seed, i, tick) % (uint)Glyphs.Length)];
            }
            return n;
        }

        private static uint Mix(int seed, int place, int tick)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u + (uint)place * 40503u + (uint)tick * 2246822519u + 374761393u;
                h ^= h >> 15; h *= 2246822519u;
                h ^= h >> 13; h *= 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }
    }

    /// <summary>Where the stack goes, pure (design units, y down from the top-left of the screen).</summary>
    internal static class CueLayout
    {
        /// <summary>The stack's left edge, 16 in; its top at 30 % of the screen's height; 34 between cues.</summary>
        public const float Left = 16f, TopFraction = 0.30f, Pitch = 34f;
        /// <summary>The words start this far in from the left edge (the shelf's caption inset), the bracket halves hug the words this far outside them, an arm is 5 long.</summary>
        public const float TextInset = 12f, HugGap = 8f, Arm = 5f;
        /// <summary>Rows inside a cue's slot: the shelf's line, the message's middle, the underline, the brackets' top and bottom (with the shelf; the bare cue of Lean and Minimal sits <see cref="BareLift"/> higher). The next slot's shelf (and its stub) is 34 on.</summary>
        public const float ShelfRow = 4f, MessageRow = 21f, UnderlineRow = 31f, BracketTop = 12f, BracketBottom = 31f, BareLift = 8f;

        public static float Top(float designHeight) => (float)Math.Round(designHeight * TopFraction);

        /// <summary>The shelf under the class word: 12 in, the word, 4 clear, then a short hairline and the node.</summary>
        public static float ShelfWidth(int captionChars) => (float)Math.Ceiling(TextInset + captionChars * 12f * 0.65f + 4f + 40f - 0.001f);

        /// <summary>How far below its own slot the cue in place <paramref name="place"/> (0 = newest) is: the slots of the newer cues, each slid down over 0.12 s from the instant that cue arrived.</summary>
        public static float Offset(Cue[] shown, int place, float now)
        {
            float y = 0f;
            for (int k = 0; k < place; k++) y += Pitch * CueTimeline.Slide(now - shown[k].Arrival);
            return y;
        }
    }
}
