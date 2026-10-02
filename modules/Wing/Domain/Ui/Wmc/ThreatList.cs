using System;
using System.Globalization;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    internal enum ThreatKind : byte { Air, AirDefence }

    /// <summary>One tracked hostile on TACTICAL's THREATS view (spec tactical v3 §2.2).</summary>
    internal struct ThreatRow
    {
        public uint Id;
        public ThreatKind Kind;
        public string Code;
        public float RangeM, BearingDeg;
        /// <summary>Pointing at us (air only).</summary>
        public bool Hot;
    }

    /// <summary>TACTICAL's THREATS (spec tactical v3 §2.2): the nearest hostiles the side tracks within <see cref="RadiusMetres"/>,
    /// nearest first, at most <see cref="Max"/>.</summary>
    internal static class WingThreatList
    {
        public const int Max = 12;
        public const float RadiusMetres = 60000f, HotDeg = 60f;

        /// <summary>A sorted insert by range, then id; when full the farthest drops. No allocation.</summary>
        public static void Insert(ThreatRow[] into, ref int n, in ThreatRow r)
        {
            int cap = into.Length < Max ? into.Length : Max;
            int at = n;
            while (at > 0 && (into[at - 1].RangeM > r.RangeM || (into[at - 1].RangeM == r.RangeM && into[at - 1].Id > r.Id))) at--;
            if (at >= cap) return;
            int last = n < cap ? n : cap - 1;
            for (int j = last; j > at; j--) into[j] = into[j - 1];
            into[at] = r;
            if (n < cap) n++;
        }

        /// <summary>The compass bearing (0-360, 0 north = +Z) of an offset (<paramref name="dx"/>, <paramref name="dz"/>).</summary>
        public static float Bearing(float dx, float dz)
        {
            float deg = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>A threat heading <paramref name="headingDeg"/> is hot when it points within <see cref="HotDeg"/> of the bearing
        /// from it to us.</summary>
        public static bool Hot(float headingDeg, float bearingToUsDeg)
        {
            float d = Math.Abs(((headingDeg - bearingToUsDeg) % 360f + 540f) % 360f - 180f);
            return d <= HotDeg;
        }

        /// <summary>"SU-27 · 18 KM · 045 · HOT"; an air-defence unit has no aspect.</summary>
        public static string Text(in ThreatRow r)
        {
            string code = string.IsNullOrEmpty(r.Code) ? WmcText.Unknown : r.Code;
            int km = (int)Math.Round(r.RangeM / 1000f);
            int brg = (int)Math.Round(r.BearingDeg) % 360;
            string s = code + " · " + km.ToString(CultureInfo.InvariantCulture) + " KM · " + brg.ToString("000", CultureInfo.InvariantCulture);
            return r.Kind == ThreatKind.Air ? s + (r.Hot ? " · HOT" : " · COLD") : s;
        }

        public static string Rail(in ThreatRow r) => r.Kind == ThreatKind.AirDefence ? "armed" : r.Hot ? "danger" : "info";
    }
}
