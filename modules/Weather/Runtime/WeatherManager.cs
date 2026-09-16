using System;
using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// The only thing in the module that touches the game. The host drives the vanilla sky from
    /// the deterministic schedule; a client reads the same schedule and the replicated vanilla
    /// values, which is why the forecast needs no wire format of its own.
    ///
    /// Every driven value goes through <see cref="WeatherDrive"/>: the ramp is bounded, and a
    /// foreign write — an authored <c>ModifyEnvironment</c> beat, another mod, the debug
    /// override — wins for a bounded hold while the model resumes around it.
    /// </summary>
    internal sealed class WeatherManager : MonoBehaviour, ISceneService
    {
        private enum Channel
        {
            Conditions,
            CloudBase,
            WindSpeed,
            Turbulence,
            Heading
        }

        private const float WriteInterval = 0.25f;
        private const float ConditionsRate = 0.012f;
        private const float CloudBaseRate = 18f;
        private const float WindSpeedRate = 0.35f;
        private const float TurbulenceRate = 0.02f;
        private const float HeadingRate = 4f;
        private const float MaxElapsed = 2f;
        private const float ForecastInterval = 1f;
        private const float OverrideDetection = 0.08f;
        private const float OverrideRateMultiplier = 20f;
        private const float HazePerFire = 1f / 24f;

        private readonly WeatherDrive[] channels =
        {
            new WeatherDrive(), new WeatherDrive(), new WeatherDrive(), new WeatherDrive(), new WeatherDrive()
        };

        private WeatherSettings settings;
        private ManualLogSource log;

        private string missionIdentity;
        private int seed;
        private float previousTime;
        private float writeElapsed;
        private float nextWriteAt;        private float nextForecastAt;
        private bool announced;
        private int announcedRegime = -1;
        private int announcedStorms = int.MinValue;

        private WeatherState live;
        private WeatherForecast forecast;
        private WeatherSnapshot snapshot;

        private readonly StormCell[] cells = new StormCell[StormField.MaxCells];
        private int cellCount;

        private bool overrideActive;
        private WeatherState overrideState;

        public bool HostAuthority => GameAccess.IsServer();

        public bool Enabled => settings != null && settings.Enabled.Value;

        public float MissionTime { get; private set; }

        public WeatherForecast Forecast => forecast;

        public WeatherSnapshot Snapshot => snapshot;

        public bool OverrideActive => overrideActive;

        /// <summary>
        /// The live storm population. Read-only by contract: the renderer, the rain and the radar
        /// all read this same array so they cannot disagree about where the weather is. Valid up
        /// to <see cref="CellCount"/>; the buffer is reused every tick and never resized.
        /// </summary>
        public StormCell[] CellBuffer => cells;

        public int CellCount => cellCount;

        public void Configure(WeatherSettings weatherSettings, ManualLogSource logger)
        {
            settings = weatherSettings;
            log = logger;
        }

        /// <summary>Debug control: aim the sky directly and stop the schedule steering it.</summary>
        public void ApplyOverride(WeatherState state)
        {
            if (!overrideActive) log?.LogInfo("Weather: debug override engaged.");
            overrideActive = true;
            overrideState = state;
            for (int i = 0; i < channels.Length; i++) channels[i].Release();
        }

        /// <summary>Debug control: force a named regime rather than a numeric delta.</summary>
        public void ForceRegime(WeatherRegime regime)
        {
            WeatherState basis = overrideActive ? overrideState : live;
            int index = WeatherRegimes.Index(regime);
            float severity = index / (float)(WeatherRegimes.Count - 1);
            ApplyOverride(new WeatherState(
                regime,
                WeatherState.Lerp(WeatherRegimes.ConditionsLo(index), WeatherRegimes.ConditionsHi(index), severity),
                WeatherState.Lerp(WeatherRegimes.CloudBaseLo(index), WeatherRegimes.CloudBaseHi(index), severity),
                WeatherState.Lerp(WeatherRegimes.WindLo(index), WeatherRegimes.WindHi(index), severity),
                basis.WindHeading,
                WeatherState.Lerp(WeatherRegimes.TurbulenceLo(index), WeatherRegimes.TurbulenceHi(index), severity)));
        }

        public void ReleaseOverride()
        {
            if (!overrideActive) return;
            overrideActive = false;
            for (int i = 0; i < channels.Length; i++) channels[i].Reset();
            log?.LogInfo("Weather: debug override released, schedule resumed.");
        }

        public void ResetForScene()
        {
            missionIdentity = null;
            seed = 0;
            previousTime = 0f;
            writeElapsed = 0f;
            nextWriteAt = 0f;
            announcedRegime = -1;
            announcedStorms = int.MinValue;
            nextForecastAt = 0f;
            MissionTime = 0f;
            live = default;
            forecast = null;
            snapshot = WeatherSnapshot.Unavailable;
            cellCount = 0;
            overrideActive = false;
            overrideState = default;
            for (int i = 0; i < channels.Length; i++) channels[i].Reset();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (!Enabled) return;

            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            if (mission == null || !MissionManager.IsRunning)
            {
                if (missionIdentity != null) ResetForScene();
                return;
            }

            string identity = MissionIdentity();
            if (!string.Equals(identity, missionIdentity, StringComparison.Ordinal))
            {
                ResetForScene();
                missionIdentity = identity;
                seed = WeatherModel.Seed(identity);
                live = ReadLive();
                if (!announced)
                {
                    announced = true;
                    log?.LogInfo("Weather schedule active for mission '" + identity + "'; a front lasts " +
                                 WeatherReadout.Decimal(WeatherModel.FrontSeconds / 60f, 0) + " minutes.");
                }
            }

            float now = mission.MissionTime;
            float elapsed = now - previousTime;
            if (elapsed < 0f || elapsed > MaxElapsed) elapsed = 0f;
            previousTime = now;
            MissionTime = now;

            live = ReadLive();
            for (int i = 0; i < channels.Length; i++)
            {
                channels[i].Advance(elapsed);
                channels[i].Observe(LiveValue(live, (Channel)i));
            }

            WeatherState model = overrideActive ? overrideState : WeatherModel.Sample(seed, now);
            model = WeatherState.WithHaze(model, Haze());
            Announce(model);
            RefreshCells(now, model);
            AnnounceStorms();
            // The ramp must advance by the real time since the last write, not by the write
            // interval, or every driven value crawls at a fraction of its stated rate.
            writeElapsed += elapsed;
            if (HostAuthority && now - nextWriteAt >= 0f)
            {
                nextWriteAt = now + WriteInterval;
                WriteWorld(model, Mathf.Max(writeElapsed, WriteInterval));
                writeElapsed = 0f;
            }

            if (forecast == null || now >= nextForecastAt)
            {
                nextForecastAt = now + ForecastInterval;
                forecast = WeatherForecast.Build(
                    seed,
                    now,
                    settings.ForecastSteps.Value,
                    settings.ForecastStepMinutes.Value * 60f);
            }

            snapshot = BuildSnapshot(model);
        }

        /// <summary>
        /// Place the storm population for this instant. A cell only exists if the front has the
        /// energy for one, so a clear sky raises nothing at all.
        /// </summary>
        private void RefreshCells(float now, WeatherState model)
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            float mapSize = level != null ? level.mapSize : 0f;
            // The field works in metres. A map that reports a small number is reporting
            // kilometres, so scale it rather than silently raising no storms at all.
            if (mapSize > 0f && mapSize < 1000f) mapSize *= 1000f;
            cellCount = StormField.Fill(
                cells, seed, now, mapSize, model.Conditions, model.WindHeading, model.WindSpeed);
        }

        /// <summary>Where the reader is looking from, for every local storm reading.</summary>
        private static bool TryLocalPosition(out float x, out float z)
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null)
            {
                x = 0f;
                z = 0f;
                return false;
            }

            x = cameras.transform.position.x;
            z = cameras.transform.position.z;
            return true;
        }

        /// <summary>
        /// One line when the storm picture changes: how many cells, and the worst one's place
        /// relative to the reader. This is what makes "is the weather doing anything" answerable
        /// from the log without the debug overlay.
        /// </summary>
        private void AnnounceStorms()
        {
            int signature = cellCount;
            for (int i = 0; i < cellCount; i++)
                signature = signature * 31 + (int)(cells[i].Intensity * 4f) * 7 + (int)cells[i].Kind;
            if (signature == announcedStorms) return;
            announcedStorms = signature;

            if (cellCount <= 0)
            {
                log?.LogInfo("Weather: no storm cells in this front.");
                return;
            }

            int strongest = 0;
            for (int i = 1; i < cellCount; i++)
                if (cells[i].Intensity > cells[strongest].Intensity) strongest = i;
            StormCell cell = cells[strongest];
            log?.LogInfo("Weather: " + cellCount + " storm cell(s) — strongest " +
                         StormReadout.Kind(cell.Kind) +
                         ", intensity " + WeatherReadout.Percent01(cell.Intensity) +
                         ", top " + WeatherReadout.Meters(cell.TopHeight) +
                         ", radius " + WeatherReadout.Meters(cell.Radius) + ".");
        }

        /// <summary>
        /// One line per regime change, so a player reading the log can see the schedule is alive
        /// without opening the debug overlay. A front lasts minutes, so this cannot spam.
        /// </summary>
        private void Announce(WeatherState model)
        {
            int regime = WeatherRegimes.Index(model.Regime);
            if (regime == announcedRegime) return;
            announcedRegime = regime;
            log?.LogInfo("Weather: " + (overrideActive ? "override" : "front") + " — " +
                         WeatherReadout.Regime(model.Regime) +
                         ", conditions " + WeatherReadout.Percent01(model.Conditions) +
                         ", cloud base " + WeatherReadout.Meters(model.CloudBase) +
                         ", wind " + WeatherReadout.Wind(model.WindSpeed, model.WindHeading) +
                         ", turbulence " + WeatherReadout.Decimal(model.Turbulence, 2) + ".");
        }

        private void WriteWorld(WeatherState model, float elapsed)
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null) return;

            Drive(level, Channel.Conditions, model.Conditions, elapsed);
            Drive(level, Channel.CloudBase, model.CloudBase, elapsed);
            Drive(level, Channel.WindSpeed, model.WindSpeed, elapsed);
            Drive(level, Channel.Turbulence, model.Turbulence, elapsed);
            Drive(level, Channel.Heading, model.WindHeading, elapsed);
        }

        private void Drive(LevelInfo level, Channel channel, float target, float elapsed)
        {
            WeatherDrive drive = channels[(int)channel];
            if (!drive.ShouldWrite) return;
            float current = LiveValue(live, channel);
            float rate = Rate(channel) * (overrideActive ? OverrideRateMultiplier : 1f);
            float next = channel == Channel.Heading
                ? drive.StepAngle(current, target, elapsed, rate)
                : drive.Step(current, target, elapsed, rate);
            if (Mathf.Approximately(next, current)) return;
            Write(level, channel, next);
            drive.Acknowledge(next);
        }

        private static void Write(LevelInfo level, Channel channel, float value)
        {
            switch (channel)
            {
                case Channel.Conditions: level.Networkconditions = value; break;
                case Channel.CloudBase: level.NetworkcloudHeight = value; break;
                case Channel.WindSpeed: level.SetWindSpeed(value); break;
                case Channel.Turbulence: level.SetWindTurbulence(value); break;
                case Channel.Heading: level.SetWindHeading(value); break;
            }
        }

        private static float Rate(Channel channel)
        {
            switch (channel)
            {
                case Channel.Conditions: return ConditionsRate;
                case Channel.CloudBase: return CloudBaseRate;
                case Channel.WindSpeed: return WindSpeedRate;
                case Channel.Turbulence: return TurbulenceRate;
                default: return HeadingRate;
            }
        }

        private static float LiveValue(WeatherState state, Channel channel)
        {
            switch (channel)
            {
                case Channel.Conditions: return state.Conditions;
                case Channel.CloudBase: return state.CloudBase;
                case Channel.WindSpeed: return state.WindSpeed;
                case Channel.Turbulence: return state.Turbulence;
                default: return state.WindHeading;
            }
        }

        private WeatherState ReadLive()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null) return live;
            return new WeatherState(
                WeatherRegimes.FromConditions(level.conditions),
                level.conditions,
                level.cloudHeight,
                level.windSpeed,
                WeatherState.WrapHeading(HeadingFromVelocity(level.windVelocity)),
                level.windTurbulence);
        }

        /// <summary>
        /// The synced mean wind vector, not <c>GetWind(position)</c>: only the server ever rotates
        /// the vanilla wind zone, so a client-side per-position sample points the wrong way.
        /// </summary>
        private static float HeadingFromVelocity(Vector3 velocity)
        {
            if (Mathf.Abs(velocity.x) < 1e-4f && Mathf.Abs(velocity.z) < 1e-4f) return 0f;
            return (float)(Math.Atan2(velocity.x, velocity.z) * 180.0 / Math.PI);
        }

        private WeatherSnapshot BuildSnapshot(WeatherState model)
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null) return WeatherSnapshot.Unavailable;

            float occlusion = 0f;
            float daylight = 1f;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras != null)
            {
                Vector3 position = cameras.transform.position;
                occlusion = level.GetCloudOcclusion(position);
                daylight = level.GetDaylightFactor(position);
            }

            Vector3 wind = level.windVelocity;
            bool overridden = overrideActive ||
                              Mathf.Abs(live.Conditions - model.Conditions) > OverrideDetection;

            float influence = 0f;
            StormWarning warning = StormWarning.None;
            StormCell warningSource = default;
            if (TryLocalPosition(out float x, out float z))
            {
                StormField.StrongestAt(cells, cellCount, x, z, out influence);
                warning = StormField.WarningAt(cells, cellCount, x, z, out warningSource);
            }

            return new WeatherSnapshot(
                true,
                MissionTime,
                model,
                live,
                wind.x,
                wind.y,
                wind.z,
                occlusion,
                daylight,
                overridden,
                HostAuthority,
                cells,
                cellCount,
                influence,
                warning,
                warningSource);
        }

        private float Haze()
        {
            if (!ModServices.TryGet<IFireSuppressionService>(out IFireSuppressionService fires)) return 0f;
            return WeatherRegimes.Clamp01(fires.ActiveFireCount * HazePerFire);
        }

        private static string MissionIdentity()
        {
            Mission mission = MissionManager.CurrentMission;
            if (mission != null && !string.IsNullOrEmpty(mission.Name)) return mission.Name;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings map = level != null ? level.LoadedMapSettings : null;
            return map != null ? "map:" + map.name : "unknown";
        }
    }
}
