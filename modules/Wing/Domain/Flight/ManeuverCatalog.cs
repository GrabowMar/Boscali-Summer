namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Shared manoeuvre labels and entry-safety gates, independent of the engine.</summary>
    internal static class ManeuverCatalog
    {

        /// <summary>Minimum entry altitude in metres AGL, allowing room for each manoeuvre's
        /// descent.</summary>
        public static float MinEntryAltitudeAgl(ManeuverKind kind)
        {
            switch (kind)
            {
                case ManeuverKind.SplitS:      return 1400f;
                case ManeuverKind.Loop:        return 900f;
                case ManeuverKind.BarrelRoll:  return 500f;
                case ManeuverKind.Immelmann:   return 400f;
                case ManeuverKind.AileronRoll: return 350f;
                case ManeuverKind.BreakLeft:
                case ManeuverKind.BreakRight:  return 120f;
                case ManeuverKind.NotchThreat: return 80f;
                case ManeuverKind.WingWaggle:  return 60f;
                case ManeuverKind.MaskTerrain: return 40f;
                default:                       return 400f;
            }
        }

        /// <summary>Minimum fraction of maximum airspeed needed to enter safely.</summary>
        public static float MinEntrySpeedFraction(ManeuverKind kind)
        {
            switch (kind)
            {
                case ManeuverKind.Loop:
                case ManeuverKind.Immelmann:  return 0.55f;
                case ManeuverKind.SplitS:
                case ManeuverKind.BarrelRoll: return 0.40f;
                case ManeuverKind.AileronRoll: return 0.30f;
                default:                       return 0.20f;
            }
        }
    }
}
