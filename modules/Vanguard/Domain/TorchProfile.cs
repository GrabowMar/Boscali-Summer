using System.Globalization;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// SRB-2 TORCH strap-on boost rockets, burned like a throttle: every bottle on the aircraft lights while the pilot holds
    /// fire and goes quiet on release, relighting on the next press until the fuel is gone (BurnSeconds of full thrust per
    /// sortie). The total push is Accel times the aircraft's mass whatever the bottle count, split over the lit bottles; thrust
    /// ramps in and out instead of stepping, fuel burns with it, and a dry bottle drops away.
    /// </summary>
    internal static class TorchProfile
    {
        public const float BurnSeconds = 15f;       // s of full thrust in one load of fuel
        public const float RampIn = .3f, RampOut = .4f;
        public const float Accel = 1f * 9.81f;      // m/s2 at full thrust, all bottles together
        public const float HoldGrace = .15f;        // s the burn holds after the last fire call (one is sent every frame while held)
        public const float ReplayInterval = .5f;    // s between the flying peer's "still burning" replays to the others
        public const float ReplayHold = .65f;       // s a replay keeps a remote peer's plume lit
        public const float BottleLength = 3.2f;     // m, the bottle's long axis
        public const float FuelKg = 320f;           // loaded mass is EmptyKg + FuelKg
        public const float EmptyKg = 60f;
        public const float Cost = .08f;             // per bottle, same scale as the other munition prices
        public const float IrIntensity = 3f;        // heat seekers see the burn (a missile motor is 1)
        public const float DustAgl = 20f;           // below this the plume kicks up the ground
        public const float CasingLife = 20f;        // s a separated casing tumbles before it is removed
        public const string Channel = "vg.torch";

        /// <summary>Kg per second at full thrust: a full bottle lasts BurnSeconds.</summary>
        public static float FuelRate => FuelKg / BurnSeconds;

        /// <summary>Next throttle 0..1: up over RampIn while held (and fuelled), down over RampOut on release.</summary>
        public static float Throttle(float current, bool held, float dt) =>
            Mathf.MoveTowards(current, held ? 1f : 0f, dt / (held ? RampIn : RampOut));

        /// <summary>Kg one bottle burns in dt at this throttle (never more than it holds).</summary>
        public static float Burn(float fuel, float throttle, float dt) => Mathf.Min(fuel, FuelRate * throttle * dt);

        /// <summary>Seconds of full thrust left in a bottle holding this much fuel.</summary>
        public static float SecondsLeft(float fuel) => Mathf.Max(0f, fuel) / FuelRate;

        /// <summary>One lit bottle's push in newtons: the aircraft's share of Accel x mass at this throttle, split over the lit bottles.</summary>
        public static float Force(float aircraftMass, int lit, float throttle) => lit <= 0 ? 0f : Accel * aircraftMass * Mathf.Clamp01(throttle) / lit;

        public static string Words(float left) => "FUEL " + left.ToString("0.0", CultureInfo.InvariantCulture) + " S";

        public static string Detail(float throttle, int lit) =>
            lit + (lit == 1 ? " BOTTLE" : " BOTTLES") + " - THRUST " + Mathf.RoundToInt(throttle * 100f) + "%";
    }
}
