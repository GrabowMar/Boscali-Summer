namespace BoscaliSummer.Core.Math
{
    /// <summary>
    /// Load factor the way the native G meter reads it (<c>GIndicators</c>): the seat acceleration plus one g of
    /// gravity, projected on the aircraft's up axis. Level flight reads 1.0, a 4 g pull reads 4.0, a push-over 0.
    /// (<c>Aircraft.gForce</c> is |dv|/dt without gravity, so level flight reads about 0; do not use it for display.)
    /// Pure: the caller hands over the acceleration (in g, gravity excluded) and the up axis.
    /// </summary>
    internal static class FlightLoad
    {
        /// <summary>Native default <c>AircraftParameters.aircraftGLimit</c>.</summary>
        public const float DefaultLimit = 9f;

        /// <summary>Over-G caution point as a share of the airframe limit.</summary>
        public const float CautionShare = 0.8f;

        /// <summary>Dot(accel + world up, aircraft up).</summary>
        public static float Factor(float ax, float ay, float az, float upX, float upY, float upZ) =>
            ax * upX + (ay + 1f) * upY + az * upZ;

        /// <summary>The load factor at which the over-G caution lights for an airframe limit (default limit when unknown).</summary>
        public static float CautionAt(float limit) => (limit > 1f ? limit : DefaultLimit) * CautionShare;
    }
}
