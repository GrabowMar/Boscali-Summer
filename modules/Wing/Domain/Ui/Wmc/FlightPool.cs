namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>What a store is, by role.</summary>
    internal enum StoreClass : byte { Other, AirMissile, StrikeMissile, Bomb, Gun, Ecm }

    internal static class StoreClasses
    {
        /// <summary>From the game's weapon flags: guns, jammers, bombs, and missiles by which role they are better at.</summary>
        public static StoreClass Of(bool gun, bool jammer, bool missile, bool bomb, float antiAir, float antiSurface)
        {
            if (gun) return StoreClass.Gun;
            if (jammer) return StoreClass.Ecm;
            if (missile) return antiAir >= antiSurface ? StoreClass.AirMissile : StoreClass.StrikeMissile;
            return bomb ? StoreClass.Bomb : StoreClass.Other;
        }
    }
}
