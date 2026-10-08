using System;
using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>One tick of one own missile, as the HUD needs it. Pure value type, no UnityEngine.</summary>
    internal struct MissileTrack
    {
        public int Id;
        public string Name;
        public float OwnM;
        public float TargetM;
        public bool HasTarget;
        public string TargetName;
        public float AgeS;
    }

    /// <summary>
    /// Missile tracker copy rules (pure): which tracks earn a HUD line, what each line says,
    /// and the C-menu status. Distances reuse <see cref="AvUnitTable"/> breakpoints exactly so
    /// missile previews read like the rest of the HUD.
    /// </summary>
    internal static class MissileTracks
    {
        /// <summary>Missile lines shown at once. The board has four rows shared with every
        /// feed; missiles take at most three so a warning always has room.</summary>
        public const int MaxShown = 3;

        /// <summary>Blind (no target) past this age reads as caution, not routine.</summary>
        public const float BlindCautionAgeS = 5f;

        public static HudTone ToneFor(in MissileTrack t) =>
            !t.HasTarget && t.AgeS > BlindCautionAgeS ? HudTone.Caution : HudTone.Info;

        /// <summary>"MSL 0.8KM · TGT 2.1KM" (head splits to value 0.8KM + label MSL).</summary>
        public static string LineText(in MissileTrack t, AvUnits units)
        {
            var buf = new char[48];
            int len = AvNumFormat.Append(buf, 0, "MSL ");
            len = AvUnitTable.DistanceReading(buf, len, t.OwnM, units);
            if (t.HasTarget)
            {
                len = AvNumFormat.Append(buf, len, " · TGT ");
                len = AvUnitTable.DistanceReading(buf, len, t.TargetM, units);
            }
            else
            {
                len = AvNumFormat.Append(buf, len, " · NO TGT");
            }
            return new string(buf, 0, len);
        }

        /// <summary>"AIM-120 → MIG-29" for the capacitor bar under the cells.</summary>
        public static string DetailText(in MissileTrack t)
        {
            string name = Clip(t.Name, 14);
            if (!t.HasTarget || string.IsNullOrEmpty(t.TargetName)) return name + " · BLIND";
            return name + " → " + Clip(t.TargetName, 14);
        }

        /// <summary>Fraction of the ownship-to-target journey the missile has covered.</summary>
        public static float BarFor(in MissileTrack t)
        {
            if (!t.HasTarget || float.IsNaN(t.OwnM) || float.IsNaN(t.TargetM)) return 0f;
            float total = t.OwnM + t.TargetM;
            if (total < 1f) return 0f;
            return Math.Max(0f, Math.Min(1f, t.OwnM / total));
        }

        /// <summary>"2 MSL · CLOSEST 1.2KM" for the C-menu status line.</summary>
        public static string Status(int count, float closestOwnM, AvUnits units)
        {
            if (count <= 0) return string.Empty;
            var buf = new char[48];
            int len = AvNumFormat.Write(buf, 0, count, 0);
            len = AvNumFormat.Append(buf, len, count == 1 ? " MSL · " : " MSL · CLOSEST ");
            if (count > 1) len = AvUnitTable.DistanceReading(buf, len, closestOwnM, units);
            else len = AvUnitTable.DistanceReading(buf, len, closestOwnM, units);
            return new string(buf, 0, len);
        }

        /// <summary>Wrap a follow index by delta over count; -1 when there is nothing to follow.</summary>
        public static int Cycle(int current, int delta, int count)
        {
            if (count <= 0) return -1;
            int next = (current + delta) % count;
            if (next < 0) next += count;
            return next;
        }

        private static string Clip(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "--";
            string upper = value.Trim().ToUpperInvariant();
            return upper.Length <= max ? upper : upper.Substring(0, max - 1) + "…";
        }
    }
}
