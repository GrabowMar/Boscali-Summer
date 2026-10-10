using System;
using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the missile's seeker is doing (<c>Missile.seekerMode</c>).</summary>
    internal enum SeekerState : byte { Unknown, Locked, Search, Passive }

    /// <summary>One tick of the missile-cam readout: the own missile the camera is riding. NaN where the game cannot say.</summary>
    internal struct MissileCamData
    {
        public bool Valid;
        /// <summary>This missile's place in the follow list ("1/2") and its weapon's short name.</summary>
        public int Index, Count;
        public string Weapon;
        /// <summary>The seeker's type as the game spells it (SARH, IR, INS) and what it is doing.</summary>
        public string Seeker;
        public SeekerState State;
        /// <summary>The target the missile is guiding on (name empty when blind).</summary>
        public bool HasTarget;
        public string Target;
        public float RangeM, ClosureMps, TtiS;
    }

    /// <summary>
    /// The missile-cam HUD (2026-10-10), pure: while the orbit camera rides one of our missiles the native canvas is off and
    /// nothing said how far the target was, how fast the missile was closing, or whether its seeker still had it. These are the
    /// words for the readout, from <see cref="MissileCamData"/>.
    /// </summary>
    internal static class MissileCamText
    {
        private static readonly char[] Scratch = new char[48];

        /// <summary><c>Missile.SeekerMode</c>: activeLock = 1, activeSearch = 2, passive = 3.</summary>
        public static SeekerState StateOf(int seekerMode) =>
            seekerMode == 1 ? SeekerState.Locked : seekerMode == 2 ? SeekerState.Search : seekerMode == 3 ? SeekerState.Passive : SeekerState.Unknown;

        /// <summary>"MSL 1/2 · AIM-120".</summary>
        public static string Header(in MissileCamData d)
        {
            string weapon = string.IsNullOrEmpty(d.Weapon) ? "" : " · " + d.Weapon.Trim().ToUpperInvariant();
            return "MSL " + Math.Max(1, d.Index + 1) + "/" + Math.Max(1, d.Count) + weapon;
        }

        /// <summary>"SARH · LOCKED", "IR · SEARCH", "INS · NO TGT": the seeker and what it is doing.</summary>
        public static string Seeker(in MissileCamData d)
        {
            string type = string.IsNullOrEmpty(d.Seeker) ? "SEEKER" : d.Seeker.Trim().ToUpperInvariant();
            if (!d.HasTarget) return type + " · NO TGT";
            string state = d.State == SeekerState.Locked ? "LOCKED" : d.State == SeekerState.Search ? "SEARCH" : d.State == SeekerState.Passive ? "PASSIVE" : "TRACK";
            return type + " · " + state;
        }

        /// <summary>"2.4KM": the range to go (distance reading); "--" when unknown.</summary>
        public static string Range(in MissileCamData d, AvUnits units)
        {
            if (float.IsNaN(d.RangeM)) return "--";
            int len = AvUnitTable.DistanceReading(Scratch, 0, d.RangeM, units);
            return new string(Scratch, 0, len).ToUpperInvariant();
        }

        /// <summary>"840KM/H" while closing; "OPENING" when the target is pulling away; "--" when unknown.</summary>
        public static string Closure(in MissileCamData d, AvUnits units)
        {
            if (float.IsNaN(d.ClosureMps)) return "--";
            if (d.ClosureMps < 0f) return "OPENING";
            int len = AvUnitTable.SpeedReading(Scratch, 0, d.ClosureMps, units);
            return new string(Scratch, 0, len).ToUpperInvariant();
        }

        /// <summary>
        /// Seconds until the missile arrives; NaN when it is not closing. Unlike a threat row there is no cap: a cruise missile
        /// has minutes to go and that is what the pilot rides it for.
        /// </summary>
        public static float TimeToGo(float rangeM, float closureMps) =>
            closureMps >= ThreatRank.MinClosureMps && rangeM >= 0f ? rangeM / closureMps : float.NaN;

        /// <summary>"3S" until impact, "20:00" from 100 s up; "--" when it is not closing.</summary>
        public static string Tti(in MissileCamData d)
        {
            if (float.IsNaN(d.TtiS) || float.IsInfinity(d.TtiS)) return "--";
            int s = (int)Math.Min(359999.0, Math.Max(1.0, Math.Round(d.TtiS, MidpointRounding.AwayFromZero)));
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return s < 100 ? s.ToString(c) + "S" : (s / 60).ToString(c) + ":" + (s % 60).ToString("00", c);
        }

        /// <summary>Ink while the seeker has its target, caution while it is searching, blind or passive.</summary>
        public static HudTone Tone(in MissileCamData d) =>
            d.HasTarget && d.State == SeekerState.Locked ? HudTone.Info : HudTone.Caution;
    }
}
