namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The landing chain of the APPROACH page: INTERCEPT, GLIDE, FLARE, ROLLOUT. A vertical lander flying to a pad uses the same three first
    /// slots as APPROACH, DESCENT and TOUCHDOWN (<see cref="ApproachScope.Pad"/>).
    /// </summary>
    internal enum ApproachStage : byte { Intercept, Glide, Flare, Rollout }

    /// <summary>The chain's words, in stage order: the runway landing's four and the pad landing's three.</summary>
    internal static class ApproachWords
    {
        public static readonly string[] Runway = { "INTERCEPT", "GLIDE", "FLARE", "ROLLOUT" };
        public static readonly string[] Pad = { "APPROACH", "DESCENT", "TOUCHDOWN" };

        public static string Of(ApproachStage stage, bool pad) => pad ? Pad[(int)stage < Pad.Length ? (int)stage : Pad.Length - 1] : Runway[(int)stage];
    }

    /// <summary>
    /// The APPROACH page's feed (HUD v8), served by the Autopilot module: where the aircraft is against the runway it is landing on (the landing
    /// autopilot's own, or the nearest landing runway of the nearest friendly field when the pilot flies it by hand), the ILS deviations, the speed
    /// it should fly and the configuration. SI units; the runway frame has its origin at the threshold, +along down the runway, +cross to the right
    /// of the landing direction. Client-local: every number is read from the local aircraft and the synced airbase, so it works on clients too.
    /// </summary>
    internal struct ApproachScope
    {
        /// <summary>The landing autopilot is flying (the phase chain is then its own; by hand the stage is read from the geometry).</summary>
        public bool Autoland;
        /// <summary>A vertical lander over a landing pad: no runway; <see cref="PadX"/> / <see cref="PadY"/> place the pad.</summary>
        public bool Pad;
        public ApproachStage Stage;
        /// <summary>The field or pad (upper case) and the runway designator (<c>RWY 27</c>); either may be null.</summary>
        public string Field, Runway;
        /// <summary>Runway length and width, and the touchdown aim point measured from the threshold.</summary>
        public float RunwayLength, RunwayWidth, AimM;
        /// <summary>Aircraft position in the runway frame: along from the threshold (negative before it), across (+ right of the centre line) and the
        /// wheels' height above the touchdown point. <see cref="RadarAltM"/> is the radar altitude, <see cref="PathDeg"/> the flight-path angle (+ = descending).</summary>
        public float AlongM, CrossM, HeightM, RadarAltM, PathDeg;
        /// <summary>The localizer / glideslope deviations in degrees (+ = right / high); <see cref="HasIls"/> false: the scales are not meaningful (flare, rollout, pad).</summary>
        public bool HasIls;
        public float LocDeg, GsDeg;
        /// <summary>The glide path's angle in degrees (the ILS setting by hand, the autopilot's own slope when it flies): the funnel's gates follow it.</summary>
        public float SlopeDeg;
        /// <summary>Airspeed, ground speed and sink rate (+ = descending), and the speed the approach wants (<see cref="HasTarget"/> false: none known).</summary>
        public float SpeedMps, GroundMps, SinkMps, TargetMps;
        public bool HasTarget;
        /// <summary>Seconds to touchdown at the closing speed (capped at 30; 30 when not closing).</summary>
        public float TouchdownS;
        /// <summary>Crosswind component on the runway axis, + = from the left; <see cref="HasWind"/> false: no wind reading.</summary>
        public bool HasWind;
        public float CrosswindMps;
        /// <summary>Gear locked down / in transit, and the airbrake out.</summary>
        public bool GearDown, GearMoving, Airbrake;
        /// <summary>Another aircraft is on the runway.</summary>
        public bool Occupied;
        /// <summary>Pad only: the pad's place in metres, right of and ahead of the nose (heading up).</summary>
        public float PadX, PadY;
        /// <summary>Flat distance to the threshold (or the pad).</summary>
        public float DistanceM;
    }
}
