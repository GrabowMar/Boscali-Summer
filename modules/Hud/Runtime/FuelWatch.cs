using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Reads the HUD aircraft's fuel for the board's fuel row (2026-10-10): the live level (the sum of the tanks, never the
    /// stale <c>Aircraft.fuelLevel</c>), the burn rate and endurance, and BINGO / JOKER against the nearest friendly field
    /// (<see cref="FuelEndurance"/> is the Wing module's rule). The nearest field is queried once a second, the same query the
    /// retired fuel/divert line made; time is the game's own (<c>Time.time</c>), so a paused game does not dilute the learned
    /// burn rate. Client-local, read-only.
    /// </summary>
    internal sealed class FuelWatch
    {
        private const float FieldSeconds = 1f;

        private readonly FuelEndurance endurance = new FuelEndurance();
        private Aircraft watched;
        private float lastTime = float.NaN, nextField;
        private Airbase field;

        public void Read(Aircraft aircraft, BoardFrame frame)
        {
            frame.Fuel = default;
            if (aircraft == null || aircraft.disabled)
            {
                Forget();
                return;
            }
            if (aircraft != watched)
            {
                Forget();
                watched = aircraft;
            }
            float now = Time.unscaledTime;
            if (now >= nextField)
            {
                nextField = now + FieldSeconds;
                field = NearestField(aircraft);
            }
            float distance = field != null && field.center != null
                ? Vector3.Distance(aircraft.transform.position, field.center.position) : float.NaN;

            float game = Time.time;
            float dt = float.IsNaN(lastTime) ? 0f : Mathf.Min(game - lastTime, 1f);
            lastTime = game;
            float level = AircraftReadout.Fuel(aircraft);
            // ponytail: afterburner is not read, so a long burn skews the learned rate until the 60 s low-pass forgets it.
            endurance.Update(level, distance, CruiseSpeed(aircraft), dt);

            frame.Fuel = new FuelState
            {
                Valid = true,
                Level = level,
                EnduranceS = endurance.EnduranceSeconds,
                Bingo = endurance.Bingo,
                Joker = endurance.Joker,
                Field = field != null ? field.name : null,
                FieldM = distance,
                Tone = FuelEndurance.Tone(level, endurance.Bingo, endurance.Joker),
            };
        }

        public void Release() => Forget();

        private void Forget()
        {
            watched = null;
            field = null;
            lastTime = float.NaN;
            nextField = 0f;
            endurance.Reset();
        }

        /// <summary>The speed the flight home is judged at: the airframe's reference airspeed, else 60 % of its top speed, else 200 m/s.</summary>
        private static float CruiseSpeed(Aircraft aircraft)
        {
            AircraftParameters p = aircraft.GetAircraftParameters();
            if (p == null) return 200f;
            if (p.PIDReferenceAirspeed > 20f) return p.PIDReferenceAirspeed;
            return p.maxSpeed > 20f ? 0.6f * p.maxSpeed : 200f;
        }

        private static Airbase NearestField(Aircraft aircraft)
        {
            if (aircraft.NetworkHQ == null) return null;
            var query = new RunwayQuery { RunwayType = RunwayQueryType.Landing, MinSize = 0f, LandingSpeed = 0f, TailHook = false };
            return aircraft.NetworkHQ.GetNearestAirbase(aircraft.transform.position, query);
        }
    }
}
