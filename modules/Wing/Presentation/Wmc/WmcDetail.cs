using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The deep card's facts about one member, gathered on the host (spec WMC program §4): fuel and bingo, ammo,
    /// damage (detached parts), radar, current target, stores by station, the pilot. Only for the one selected member,
    /// at the panel's refresh rate.</summary>
    internal static class WmcDetail
    {
        /// <summary>The member's stores by station, each with its flight-pool class (TACTICAL's FLIGHT POOL gathers these for
        /// every scoped member at the panel's rate: no pilot text, nothing allocated).</summary>
        public static void Stores(WingMember m, ref MemberDetail d)
        {
            Aircraft a = m.Aircraft;
            d.StoreCount = 0;
            // Review R1 C1: a caller's detail without a stores array got a NullReferenceException that faulted the room.
            if (d.Stores == null) d.Stores = new StoreLine[DetailLines.MaxStores];
            if (a == null || a.disabled || a.weaponStations == null) return;
            foreach (WeaponStation s in a.weaponStations)
            {
                if (d.StoreCount >= d.Stores.Length) break;
                if (s == null || s.Cargo || s.WeaponInfo == null || s.WeaponInfo.hideInDisplay) continue;
                WeaponInfo w = s.WeaponInfo;
                string name = !string.IsNullOrEmpty(w.shortName) ? w.shortName : w.weaponName;
                d.Stores[d.StoreCount++] = new StoreLine
                {
                    Name = name, Ammo = s.Ammo, Full = s.FullAmmo,
                    Class = StoreClasses.Of(w.gun, w.jammer, w.missile, w.bomb || w.glideBomb, w.effectiveness.antiAir, w.effectiveness.antiSurface),
                };
            }
        }

        public static void Gather(WingMember m, ref MemberDetail d)
        {
            Aircraft a = m.Aircraft;
            bool alive = a != null && !a.disabled;
            d.Fuel = alive ? a.GetFuelLevel() : float.NaN;
            d.Ammo = alive ? WingService.AmmoFraction(a) : float.NaN;
            d.BingoSeconds = m.Bingo.SecondsToBingo;
            // The same hull the DAMAGED alert reads (GetDetachedRatio ignored hit-point damage).
            d.Damage = alive ? 1f - m.Damage.Hull : float.NaN;
            // A plain TargetDetector is visual search, not a radar (weapons-radar.md B).
            d.Radar = alive && a.radar is Radar r ? (r.activated ? 1 : 0) : -1;
            Unit t = m.AssignedTarget != null ? m.AssignedTarget : m.StandingTarget;
            d.Target = t != null && !t.disabled ? (t.definition != null ? t.definition.unitName : t.unitName) : null;
            Stores(m, ref d);
            WingPilot p = a != null ? WingPilotRoster.Of(a) : null;
            d.Callsign = p?.Callsign;
            d.Rank = p != null ? WingPilotRoster.RankName(p.Rank) : null;
            d.Perks = null;
            if (p != null && p.Perks.Count > 0)
            {
                var names = new string[p.Perks.Count];
                for (int i = 0; i < names.Length; i++) names[i] = PilotPerks.Name(p.Perks[i]);
                d.Perks = string.Join(", ", names);
            }
        }
    }
    /// <summary>The AIRCRAFT damage map's parts: each part's place in the aircraft's own frame (sideways, along the length), remembered while
    /// the part is attached, because a detached part is no longer where it was; its state (whole, hit, lost) read from the game's hit points
    /// (100 a part, as the DAMAGED alert reads them). One per inspected aircraft; nothing is stored beyond a few floats.</summary>
    internal sealed class PartMap
    {
        public const int Max = 96, HitBelow = 75;
        private readonly float[] homeX = new float[Max], homeZ = new float[Max];
        private readonly bool[] known = new bool[Max];
        private uint forId;

        public void Gather(WingMember m, uint id, ref MemberDetail d)
        {
            if (d.Parts == null) d.Parts = new PartDot[Max];
            d.PartCount = d.PartsLost = d.PartsHit = 0;
            Aircraft a = m.Aircraft;
            if (id != forId)
            {
                forId = id;
                System.Array.Clear(known, 0, known.Length);
            }
            var parts = a != null && !a.disabled ? a.partLookup : null;
            if (parts == null) return;
            int n = System.Math.Min(parts.Count, Max);
            float maxX = 0f, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                UnitPart p = parts[i];
                if (p == null) continue;
                if (!p.IsDetached())
                {
                    UnityEngine.Vector3 l = a.transform.InverseTransformPoint(p.transform.position);
                    homeX[i] = l.x;
                    homeZ[i] = l.z;
                    known[i] = true;
                }
                if (!known[i]) continue;
                maxX = System.Math.Max(maxX, System.Math.Abs(homeX[i]));
                minZ = System.Math.Min(minZ, homeZ[i]);
                maxZ = System.Math.Max(maxZ, homeZ[i]);
            }
            float midZ = (minZ + maxZ) * 0.5f, halfZ = System.Math.Max(0.5f, (maxZ - minZ) * 0.5f), halfX = System.Math.Max(0.5f, maxX);
            for (int i = 0; i < n; i++)
            {
                UnitPart p = parts[i];
                if (p == null) continue;
                bool lost = p.IsDetached();
                bool hit = !lost && p.hitPoints < HitBelow;
                if (lost) d.PartsLost++;
                else if (hit) d.PartsHit++;
                if (!known[i]) continue;
                d.Parts[d.PartCount++] = new PartDot
                {
                    X = UnityEngine.Mathf.Clamp(homeX[i] / halfX, -1f, 1f), Z = UnityEngine.Mathf.Clamp((homeZ[i] - midZ) / halfZ, -1f, 1f),
                    State = (byte)(lost ? 2 : hit ? 1 : 0),
                };
            }
        }
    }
}
