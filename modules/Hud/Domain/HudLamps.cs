using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Landing gear as an annunciator sees it.</summary>
    internal enum GearLamp { Unknown, Up, Down, Moving }

    /// <summary>How one annunciator lamp is lit. Dark is "nothing to say"; the rest light in a tone.</summary>
    internal enum LampState { Dark, On, Caution, Warning }

    /// <summary>One tick of own-aircraft system states; null = the game does not expose it on this aircraft.</summary>
    internal struct SystemStates
    {
        public GearLamp Gear;
        public bool? FlightAssist, AutoHover, RadarEmitting, NightVision, WeaponSafe, EngineRunning;
    }

    /// <summary>One lamp: its legend and how it is lit. Flashing is for state in transition.</summary>
    internal struct Lamp
    {
        public string Legend;
        public LampState State;
        public bool Flash;
        public bool Present;
    }

    /// <summary>
    /// The always-on annunciator row of the status board (2026-10-07): a real cockpit-style lamp strip of the
    /// aircraft's own switches, read-only. Lit means "this is on" for modes (GEAR down, FA, HOVR, NVG), caution for
    /// things that give you away or stop you shooting (RDR emitting, SAFE), warning for an engine that is not running.
    /// A missing system leaves its lamp out. Pure; fills a caller-owned array, no allocation.
    /// </summary>
    internal static class HudLamps
    {
        public const int Count = 7;

        public static void Fill(in SystemStates s, Lamp[] lamps)
        {
            lamps[0] = new Lamp
            {
                Legend = "GEAR", Present = s.Gear != GearLamp.Unknown,
                State = s.Gear == GearLamp.Down ? LampState.On : s.Gear == GearLamp.Moving ? LampState.Caution : LampState.Dark,
                Flash = s.Gear == GearLamp.Moving,
            };
            lamps[1] = Mode("FA", s.FlightAssist, LampState.On);
            lamps[2] = Mode("HOVR", s.AutoHover, LampState.On);
            lamps[3] = Mode("RDR", s.RadarEmitting, LampState.Caution);
            lamps[4] = Mode("NVG", s.NightVision, LampState.On);
            lamps[5] = Mode("SAFE", s.WeaponSafe, LampState.Caution);
            lamps[6] = new Lamp
            {
                Legend = "ENG", Present = s.EngineRunning.HasValue,
                State = s.EngineRunning == false ? LampState.Warning : LampState.Dark,
            };
        }

        /// <summary>
        /// Whether a lamp is drawn: any lamp while unlit ones are (<paramref name="showDark"/>: the Full level, or the spawn lamp test), else only one that is lit or has just changed
        /// (<paramref name="changed"/>, the "just changed" window: a lamp that went out is seen once before it leaves the row).
        /// </summary>
        public static bool Drawn(LampState state, bool changed, bool showDark) => showDark || changed || state != LampState.Dark;

        private static Lamp Mode(string legend, bool? on, LampState lit) =>
            new Lamp { Legend = legend, Present = on.HasValue, State = on == true ? lit : LampState.Dark };
    }

    /// <summary>
    /// Where the lamp line's lamps go (LAYOUT.md 3.4), pure. A lamp is a 4 unit square, 3 clear, then its word in bold 12 (7.8 units a letter, rounded up); two groups share the line: the system lamps from the left end
    /// and the threat lamps (LOCK SPKE MSL) against the right end. At the Full level every lamp has its slot, <see cref="SlotGap"/> apart (less, never under <see cref="MinGap"/>, when the groups would come closer than
    /// <see cref="GroupGap"/>); at Lean and Minimal only the lit ones are drawn and they pack <see cref="PackedGap"/> apart.
    /// </summary>
    internal static class LampLine
    {
        public const float Marker = 7f, SlotGap = 10f, PackedGap = 14f, MinGap = 4f, GroupGap = 16f;

        /// <summary>The width of a lamp whose word has <paramref name="letters"/> characters.</summary>
        public static float Width(int letters) => Marker + (letters * 78 + 9) / 10;

        /// <summary>
        /// The left edge of every lamp: <paramref name="left"/> (widths in order) from x 0, <paramref name="right"/> ending at <paramref name="width"/>. <paramref name="slots"/>: the Full level's gaps, else the packed ones.
        /// </summary>
        public static void Place(ReadOnlySpan<float> left, ReadOnlySpan<float> right, bool slots, float width, Span<float> leftX, Span<float> rightX)
        {
            float sum = 0f, gap = slots ? SlotGap : PackedGap;
            foreach (float w in left) sum += w;
            foreach (float w in right) sum += w;
            int gaps = Math.Max(0, left.Length - 1) + Math.Max(0, right.Length - 1);
            if (slots && gaps > 0 && left.Length > 0 && right.Length > 0 && width - sum - gaps * gap < GroupGap) gap = Math.Max(MinGap, (width - sum - GroupGap) / gaps);
            float x = 0f;
            for (int i = 0; i < left.Length; i++) { leftX[i] = x; x += left[i] + gap; }
            float rightSum = 0f;
            foreach (float w in right) rightSum += w;
            x = width - rightSum - gap * Math.Max(0, right.Length - 1);
            for (int i = 0; i < right.Length; i++) { rightX[i] = x; x += right[i] + gap; }
        }
    }
}
