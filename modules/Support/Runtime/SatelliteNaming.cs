namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>Stable call signs for constellation display, one family per role.</summary>
    internal static class SatelliteNaming
    {
        public static string Callsign(SatelliteRole role, byte id)
        {
            string family;
            switch (role)
            {
                case SatelliteRole.Recon: family = "ARGUS"; break;
                case SatelliteRole.Strike: family = "DAMOCLES"; break;
                default: family = "VEIL"; break;
            }
            return family + "-" + id.ToString("00");
        }

        public static string RoleTag(SatelliteRole role)
        {
            switch (role)
            {
                case SatelliteRole.Recon: return "RECON";
                case SatelliteRole.Strike: return "STRIKE";
                default: return "EW";
            }
        }
    }
}
