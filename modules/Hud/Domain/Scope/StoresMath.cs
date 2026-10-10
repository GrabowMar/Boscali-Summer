using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the weapon cell's state tab says (the word is the state; colour only adds to it).</summary>
    internal enum StoreState : byte { Armed, Safe, Firing, Launch, Release, Reload, Empty }

    /// <summary>What a countermeasure dispenser's band says: only DISPENSE, FIRING, LOW and EMPTY light it, a plain selected dispenser is just SEL.</summary>
    internal enum CmCellState : byte { Ready, Selected, Firing, Low, Empty, Dispense }

    /// <summary>
    /// The STORES HEADER's rules (the prototype's <c>takeover/stores.js</c> maths), pure: which silhouette a weapon wears, what the pooled
    /// rounds of a weapon read, the state tab's word, a dispenser's band, the station rail's pip width and the dot grid of a dispenser.
    /// </summary>
    internal static class StoresMath
    {
        /// <summary>At or below this share of the pooled load the weapon is LOW (amber figure, LOW tag); none left is EMPTY.</summary>
        public const float LowShare = 0.2f;
        /// <summary>FIRING / LAUNCH / RELEASE stays on the tab this long after a shot.</summary>
        public const float FireWordS = 0.9f;
        /// <summary>The pip flashes for this long after its station fired.</summary>
        public const float PipFlashS = 0.35f;
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

        /// <summary>
        /// The width of a non-gun pip on the station rail: what is left of <paramref name="width"/> after the gun pips (<paramref name="gunWidth"/> each) and the gaps,
        /// shared by the other stations, between 12 and 30 px (a few stations do not stretch into wide slabs, a crowded rail stays readable).
        /// </summary>
        public static float PipWidth(int stations, int guns, float width, float gunWidth, float gap)
        {
            int others = stations - guns;
            if (others <= 0) return gunWidth;
            float w = (float)Math.Floor((width - guns * gunWidth - (stations - 1) * gap) / others);
            return w < 12f ? 12f : w > 30f ? 30f : w;
        }

        /// <summary>
        /// A dispenser's dot grid inside <paramref name="w"/> x <paramref name="h"/>: the prototype's 12 (a big load) or 10 columns at pitch 6, a narrower pitch (down to 3)
        /// and then a wider grid when the load will not fit, and past that one dot stands for <paramref name="perDot"/> rounds.
        /// </summary>
        public static void Grid(int rounds, float w, float h, out int cols, out int rows, out float pitch, out int perDot)
        {
            rounds = Math.Max(1, rounds);
            perDot = 1;
            int preferred = rounds > 36 ? 12 : 10;
            for (pitch = 6f; pitch >= 3f; pitch -= 1f)
            {
                int fitCols = (int)(w / pitch), fitRows = (int)(h / pitch);
                if (fitCols < 1 || fitRows < 1) continue;
                int c = Math.Min(preferred, fitCols), r = (rounds + c - 1) / c;
                if (r <= fitRows) { cols = c; rows = r; return; }
                c = fitCols;
                r = (rounds + c - 1) / c;
                if (r <= fitRows) { cols = c; rows = r; return; }
            }
            pitch = 3f;
            cols = Math.Max(1, (int)(w / 3f));
            int rowsMax = Math.Max(1, (int)(h / 3f));
            perDot = (rounds + cols * rowsMax - 1) / (cols * rowsMax);
            rows = (((rounds + perDot - 1) / perDot) + cols - 1) / cols;
        }

        /// <summary>The capacitor's word: DRN while it drains, CHG while it charges (each with the measured rate), FULL or HOLD when it is steady.</summary>
        public static CapWord CapWordOf(float rateKjS, float fraction) =>
            rateKjS <= -TrendBand ? CapWord.Drain : rateKjS >= TrendBand ? CapWord.Charge : fraction >= 0.999f ? CapWord.Full : CapWord.Hold;

        public static bool CapLow(float kj, float max) => max > 0f && kj / max <= CapLowShare;
    }

    internal enum CapWord : byte { Hold, Full, Charge, Drain }
}
