using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The AIRCRAFT card's facts about one member, gathered on the host: fuel and bingo, ammo, hull and stores by station. Only for the
    /// one inspected member, at the panel's refresh rate.</summary>
    internal static class WmcDetail
    {
        /// <summary>The member's stores by station, each with its flight-pool class (nothing allocated).</summary>
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
            Stores(m, ref d);
        }
    }
}
