namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// VANGUARD prices in millions, on vanilla's scale (IRM-S2 0.18, AGM-99 0.48, NL-98 0.89 a round). Reusable drones that
    /// kill several targets a sortie cost what that many vanilla missiles would. A munition's unit value equals its price, as
    /// every vanilla missile's does: kill rewards pay sqrt(value), so the old 20-110 values made a shot-down decoy or drone pay
    /// more than a fighter. Keep in sync with the spec table in Assets/Editor/VanguardBuilder.cs (the bundle's baked values).
    /// </summary>
    internal static class VanguardPrices
    {
        /// <summary>Price a round of the munition with this jsonKey; 0 for a key that is not VANGUARD's.</summary>
        public static float Munition(string jsonKey)
        {
            switch (jsonKey)
            {
                case VanguardKeys.MaldX: return 0.4f;
                case VanguardKeys.MaldJ: return 0.8f;
                case VanguardKeys.Remora: return 2.4f;
                case VanguardKeys.HawcX: return 3f;
                case VanguardKeys.AegisDart: return 0.2f;
                case VanguardKeys.Glaive2A: return 2f;
                case VanguardKeys.Glaive2S: return 2.8f;
                case VanguardKeys.Orca: return 1.8f;
                case VanguardKeys.AleX: return 0.1f;
                case VanguardKeys.Kestrel: return 1.2f;
                case VanguardKeys.Hamilcar: return 2f;
                default: return 0f;
            }
        }

        /// <summary>Per-round price of a VANGUARD gun by its WeaponInfo asset name; 0 when the bundle's own price stands.</summary>
        public static float Gun(string infoName)
        {
            if (infoName == null) return 0f;
            if (infoName.Contains(VanguardKeys.LanceInfo)) return 0.08f;           // a slug kills armour like an AT-145 (0.08)
            if (infoName.Contains("WI_VG_GlaiveCannon_S")) return 0.0003f;          // vanilla 30mm Cannon round
            return 0f;
        }
    }
}
