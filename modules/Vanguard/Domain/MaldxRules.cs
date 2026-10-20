using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>MALD-X BAIT: cruise, orbit and exposure numbers (design/vanguard-remake/MECHANICS.md).</summary>
    internal static class MaldxRules
    {
        public const float Lifetime = 420f;
        public const float CruiseSpeed = 230f;
        public const float MinCruiseAltitude = 1500f;
        public const float MinClearance = 500f;   // over high ground the cruise floor lifts to terrain + this
        public const float OrbitEnter = 4000f;    // flat range to the lure where the weave becomes a loiter
        public const float OrbitRadius = 3000f;
        public const float OrbitLeadDeg = 35f;
        public const float ScanInterval = 1f;
        public const float SeeSeconds = 3f;       // cumulative radar contact before an emitter is exposed
        public const float ExposeSeconds = 60f;
        public const int MaxExposed = 8;          // per launcher faction, at once

        // CHAFF: the bait seeds one cloud every ChaffSpacing metres flown; each hangs ChaffSeconds and blinds one missile.
        public const float ChaffSpacing = 300f;
        public const float ChaffSeconds = 60f;
        public const float ChaffRadius = 750f;
        public const float ChaffInterval = 0.5f;

        /// <summary>
        /// A radar seeker is blinded when its line of sight to the target passes within ChaffRadius of a cloud (both
        /// given relative to the seeker). A seeker or a target inside the cloud counts; a cloud behind either does not.
        /// </summary>
        public static bool Screens(Vector3 toCloud, Vector3 toTarget)
        {
            float along = toTarget.sqrMagnitude > 1e-3f ? Mathf.Clamp01(Vector3.Dot(toCloud, toTarget) / toTarget.sqrMagnitude) : 0f;
            return (toTarget * along - toCloud).sqrMagnitude <= ChaffRadius * ChaffRadius;
        }

        public static float CruiseAltitude(float launchY, float groundY) =>
            Mathf.Max(Mathf.Max(launchY, MinCruiseAltitude), groundY + MinClearance);

        /// <summary>P-control on airspeed: half throttle on speed, full 20 m/s slow, idle 20 m/s fast.</summary>
        public static float Throttle(float speed) => Mathf.Clamp01(0.5f + (CruiseSpeed - speed) / 40f);

        /// <summary>The game's radar horizon test (DetectorManager.RequestRadarCheck): sum of both horizons.</summary>
        public static bool AboveHorizon(float radarY, float baitY, float flatRange) =>
            Mathf.Sqrt(12742000f * Mathf.Max(radarY, 0f)) + Mathf.Sqrt(12742000f * Mathf.Max(baitY, 0f)) >= flatRange;

        /// <summary>
        /// Loiter aim point relative to the orbit centre (x east, y north): the bait's bearing from the centre
        /// advanced clockwise (seen from above) by leadDeg, at radius.
        /// </summary>
        public static Vector2 OrbitOffset(Vector2 fromCentre, float radius, float leadDeg)
        {
            float bearing = fromCentre.sqrMagnitude > 1f ? Mathf.Atan2(fromCentre.x, fromCentre.y) : 0f;
            float aim = bearing + leadDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(aim), Mathf.Cos(aim)) * radius;
        }
    }

    /// <summary>
    /// One faction's exposed-emitter book. A site banks radar-contact seconds; at SeeSeconds it is exposed for
    /// ExposeSeconds (kept lit while contact continues). At MaxExposed live entries new sites keep banking and wait.
    /// </summary>
    internal sealed class MaldxLedger
    {
        private sealed class Site
        {
            public float Seen;
            public float Until = float.NegativeInfinity;
        }

        private readonly Dictionary<uint, Site> sites = new Dictionary<uint, Site>();

        /// <summary>One scan in which the bait was inside the site's radar cover. True when this scan exposes it.</summary>
        public bool See(uint site, float now, float dt)
        {
            if (!sites.TryGetValue(site, out Site s)) sites[site] = s = new Site();
            if (s.Until > now)
            {
                s.Until = now + MaldxRules.ExposeSeconds;
                return false;
            }
            s.Seen += dt;
            if (s.Seen < MaldxRules.SeeSeconds || Exposed(now) >= MaldxRules.MaxExposed) return false;
            s.Seen = 0f;
            s.Until = now + MaldxRules.ExposeSeconds;
            return true;
        }

        public bool IsExposed(uint site, float now) => sites.TryGetValue(site, out Site s) && s.Until > now;

        public int Exposed(float now)
        {
            int n = 0;
            foreach (Site s in sites.Values)
                if (s.Until > now) n++;
            return n;
        }

        /// <summary>Live exposures into `into`; lapsed entries with nothing banked are dropped.</summary>
        public void Live(float now, List<uint> into)
        {
            into.Clear();
            List<uint> lapsed = null;
            foreach (KeyValuePair<uint, Site> pair in sites)
            {
                if (pair.Value.Until > now) into.Add(pair.Key);
                else if (pair.Value.Seen <= 0f) (lapsed ??= new List<uint>()).Add(pair.Key);
            }
            if (lapsed != null)
                foreach (uint id in lapsed) sites.Remove(id);
        }

        public void Forget(uint site) => sites.Remove(site);
    }
}
