using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// The ASAT ascent every client sees: when the OPERATIONS mirror lists a flight that this client has not played yet, the existing cosmetic launch streak
    /// (<see cref="SatelliteLaunchVisuals"/>: a bright trail and a rising light on a gravity-turn arc) starts at the launch point for the seconds the flight has left.
    /// Nothing here is a vanilla missile and nothing is replicated beyond the flight row; a flight id is played once per scene.
    /// </summary>
    internal static class OpsFlightVisuals
    {
        private static int lastPlayed;

        public static void Reset() => lastPlayed = 0;

        public static void Tick(OpsMirror mirror, float now)
        {
            if (mirror == null || !mirror.Known || !mirror.State.Active) return;
            foreach (OpsFlightRow flight in mirror.State.Flights)
            {
                if (flight.Id <= lastPlayed) continue;
                lastPlayed = flight.Id;
                float remaining = flight.EndsAt - now;
                if (remaining < 3f || !SupportTargeting.TryMapPoint(new GlobalPosition(flight.X, 0f, flight.Z), out Vector3 ground)) continue;
                SatelliteLaunchVisuals.Play(ground, remaining);
            }
        }
    }
}
