using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Weather.Audio;
using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Networking;
using BoscaliSummer.Modules.Weather.Presentation;
using BoscaliSummer.Modules.Weather.Visuals;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using NuclearOption.MissionEditorScripts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    // ENV panel queries: forecast timeline, briefing, solar and lunar data.
    internal sealed partial class WeatherManager
    {
        /// <summary>
        /// Obtain the deterministic forecast timeline for display on the ENV panel.
        /// Cached while the ENV screen is open; this samples the same seeded field as live weather.
        /// </summary>
        public ForecastStep[] GetForecastTimeline()
        {
            float now = Time.unscaledTime;
            if (cachedForecast != null && now - lastForecastSampleTime < 5.0f)
            {
                return cachedForecast;
            }

            if (!MissionTimeSource.TryNow(out float missionTime)) return cachedForecast ?? Array.Empty<ForecastStep>();
            lastForecastSampleTime = now;
            int[] offsets = WeatherForecast.DefaultOffsetsMinutes;
            if (cachedForecast == null || cachedForecast.Length != offsets.Length) cachedForecast = new ForecastStep[offsets.Length];

            if (fieldKey == null)
            {
                for (int i = 0; i < offsets.Length; i++)
                    cachedForecast[i] = WeatherForecast.SampleForecast(currentConditions,
                        currentCloudHeight > 0f ? currentCloudHeight : 3000f,
                        missionSeed, missionTime, offsets[i]);
                return cachedForecast;
            }

            // Spec 8.4: the weather at the aircraft's current position at t + offset, each read from the tape with D at that time. A held tape gives one sky at every offset,
            // and the screen says HELD instead of drawing it.
            Vector2 at = AircraftPosition();
            EnvForecast.Fill(cachedForecast, forecastField, fieldKey, missionTime, fieldSpan.x * 0.5f, fieldSpan.y * 0.5f,
                LevelInfo.i != null ? LevelInfo.i.timeOfDay : 12f, offsets, at.x, at.y, field.IsBuilt && field.Moving && fieldKey.Dynamic, forcedRainIntensity);
            return cachedForecast;
        }

        /// <summary>The ENV briefing of the sky over the followed aircraft (spec 8.4); false until the field is built from a tape.</summary>
        internal bool TryBriefing(out EnvBriefing briefing)
        {
            briefing = default;
            WeatherField f = Field;
            if (f == null || f.TapeSource == null) return false;
            Vector2 at = AircraftPosition();
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Aircraft aircraft = cameras != null ? cameras.followingUnit as Aircraft : null;
            Vector3 v = aircraft != null && aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
            float groundSpeed = new Vector2(v.x, v.z).magnitude;
            briefing = EnvBriefings.Build(f.TapeSource, f.Frame, f.Sample(at.x, at.y), at.x, at.y, groundSpeed, f.Moving && f.Key.Dynamic);
            return true;
        }

        public SolarData GetSolarData()
        {
            LevelInfo level = LevelInfo.i;
            if (level == null || level.sun == null)
            {
                return new SolarData(0f, 0f, -1f, -1f, false, false, -1f, "---");
            }

            Vector3 sunDir = level.sun.transform.forward;
            Vector3 worldAxis = level.worldAxis != null ? level.worldAxis.transform.forward : Vector3.up;
            float tod = level.timeOfDay;

            return WeatherForecast.ComputeSolarData(
                sunDir.x, sunDir.y, sunDir.z,
                worldAxis.x, worldAxis.y, worldAxis.z,
                tod);
        }

        public LunarData GetLunarData()
        {
            LevelInfo level = LevelInfo.i;
            if (level == null)
            {
                return new LunarData("---", 0f, 0f, true);
            }

            float phase = level.moonPhase.GetValueOrDefault(0.5f);
            return WeatherForecast.ComputeLunarData(phase);
        }
    }
}
