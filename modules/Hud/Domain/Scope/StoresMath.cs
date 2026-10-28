using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the weapon cell's state tab says (the word is the state; colour only adds to it).</summary>
    internal enum StoreState : byte { Armed, Safe, Firing, Launch, Release, Reload, Empty }

    /// <summary>What a countermeasure dispenser's band says: only DISPENSE, FIRING, LOW and EMPTY light it, a plain selected dispenser is just SEL.</summary>
    internal enum CmCellState : byte { Ready, Selected, Firing, Low, Empty, Dispense }

    /// <summary>
    /// The stores side's rules (the prototype's <c>takeover/stores.js</c> maths, LAYOUT.md 3.1 to 3.3), pure: which silhouette a weapon wears, what the pooled rounds of a weapon read, the arm word, a
    /// station's mark on the rail, a dispenser's word, the capacitor's cells and which of the three blocks (HERO, PWR BUS, EXPENDABLES) exist.
    /// </summary>
    internal static class StoresMath
    {
        /// <summary>At or below this share of the pooled load the weapon is LOW (amber figure, LOW tag); none left is EMPTY.</summary>
        public const float LowShare = 0.2f;
        /// <summary>FIRING / LAUNCH / RELEASE stays on the tab this long after a shot.</summary>
        public const float FireWordS = 0.9f;
        /// <summary>The capacitor is LOW (amber figure) at or below this share of its full charge.</summary>
        public const float CapLowShare = 0.2f;
        /// <summary>A charge that moves less than this (kJ per second) is holding.</summary>
        public const float TrendBand = 1f;

        /// <summary>The class of a store. Order matters: a gun is a gun even on a pod, a powered weapon is a rail, a jammer or cargo is a kit, a launcher of rockets a pod, a missile an AAM or an AGM by its rated role.</summary>
        public static StoreClass ClassOf(in WeaponFlags f)
        {
            if (f.Gun) return StoreClass.Gun;
            if (f.Energy) return StoreClass.Rail;
            if (f.Bomb) return StoreClass.Bomb;
            if (f.Jammer || f.Cargo) return StoreClass.Kit;
            if (f.Launcher) return StoreClass.Pod;
            if (f.Missile) return f.AntiSurface > f.AntiAir ? StoreClass.Agm : StoreClass.Aam;
            return StoreClass.Kit;
        }

        public static string ClassWord(StoreClass c)
        {
            switch (c)
            {
                case StoreClass.Gun: return "GUN";
                case StoreClass.Aam: return "AAM";
                case StoreClass.Agm: return "AGM";
                case StoreClass.Bomb: return "BOMB";
                case StoreClass.Pod: return "POD";
                case StoreClass.Rail: return "RAIL";
                default: return "KIT";
            }
        }

        /// <summary>The rounds of every station that carries the same weapon as station <paramref name="index"/> (two AAM-4 pylons read as one "2 /2"), and their full load.</summary>
        public static void Pool(in StoresScope d, int index, out int total, out int max)
        {
            total = max = 0;
            if (d.Stations == null || index < 0 || index >= d.StationCount) return;
            string name = d.Stations[index].Name;
            for (int i = 0; i < d.StationCount; i++)
            {
                if (!ReferenceEquals(d.Stations[i].Name, name) && d.Stations[i].Name != name) continue;
                total += Math.Max(0, d.Stations[i].Ammo);
                max += Math.Max(0, d.Stations[i].Max);
            }
        }

        public static bool Empty(int total) => total <= 0;

        /// <summary>LOW: some left, the full load known, and a fifth of it or less.</summary>
        public static bool Low(int total, int max) => total > 0 && max > 0 && total <= max * LowShare + 1e-4f;

        /// <summary>The fraction of the pooled load left, 0..1 (0 when the full load is unknown).</summary>
        public static float Fraction(int total, int max) => max > 0 ? Math.Min(1f, Math.Max(0f, (float)total / max)) : 0f;

        /// <summary>The tab's state for the selected station: a reload first, then the word of a shot that has just left, then SAFE or ARMED, and EMPTY once nothing is left.</summary>
        public static StoreState StateOf(in StoresScope d)
        {
            if (d.Selected < 0 || d.Selected >= d.StationCount) return StoreState.Armed;
            StoreStation s = d.Stations[d.Selected];
            if (s.Reloading) return StoreState.Reload;
            Pool(d, d.Selected, out int total, out _);
            if (s.FiredAgeS < FireWordS) return s.Class == StoreClass.Gun ? StoreState.Firing : s.Class == StoreClass.Bomb ? StoreState.Release : StoreState.Launch;
            if (total <= 0) return StoreState.Empty;
            return d.Safe ? StoreState.Safe : StoreState.Armed;
        }

        public static string Word(StoreState s)
        {
            switch (s)
            {
                case StoreState.Safe: return "SAFE";
                case StoreState.Firing: return "FIRING";
                case StoreState.Launch: return "LAUNCH";
                case StoreState.Release: return "RELEASE";
                case StoreState.Reload: return "RELOAD";
                case StoreState.Empty: return "EMPTY";
                default: return "ARMED";
            }
        }

        /// <summary>A dispenser's band: the THREAT page's call first, then rounds just fired, then EMPTY, LOW, a plain selection, READY.</summary>
        public static CmCellState CmStateOf(in CmStation s, bool call, bool firing)
        {
            if (call) return CmCellState.Dispense;
            if (firing) return CmCellState.Firing;
            CmLevel level = HudCm.Level(s);
            if (level == CmLevel.Empty) return CmCellState.Empty;
            if (level == CmLevel.Low) return CmCellState.Low;
            return s.Selected ? CmCellState.Selected : CmCellState.Ready;
        }

        /// <summary>The countermeasure station is called for: its seeker classes meet the call mask (flares for IR, chaff for radar). A jammer is never called (it is a switch, not a round).</summary>
        public static bool Called(in CmStation s, byte call) => !s.Chargeable && (s.Mask & call) != 0;

        // ---- which blocks exist, and the HERO block's words and tones (LAYOUT.md 3.1)

        /// <summary>HERO: a station is selected.</summary>
        public static bool HasHero(in StoresScope d) => d.Stations != null && d.Selected >= 0 && d.Selected < d.StationCount;

        /// <summary>PWR BUS: the airframe has a capacitor with a user.</summary>
        public static bool HasPwr(in StoresScope d) => d.HasCap;

        /// <summary>EXPENDABLES: the airframe has a dispenser or a jammer.</summary>
        public static bool HasExp(in StoresScope d) => d.CmCount > 0;

        /// <summary>The arm word's and the arm marker's tone: ARMED and the words of a shot just gone are BRIGHT; SAFE, RELOAD and EMPTY are amber.</summary>
        public static ScopeTone ArmTone(StoreState s) => s == StoreState.Safe || s == StoreState.Reload || s == StoreState.Empty ? ScopeTone.Amber : ScopeTone.Hi;

        /// <summary>The arm marker is a filled square for every state but SAFE (a hollow one).</summary>
        public static bool ArmFilled(StoreState s) => s != StoreState.Safe;

        /// <summary>The rounds numeral: BRIGHT; amber when none is left or a fifth is; INK while the station reloads (the amber word beside the name carries the countdown).</summary>
        public static ScopeTone RoundsTone(bool reloading, bool empty, bool low) => reloading ? ScopeTone.Ink : empty || low ? ScopeTone.Amber : ScopeTone.Hi;

        /// <summary>The line art's tint: INK while master arm is SAFE, BRIGHT armed or hot, amber when low, empty or reloading.</summary>
        public static ScopeTone ArtTone(StoreState s, bool low) => low || s == StoreState.Reload || s == StoreState.Empty ? ScopeTone.Amber : s == StoreState.Safe ? ScopeTone.Ink : ScopeTone.Hi;

        /// <summary>The class word's tone: GUN is furniture (DIM), "STA 3 AGM" carries a digit, so it is data (INK).</summary>
        public static ScopeTone ClassTone(StoreClass c) => c == StoreClass.Gun ? ScopeTone.Dim : ScopeTone.Ink;

        /// <summary>A magazine store (a gun's belt, a pod's rockets): the share already spent is drawn dimmer. Missiles pooled over pylons are not: one picture is one round.</summary>
        public static bool Magazine(StoreClass c, int max) => (c == StoreClass.Gun || c == StoreClass.Pod) && max > 1;

        /// <summary>The spent share of a magazine store's picture is drawn at this alpha of the INK.</summary>
        public const float SpentArtAlpha = 0.4f;

        /// <summary>A spent station's glyph on the rail is drawn at this alpha, dashed.</summary>
        public const float SpentMarkAlpha = 0.8f;

        /// <summary>How a station reads on the rail: INK, BRIGHT when selected (and bracketed), amber and bold while it reloads; a spent one (nothing left, not reloading) is dashed and a little dimmer but still readable.</summary>
        public static RailMark RailMarkOf(in StoreStation q, bool selected)
        {
            bool spent = q.Ammo <= 0 && !q.Reloading;
            return new RailMark(q.Reloading ? ScopeTone.Amber : selected ? ScopeTone.Hi : ScopeTone.Ink, spent ? SpentMarkAlpha : 1f, spent, selected || q.Reloading, selected);
        }

        // ---- PWR BUS (LAYOUT.md 3.2)

        public const float CellW = 7f, CellH = 10f, CellPitch = 9f;
        /// <summary>An unlit cell is a 1 x <see cref="CellH"/> DIM tick this far into its slot (the middle of the 7), not an outline.</summary>
        public const float UnlitTickX = 3f;

        /// <summary>How many cells a row <paramref name="width"/> units wide holds (each <see cref="CellW"/> wide, <see cref="CellPitch"/> apart): 20 in WIDE's 180, 22 in STACK's inner 196 (<see cref="StoresLayout.PwrInner"/>).</summary>
        public static int Cells(float width) => Math.Max(1, (int)Math.Floor((width + CellPitch - CellW) / CellPitch));

        /// <summary>The lit cells for a charge share <paramref name="frac"/> of <paramref name="cells"/>.</summary>
        public static int LitCells(float frac, int cells) => (int)Math.Floor((frac < 0f ? 0f : frac > 1f ? 1f : frac) * cells + 0.5f);

        /// <summary>A charge that falls by at least this share of the full charge (and at least <see cref="ShotMinKj"/>) between two reads, and that fast (kJ per second), is a shot, not the jammer's trickle or a stalled frame.</summary>
        public const float ShotMinShare = 0.04f, ShotMinKj = 5f, ShotMinRate = 100f;

        /// <summary>The "SHOT -n" word and the just-spent cells last this long.</summary>
        public const float ShotWordS = 1.1f;

        /// <summary>The charge fell from <paramref name="before"/> to <paramref name="now"/> (kJ) in <paramref name="seconds"/> by a shot's worth: <paramref name="drop"/> says how much.</summary>
        public static bool Shot(float before, float now, float seconds, float maxKj, out float drop)
        {
            drop = before - now;
            return drop >= Math.Max(ShotMinKj, maxKj * ShotMinShare) && drop >= ShotMinRate * Math.Max(seconds, 0.02f);
        }

        // ---- EXPENDABLES (LAYOUT.md 3.3)

        /// <summary>A dispenser's state word on its row: DISPENSE while the THREAT page calls for it, FIRING for rounds just gone, LOW amber, SEL on the selected row; none otherwise (EMPTY takes the bar's place).</summary>
        public static string StateWord(CmCellState s) =>
            s == CmCellState.Dispense ? "DISPENSE" : s == CmCellState.Firing ? "FIRING" : s == CmCellState.Low ? "LOW" : s == CmCellState.Selected ? "SEL" : null;

        /// <summary>A dispenser's count: amber at a quarter or less, red at none; else BRIGHT on the selected row and INK on the others.</summary>
        public static ScopeTone CountTone(CmLevel l, bool selected) => l == CmLevel.Empty ? ScopeTone.Red : l == CmLevel.Low ? ScopeTone.Amber : selected ? ScopeTone.Hi : ScopeTone.Ink;

        /// <summary>The share of a station's bar that is lit: rounds over the full load (a load the game gives no full size for reads full while any is left), a jammer's charge.</summary>
        public static float BarShare(in CmStation c) =>
            c.Chargeable ? Math.Max(0f, Math.Min(1f, c.Charge)) : c.Max > 0 ? Math.Max(0f, Math.Min(1f, c.Ammo / (float)c.Max)) : c.Ammo > 0 ? 1f : 0f;

        /// <summary>The capacitor's word: DRN while it drains, CHG while it charges (each with the measured rate), FULL or HOLD when it is steady.</summary>
        public static CapWord CapWordOf(float rateKjS, float fraction) =>
            rateKjS <= -TrendBand ? CapWord.Drain : rateKjS >= TrendBand ? CapWord.Charge : fraction >= 0.999f ? CapWord.Full : CapWord.Hold;

        public static bool CapLow(float kj, float max) => max > 0f && kj / max <= CapLowShare;
    }

    internal enum CapWord : byte { Hold, Full, Charge, Drain }

    /// <summary>How one station reads on the rail: the tone of its glyph and its tag, the glyph's alpha, whether its outline is dashed (a spent station), whether its tag is bold and whether it is bracketed (the selected one).</summary>
    internal readonly struct RailMark
    {
        public readonly ScopeTone Tone;
        public readonly float Alpha;
        public readonly bool Dashed, Bold, Bracketed;

        public RailMark(ScopeTone tone, float alpha, bool dashed, bool bold, bool bracketed) { Tone = tone; Alpha = alpha; Dashed = dashed; Bold = bold; Bracketed = bracketed; }
    }
}
