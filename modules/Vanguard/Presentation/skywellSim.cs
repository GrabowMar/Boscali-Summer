using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Sim-only hooks of the dock script (tests/ingame/vg-skywell-seat.json). A nomodkit scenario cannot press the brake or fly a
    /// seated jet (input is guarded, <c>fly</c> hands the jet to AI and drops the HUD), so it overrides the brake read and places
    /// the jet in the hook-up window; both are inert unless <c>NOMODKIT_SIM_SCENARIO</c> is set.
    /// </summary>
    internal sealed partial class SkywellVisuals
    {
        /// <summary>Replaces the LOCAL aircraft's brake input in both brake reads (null = the real input).</summary>
        internal static float? SimBrake;

        private static float Brake(Aircraft a) => SimRun && SimBrake.HasValue ? SimBrake.Value : a.GetInputs().brake;

        /// <summary>
        /// Puts a jet this peer simulates <paramref name="astern"/> metres behind the tanker's contact point, level with the tanker
        /// and flying its velocity (the same body move the dock script uses, frozen at its first frame).
        /// </summary>
        internal static bool SimPlace(Aircraft r, Aircraft tanker, float astern)
        {
            if (!SimRun || r == null || tanker == null || r.rb == null || !r.LocalSim) return false;
            Hold hold = MakeHold(r, tanker, Time.timeSinceLevelLoad);
            hold.StartPos = DockPos(r, tanker) + Vector3.back * astern; // tanker space: +z is forward
            hold.StartRot = Quaternion.identity;
            hold.Duration = 1f;
            Drive(r, hold, hold.Start); // s = 0: the start pose
            return true;
        }
    }
}
