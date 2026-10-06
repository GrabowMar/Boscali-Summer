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
    internal enum WatchdogAction : byte { None, Reroute, Relocate }

    /// <summary>A ground aircraft that makes no progress (less than <see cref="ProgressMetres"/> in
    /// <see cref="RerouteSeconds"/>) while it is not legitimately waiting (holding short, queued behind a reservation)
    /// is rerouted; still stuck <see cref="RelocateSeconds"/> after it last moved, it is relocated once — never twice
    /// (spec M3 §2.1).</summary>
    internal sealed class StuckWatchdog
    {
        // Relocate at 30 s: wing-taxi sim 2026-10-06 — a jet wedged at its stand at full throttle started taking damage
        // around 24 s and had tipped and shed parts before the 60 s rescue came.
        public static float RerouteSeconds = 20f, RelocateSeconds = 30f, ProgressMetres = 1f;

        private Vec3 anchor;
        private bool primed, rerouted, relocated;
        private float stuck;

        public bool Relocated => relocated;

        public WatchdogAction Update(Vec3 pos, bool waiting, float dt)
        {
            if (!primed || waiting || (pos - anchor).Length > ProgressMetres)
            {
                primed = true;
                anchor = pos;
                stuck = 0f;
                rerouted = false;
                return WatchdogAction.None;
            }
            stuck += dt;
            if (!relocated && stuck >= RelocateSeconds)
            {
                relocated = true;
                return WatchdogAction.Relocate;
            }
            if (!rerouted && stuck >= RerouteSeconds)
            {
                rerouted = true;
                return WatchdogAction.Reroute;
            }
            return WatchdogAction.None;
        }

        /// <summary>A new goal: reroute and relocation may each fire once more.</summary>
        public void Rearm(Vec3 pos)
        {
            rerouted = relocated = false;
            Restart(pos);
        }

        /// <summary>After a relocation or a reroute the clock restarts from where the aircraft is.</summary>
        public void Restart(Vec3 pos)
        {
            anchor = pos;
            stuck = 0f;
        }
    }
}
