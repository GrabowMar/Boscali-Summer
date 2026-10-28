using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>The mission's own values at capture and the host's tuning entries: everything the native mapping needs besides the field. Plain numbers, so it is testable.</summary>
    internal readonly struct NativeBaseline
    {
        public readonly float WindX, WindY, WindZ, Turbulence;
        public readonly float MinConditions, MaxConditions, WindVariability, TurbulenceMultiplier;

        public NativeBaseline(float windX, float windY, float windZ, float turbulence,
            float minConditions, float maxConditions, float windVariability, float turbulenceMultiplier)
        {
            WindX = windX; WindY = windY; WindZ = windZ; Turbulence = turbulence;
            MinConditions = minConditions; MaxConditions = maxConditions;
            WindVariability = windVariability; TurbulenceMultiplier = turbulenceMultiplier;
        }
    }

    /// <summary>One set of native outputs: what <c>LevelInfo.conditions</c>, <c>cloudHeight</c>, <c>windVelocity</c> and <c>windTurbulence</c> should follow.</summary>
    internal struct NativeValues
    {
        public float Conditions, CloudHeight, WindX, WindY, WindZ, Turbulence;
    }

    /// <summary>
    /// The native weather outputs by peer role (spec 8.1). <c>level.conditions</c>, <c>cloudHeight</c>, <c>windVelocity</c> and <c>windTurbulence</c> are one value per process,
    /// so the <b>server</b> writes them from a camera-independent reference (the theater centre and the theater mean) and every <b>client</b> derives them from its own camera
    /// and aircraft. Both go through the same mapping and the same fixed 60 s easing, and the server path reads no camera: a server's natives are a pure function of
    /// <c>(key, mission time)</c>, identical in a listen and a dedicated session.
    /// </summary>
    internal static class NativeWeather
    {
        /// <summary>Vanilla greys the fog and kills the sun only above this (<c>LevelInfo</c> tests <c>conditions &gt; 0.6</c>), so the native value never exceeds it and nothing pops at a crossing.</summary>
        public const float ConditionsCap = 0.6f;

        /// <summary>The fixed 60 s transition (rate scale 2 on the old 0.02 / 25 / 2.0 / 0.05 per second): faster than any tape rate of 9.6.2.</summary>
        public const float ConditionsRate = 0.04f, CloudHeightRate = 50f, WindRate = 4f, TurbulenceRate = 0.1f;

        /// <summary>The client's cover is the mean of a 3 x 3 grid reaching this far from the camera.</summary>
        public const float MeanRadiusMetres = 10000f;

        /// <summary>Cell rain supplies its own haze, so the convective term takes this share off the conditions; the wind-speed ratio of a point is clamped to the range below.</summary>
        public const float ConvectiveFog = 0.55f, MinWindRatio = 0.25f, MaxWindRatio = 3f;

        /// <summary>level.conditions = min(0.6, clamp(mean SkyCover, Min, Max) x (1 - 0.55 x Convective)): the convective term comes from the local sample.</summary>
        public static float Conditions(float meanSkyCover, float convective, in NativeBaseline b)
        {
            float lo = Math.Min(b.MinConditions, b.MaxConditions), hi = Math.Max(b.MinConditions, b.MaxConditions);
            float cover = Scalar.Clamp(Scalar.IsFinite(meanSkyCover) ? meanSkyCover : 0f, lo, hi);
            return Math.Min(ConditionsCap, Scalar.Clamp01(cover * (1f - ConvectiveFog * Scalar.Clamp01(convective))));
        }

        /// <summary>
        /// The mission wind scaled by the point's wind-speed ratio and the host's variability, never reversed and never veered: only the magnitude of the point's wind
        /// reaches vanilla (a front's veer and gust stay in <see cref="WeatherPoint"/>, spec 5.3). Vertical wind is the mission's.
        /// </summary>
        public static void Wind(in WeatherPoint at, in NativeBaseline b, out float x, out float y, out float z)
        {
            float factor = WindFactor(at, b.WindVariability);
            x = b.WindX * factor; y = b.WindY; z = b.WindZ * factor;
        }

        /// <summary>The factor <see cref="Wind"/> scales the mission wind by at a point: <c>lerp(1, clamp(windSpeed / BaseWindSpeed, 0.25, 3), variability)</c>.</summary>
        public static float WindFactor(in WeatherPoint at, float windVariability) =>
            Scalar.Lerp(1f, Scalar.Clamp(at.WindSpeed / WeatherField.BaseWindSpeed, MinWindRatio, MaxWindRatio), Scalar.Clamp01(windVariability));

        /// <summary>
        /// The mission's own wind and turbulence as a client recovers them from the first message it adopts, instead of from its own <c>LevelInfo</c> (the host's SyncVars may already hold the host's scaled values):
        /// the target is <c>mission x WindFactor(Regional())</c>, so dividing by the factor of the client's own <c>Regional()</c> gives the mission wind back. Turbulence is <c>max(mission, point) x multiplier</c>, so its
        /// target over the multiplier bounds the mission's from above: the smaller of that and what was captured. A non-finite target, or a factor or multiplier near zero, keeps what was captured.
        /// </summary>
        public static NativeBaseline MissionFromTargets(in WeatherPoint regional, in NativeBaseline captured, float targetWindX, float targetWindZ, float targetTurbulence)
        {
            float factor = WindFactor(regional, captured.WindVariability);
            bool wind = factor > 1e-3f && Scalar.IsFinite(targetWindX) && Scalar.IsFinite(targetWindZ);
            bool turbulence = captured.TurbulenceMultiplier > 1e-3f && Scalar.IsFinite(targetTurbulence);
            return new NativeBaseline(wind ? targetWindX / factor : captured.WindX, captured.WindY, wind ? targetWindZ / factor : captured.WindZ,
                turbulence ? Math.Min(captured.Turbulence, targetTurbulence / captured.TurbulenceMultiplier) : captured.Turbulence,
                captured.MinConditions, captured.MaxConditions, captured.WindVariability, captured.TurbulenceMultiplier);
        }

        public static float Turbulence(in WeatherPoint at, in NativeBaseline b) =>
            Math.Max(b.Turbulence, at.Turbulence) * Math.Max(0f, b.TurbulenceMultiplier);

        /// <summary>
        /// The server's values: wind, turbulence and cloud height from <c>Regional()</c> (the sample at the theater centre), cover from the theater mean of
        /// <see cref="WeatherPoint.SkyCover"/> (<c>MeanCover(3)</c>). No camera, no aircraft: the same key and time give the same values on every server.
        /// </summary>
        public static NativeValues Server(WeatherField field, in NativeBaseline b)
        {
            WeatherPoint centre = field.Regional();
            return Build(field.MeanCover(3), centre, centre, b);
        }

        /// <summary>
        /// A client's values once its field is built from the key: conditions and cloud height at the camera (the 10 km mean of <see cref="WeatherPoint.SkyCover"/>), wind and
        /// turbulence at the followed aircraft. A client's only locally simulated aircraft is its own, so a local gust cannot leak to anyone else.
        /// </summary>
        public static NativeValues Client(WeatherField field, float cameraX, float cameraZ, float aircraftX, float aircraftZ, in NativeBaseline b)
        {
            WeatherPoint camera = field.Sample(cameraX, cameraZ);
            return Build(MeanSkyCover(field, cameraX, cameraZ, MeanRadiusMetres), camera, field.Sample(aircraftX, aircraftZ), b);
        }

        /// <summary>The mean of <see cref="WeatherPoint.SkyCover"/> over a 3 x 3 grid of spacing <paramref name="radius"/> centred on a point.</summary>
        public static float MeanSkyCover(WeatherField field, float x, float z, float radius)
        {
            float sum = 0f;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    sum += field.Sample(x + i * radius, z + j * radius).SkyCover;
            return sum / 9f;
        }

        private static NativeValues Build(float meanSkyCover, in WeatherPoint sky, in WeatherPoint air, in NativeBaseline b)
        {
            var v = new NativeValues
            {
                Conditions = Conditions(meanSkyCover, sky.Convective, b),
                CloudHeight = sky.NativeDeck,
                Turbulence = Turbulence(air, b),
            };
            Wind(air, b, out v.WindX, out v.WindY, out v.WindZ);
            return v;
        }

        /// <summary>One easing step toward <paramref name="target"/> at the fixed rates; <paramref name="dt"/> is the frame time in seconds.</summary>
        public static NativeValues Ease(in NativeValues current, in NativeValues target, float dt)
        {
            dt = Math.Max(0f, dt);
            var next = new NativeValues
            {
                Conditions = Toward(current.Conditions, target.Conditions, ConditionsRate * dt),
                CloudHeight = Toward(current.CloudHeight, target.CloudHeight, CloudHeightRate * dt),
                Turbulence = Toward(current.Turbulence, target.Turbulence, TurbulenceRate * dt),
            };
            float dx = target.WindX - current.WindX, dy = target.WindY - current.WindY, dz = target.WindZ - current.WindZ;
            float length = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz), reach = WindRate * dt;
            float k = length <= reach || length < 1e-6f ? 1f : reach / length;
            next.WindX = current.WindX + dx * k; next.WindY = current.WindY + dy * k; next.WindZ = current.WindZ + dz * k;
            return next;
        }

        /// <summary>A NaN target holds the current value (<c>Math.Sign(NaN)</c> throws, and a throw per frame in Update is worse than a held sky).</summary>
        private static float Toward(float from, float to, float maxDelta) =>
            float.IsNaN(to) ? from : Math.Abs(to - from) <= maxDelta ? to : from + (to > from ? maxDelta : -maxDelta);
    }
}
