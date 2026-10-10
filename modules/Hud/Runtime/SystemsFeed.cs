using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Hud's own provider of the SYSTEMS feed (<see cref="SystemsScope"/>): the watchers' reading of this tick (damage with its zones, fuel with
    /// its marks and the way home) plus what the board adds itself: whether the airframe is a helicopter, the wind
    /// (Weather's <see cref="IFlightEnvironmentView"/>), whether the aircraft is inside a cloud deck (<see cref="IWeatherView"/>) and the
    /// load factor. Refreshed at 10 Hz by <c>HudBoard</c>; the page reads the cache through <see cref="Read"/>. Weather is optional: without it
    /// the page shows no wind line and no cloud word (it never invents a calm clear sky). Client-local.
    /// </summary>
    internal sealed class SystemsFeed
    {
        /// <summary>A cloud deck with less cover than this is not "in cloud".</summary>
        private const float CloudCover = 0.3f;

        private SystemsScope cache;
        private bool valid;

        /// <summary>The <see cref="ScopeProvider{T}"/> the board registers.</summary>
        public bool Read(out SystemsScope data)
        {
            data = cache;
            return valid;
        }

        public void Update(Aircraft aircraft, BoardFrame frame, bool rotary)
        {
            valid = false;      // set again at the end: a read that throws half way leaves no stale reading behind
            if (aircraft == null || !frame.Fuel.Valid) return;
            cache.Rotary = rotary;
            cache.Damage = frame.Damage;
            cache.Fuel = frame.Fuel;
            cache.HeadingDeg = aircraft.transform.eulerAngles.y;
            cache.Units = frame.Units;
            cache.G = AircraftReadout.LoadFactor(aircraft);
            cache.GLimit = AircraftReadout.GLimit(aircraft);

            cache.HasWind = false;
            if (ModuleServices.TryGet(out IFlightEnvironmentView environment) && !(environment is Object gone && gone == null) &&
                environment.TryGet(out FlightEnvironmentSnapshot snapshot) && snapshot.ValidWeather)
            {
                Vector3 wind = snapshot.WindWorldMps;
                cache.HasWind = true;
                cache.WindMps = new Vector2(wind.x, wind.z).magnitude;
                cache.WindFromDeg = (Mathf.Atan2(wind.x, wind.z) * Mathf.Rad2Deg + 180f + 360f) % 360f;   // the wind vector is where it blows TO
            }

            cache.HasCloud = cache.InCloud = false;
            if (ModuleServices.TryGet(out IWeatherView view) && !(view is Object lost && lost == null))
            {
                GlobalPosition at = aircraft.transform.position.ToGlobalPosition();
                if (view.TrySample(at.x, at.z, out WeatherViewSample sky))
                {
                    cache.HasCloud = true;
                    cache.InCloud = sky.Cover >= CloudCover && at.y >= sky.CloudBase && at.y <= sky.CloudTop;
                }
            }
            valid = true;
        }

        public void Clear() => valid = false;
    }
}
