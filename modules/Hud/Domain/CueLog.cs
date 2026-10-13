using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Which of the game's own message boxes a cue stands in for: the action report (<c>AircraftActionsReport</c>), the reserve notice (<c>ReserveReport</c>), the funds line (<c>AllocationDisplay</c>) and the score line (<c>KillDisplay.killText</c>).</summary>
    internal enum CueSource : byte { Action, Reserve, Allocation, Score }

    /// <summary>The DIM class word on a cue's shelf.</summary>
    internal enum CueClass : byte { Sys, Wpn, Dmg, Funds, Rsv }

    /// <summary>Normal is BRIGHT, a caution AMBER, an alert (the game's alert colour: a failure) RED.</summary>
    internal enum CueSeverity : byte { Normal, Caution, Alert }

    internal static class CueWords
    {
        public static string Of(CueClass c)
        {
            switch (c)
            {
                case CueClass.Wpn: return "WPN";
                case CueClass.Dmg: return "DMG";
                case CueClass.Funds: return "FUNDS";
                case CueClass.Rsv: return "RSV";
                default: return "SYS";
            }
        }
    }

    /// <summary>One message the game showed on a grey box, kept for the CUE stack: the words as drawn (upper case, fitted), its class and severity, and when it came and how long the game keeps it (the game's own clock, <c>Time.timeSinceLevelLoad</c>).</summary>
    internal sealed class Cue
    {
        public CueSource Source;
        public CueClass Class;
        public CueSeverity Severity;
        /// <summary>The message in capitals, tags gone, cut to fit <see cref="CueFit"/>.</summary>
        public string Text;
        /// <summary>20 or 16 design units.</summary>
        public float Size;
        public float Arrival, Life;
        /// <summary>When its SHOW starts: its arrival, or a little after it when it came in the same burst as the cue before (<see cref="CueTimeline.StartOf"/>). Its age, life and fade count from here; its slot from its arrival.</summary>
        public float Start;
        /// <summary>Counts up with every cue ever added: a cue's identity.</summary>
        public int Serial;
        /// <summary>Seeds the decode's glyphs: the same words always scramble the same way.</summary>
        public int Seed;

        public float Age(float now) => now - Start;
        public bool Alive(float now) => now - Start < Life;
    }

    /// <summary>
    /// The newest four messages of the game's grey boxes in a fixed ring, oldest first (the CUE stack), pure. A cue lives as long as the game shows its line (<see cref="CueTimeline.LifeOf"/>: the game's own time, at least
    /// <see cref="MinLife"/>). The clock is the game's <c>Time.timeSinceLevelLoad</c>, which starts again with every scene: a clock that runs backwards empties the ring. Everything here is allocation free except <see cref="Add"/>,
    /// which runs once per message the game shows.
    /// </summary>
    internal sealed class CueLog
    {
        public const int Capacity = 4, MaxShown = 3;
        public const float MinLife = 1.2f, DefaultSeconds = 5f, MaxSeconds = 60f;
        /// <summary>A native box shows text but this log holds no live cue of its source for this long: the capture is not seeing the game's messages (<see cref="Healthy"/>).</summary>
        public const float MissSeconds = 0.6f;
        private const int Sources = 4;

        private readonly Cue[] ring = new Cue[Capacity];
        private readonly float[] missSince = { float.NaN, float.NaN, float.NaN, float.NaN };
        private int count, serial;

        public int Count => count;

        /// <summary>The <paramref name="i"/>th oldest cue.</summary>
        public Cue this[int i] => ring[i];

        public void Clear()
        {
            Array.Clear(ring, 0, ring.Length);
            count = 0;
            for (int i = 0; i < Sources; i++) missSince[i] = float.NaN;
        }

        /// <summary>
        /// Keep a message the game just showed: <paramref name="rich"/> is its text as the game wrote it, <paramref name="severity"/> what its native colour says (a failure is alert-coloured), <paramref name="seconds"/> how long the game keeps it.
        /// Returns the cue, or null when there was nothing to show (tags only).
        /// </summary>
        public Cue Add(CueSource source, string rich, CueSeverity severity, float arrival, float seconds)
        {
            Tick(arrival);
            string plain = CueText.Plain(rich);
            if (plain.Length == 0) return null;
            CueClass cls = CueText.Classify(plain, source, ref severity);
            string text = CueFit.Fit(plain, out float size);
            if (count == Capacity)
            {
                Array.Copy(ring, 1, ring, 0, Capacity - 1);
                count--;
            }
            float start = count > 0 ? CueTimeline.StartOf(arrival, ring[count - 1].Arrival, ring[count - 1].Start) : arrival;
            var cue = new Cue { Source = source, Class = cls, Severity = severity, Text = text, Size = size, Arrival = arrival, Start = start, Life = CueTimeline.LifeOf(seconds), Serial = ++serial, Seed = CueText.Seed(text) };
            ring[count++] = cue;
            return cue;
        }

        /// <summary>The scene started again (the clock ran back): nothing of the old one is left.</summary>
        public void Tick(float now)
        {
            if (count > 0 && now + 0.5f < ring[count - 1].Arrival) Clear();
        }

        /// <summary>Cues of <paramref name="source"/> that are still showing.</summary>
        public int LiveCount(CueSource source, float now)
        {
            int n = 0;
            for (int i = 0; i < count; i++) if (ring[i].Source == source && ring[i].Alive(now)) n++;
            return n;
        }

        /// <summary>MINIMAL keeps only the AMBER and RED cues; every other level every cue.</summary>
        public static bool Shows(BoardDeclutter level, Cue cue) => level != BoardDeclutter.Minimal || cue.Severity != CueSeverity.Normal;

        /// <summary>
        /// The live cues <paramref name="level"/> shows whose source is in charge (<paramref name="chargeMask"/>: bit <see cref="CueSource"/>), newest first, at most <see cref="MaxShown"/>, into <paramref name="into"/>; how many.
        /// </summary>
        public int Select(float now, BoardDeclutter level, int chargeMask, Cue[] into)
        {
            int n = 0;
            for (int i = count - 1; i >= 0 && n < into.Length && n < MaxShown; i--)
            {
                Cue c = ring[i];
                if (c.Alive(now) && (chargeMask >> (int)c.Source & 1) != 0 && Shows(level, c)) into[n++] = c;
            }
            return n;
        }

        /// <summary>A number that changes whenever what <see cref="Select"/> returns would be drawn differently: a cue comes, goes, or moves a step of its show, fade or slide (<see cref="CueTimeline.Stage"/>).</summary>
        public static int Signature(float now, BoardDeclutter level, int chargeMask, Cue[] shown, int count)
        {
            unchecked
            {
                int h = 17 + (int)level * 7 + chargeMask * 131;
                for (int i = 0; i < count; i++)
                {
                    h = h * 31 + shown[i].Serial;
                    h = h * 31 + CueTimeline.Stage(now - shown[i].Start, shown[i].Life);
                    h = h * 31 + CueTimeline.SlideStage(now - shown[i].Arrival);
                }
                return h;
            }
        }

        /// <summary>
        /// False when the game's own box of <paramref name="source"/> is showing something (<paramref name="nativeShowing"/>) and no cue of that source is live for <see cref="MissSeconds"/>: the capture is not seeing the game's
        /// messages, and the game's box is better left alone (fail open). A cue that is live, or a box that shows nothing, is healthy at once.
        /// </summary>
        public bool Healthy(CueSource source, float now, bool nativeShowing)
        {
            int s = (int)source;
            if (!nativeShowing || LiveCount(source, now) > 0) { missSince[s] = float.NaN; return true; }
            if (float.IsNaN(missSince[s])) missSince[s] = now;
            return now - missSince[s] <= MissSeconds;
        }
    }
}
