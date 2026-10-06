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
    /// <summary>Shared manoeuvre labels and entry-safety gates, independent of the engine.</summary>
    internal static class ManeuverCatalog
    {
        /// <summary>Manoeuvres in display order.</summary>
        public static readonly ManeuverKind[] All =
        {
            ManeuverKind.BreakLeft,
            ManeuverKind.BreakRight,
            ManeuverKind.SplitS,
            ManeuverKind.Immelmann,
            ManeuverKind.BarrelRoll,
            ManeuverKind.AileronRoll,
            ManeuverKind.Loop,
            ManeuverKind.WingWaggle,
            ManeuverKind.NotchThreat,
            ManeuverKind.MaskTerrain,
        };

        public static string Label(ManeuverKind kind)
        {
            switch (kind)
            {
                case ManeuverKind.BreakLeft:   return "Break Left";
                case ManeuverKind.BreakRight:  return "Break Right";
                case ManeuverKind.SplitS:      return "Split-S";
                case ManeuverKind.Immelmann:   return "Immelmann";
                case ManeuverKind.BarrelRoll:  return "Barrel Roll";
                case ManeuverKind.AileronRoll: return "Aileron Roll";
                case ManeuverKind.Loop:        return "Loop";
                case ManeuverKind.WingWaggle:  return "Wing Waggle";
                case ManeuverKind.NotchThreat: return "Notch Threat";
                case ManeuverKind.MaskTerrain: return "Terrain Mask";
                default:                       return kind.ToString();
            }
        }

        /// <summary>Rotary-compatible manoeuvres; only level breaks avoid unsupported vertical energy
        /// demands.</summary>
        public static bool RotaryCapable(ManeuverKind kind) =>
            kind == ManeuverKind.BreakLeft ||
            kind == ManeuverKind.BreakRight ||
            kind == ManeuverKind.WingWaggle ||
            kind == ManeuverKind.NotchThreat ||
            kind == ManeuverKind.MaskTerrain;

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
