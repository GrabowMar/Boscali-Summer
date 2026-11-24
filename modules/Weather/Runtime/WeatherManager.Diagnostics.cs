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
    // Automation readouts, sim fixtures and the transition trace.
    internal sealed partial class WeatherManager
    {
        /// <summary>Live rain state for the automation hook; lands in the sim report.</summary>
        internal Dictionary<string, object> DebugReadout()
        {
            WeatherLight cloudLight = WeatherLighting.ResolveCloud(LevelInfo.i);
            var state = new Dictionary<string, object>
            {
                { "conditions", currentConditions },
                { "cloudHeight", currentCloudHeight },
                { "forcedRain", forcedRainIntensity ?? -1f },
                { "rainExposure", rainExposure },
                { "heightAboveGround", heightAboveGround },
                { "rainSystems", rainRoot != null ? 1 : 0 },
                { "streakAlive", rainEmitter != null ? rainEmitter.AliveParticles : -1 },
                { "streakPlaying", rainEmitter != null && rainEmitter.Playing ? 1 : 0 },
                { "streakRate", rainEmitter != null ? rainEmitter.EmissionNow : -1f },
                { "streakApparent", rainEmitter != null ? rainEmitter.ApparentSpeedNow : -1f },
                { "streakShader", rainEmitter != null ? rainEmitter.ShaderNow : "none" },
                { "streakIntensity", lastVisualIntensity },
                { "lightNow", lightLevel },
                { "ambientNow", LevelInfo.i != null ? LevelInfo.i.GetAmbientLight() : -1f },
                { "sunNow", LevelInfo.i != null && LevelInfo.i.sun != null ? LevelInfo.i.sun.intensity : -1f },
                { "missionNow", MissionTimeSource.TryNow(out float mtForReadout) ? mtForReadout : -1f },
                { "todNow", LevelInfo.i != null ? LevelInfo.i.timeOfDay : -1f },
                { "canopySurfaces", canopySurfaces != null ? canopySurfaces.Count : 0 },
                { "canopyWetness", canopyWetness },
                { "canopyShader", canopyShaderActive ? 1 : 0 },
                { "canopyDrawnFrames", canopyDrawnFrames },
                { "cockpitView", SceneSingleton<CameraStateManager>.i != null && SceneSingleton<CameraStateManager>.i.currentState == SceneSingleton<CameraStateManager>.i.cockpitState ? 1 : 0 },
                { "sceneRefraction", sceneRefraction ? 1 : 0 },
                { "atmosphere", atmosphere.Applied ? 1 : 0 },
                { "fogMultiplier", atmosphere.LastFogMultiplier },
                { "fogDensity", RenderSettings.fogDensity },
                { "darkNights", DarkNightsEnabled ? 1 : 0 },
                { "nightLight", atmosphere.LastNightLight },
                { "moonIntensity", LevelInfo.i != null && LevelInfo.i.moon != null ? LevelInfo.i.moon.intensity : 0f },
                { "ambientIntensity", RenderSettings.ambientIntensity },
                { "ambientSkyR", RenderSettings.ambientSkyColor.r },
                { "ambientSkyG", RenderSettings.ambientSkyColor.g },
                { "ambientSkyB", RenderSettings.ambientSkyColor.b },
                { "cinematicClouds", clouds.Applied || (flightClouds != null && flightClouds.Active) ? 1 : 0 },
                { "flightClouds", flightClouds != null && flightClouds.Active ? 1 : 0 },
                { "nativeCloudsHidden", clouds.NativeHidden ? 1 : 0 },
                { "cloudBodies", flightClouds?.BodyCount ?? 0 },
                { "cloudRainCurtainsEnabled", flightClouds != null && flightClouds.Active && flightClouds.RainCurtainsEnabled ? 1 : 0 },
                { "nativeSunActive", LevelInfo.i != null && LevelInfo.i.sun != null && LevelInfo.i.sun.isActiveAndEnabled ? 1 : 0 },
                { "cloudSunElevation", cloudLight.Direction.y },
                { "cloudLightPeak", cloudLight.Colour.maxColorComponent },
                { "cloudLightIsMoon", cloudLight.IsMoon ? 1 : 0 },
                { "cloudDeck", flightClouds != null && flightClouds.DeckActive ? 1 : 0 },
                { "weatherMapUpdates", flightClouds?.MapUpdates ?? 0 },
                { "cloudShadowActive", flightClouds != null && flightClouds.ShadowActive ? 1 : 0 },
                { "cloudShadowUpdates", flightClouds?.ShadowUpdates ?? 0 },
                { "cloudShadowFailure", flightClouds?.ShadowFailure ?? string.Empty },
                { "weatherState", Field != null ? SkyMath.ClassOf(Field.RowAt(0f, 0f)).ToString() : "-" },
                { "weatherSets", fieldKey?.Sets ?? 0 },
                { "captured", IsCaptured ? 1 : 0 },
                { "captureMapLoaded", capturedMap != null ? 1 : 0 },
                { "keySeed", fieldKey != null ? (double)fieldKey.Seed : -1.0 },
                { "keySeedIsLegacy", fieldKey != null && fieldKey.Seed == 16768u ? 1 : 0 },
                { "keyClass", fieldKey != null ? (int)fieldKey.MissionClass : -1 },
                { "keyOpeningBase", fieldKey != null ? fieldKey.OpeningBase : -1f },
                { "keyHeading", fieldKey != null ? fieldKey.HeadingDeg : -1f },
                { "keyDynamic", fieldKey != null && fieldKey.Dynamic ? 1 : 0 },
                { "keyEpoch", fieldKey != null ? fieldKey.Epoch : -1f },
                { "keyOffset", fieldKey != null ? fieldKey.Offset : -1f },
                { "keySpeed", fieldKey != null ? fieldKey.SystemSpeed : -1f },
                { "baselineConditions", baselineConditions },
                { "baselineCloudHeight", baselineCloudHeight },
                { "baselineWindSpeed", baselineWindSpeed },
                { "protocol", (int)WeatherNet.ProtocolVersion },
                { "fieldCover", localWeather.LowCover },
                { "skyCover", localWeather.SkyCover },
                { "classSampled", localWeather.ClassSampled },
                { "nativeDeck", localWeather.NativeDeck },
                { "nativeRole", GameAccess.IsServer() ? "server" : "client" },
                { "ownNatives", ownNatives ? 1 : 0 },
                { "nativeConditions", LevelInfo.i != null ? LevelInfo.i.conditions : -1f },
                { "targetConditions", targetConditions },
                { "sunFlips", sunFlips },
                { "syncSeen", syncSeen },
                { "syncAdopted", syncAdopted },
                { "syncIgnored", syncIgnored },
                { "syncBuffered", syncBuffered },
                { "syncPending", pendingSync.Has ? 1 : 0 },
                { "tapeMoving", Field != null && Field.Moving ? 1 : 0 },
                { "settingsTapeMoves", settings != null && settings.TapeMoves.Value ? 1 : 0 },
                { "settingsPace", settings != null ? settings.Pace.Value : -1 },
                { "nativeWindSpeed", new Vector3(currentWind.x, 0f, currentWind.z).magnitude },
                { "nativeTurbulence", currentTurbulence },
                { "nativeWindFromDeg", Mathf.Repeat(Mathf.Atan2(currentWind.x, currentWind.z) * Mathf.Rad2Deg + 180f, 360f) },
                // How far the native wind has veered from the key's heading (the mission wind's own direction), shortest way round; 0 when the mission is calm (the key's heading is then a seeded one).
                { "nativeWindVeerDeg", fieldKey != null && baselineWindSpeed > 0.5f ? Mathf.DeltaAngle(fieldKey.HeadingDeg, Mathf.Repeat(Mathf.Atan2(currentWind.x, currentWind.z) * Mathf.Rad2Deg + 180f, 360f)) : 0f },
                { "mapWindowSwaps", flightClouds?.MapUpdates ?? 0 },
                { "tapeId", fieldKey != null ? (double)fieldKey.TapeId : -1.0 },
                { "story", fieldKey != null && fieldKey.Story < WeatherStories.Count ? WeatherStories.Name(fieldKey.Story) : "auto" },
                { "skyPin", fieldKey != null && fieldKey.SkyPin < SkyLibrary.Count ? fieldKey.SkyPin + 1 : 0 },
                { "skyName", fieldKey != null && fieldKey.SkyPin < SkyLibrary.Count ? SkyLibrary.Get(fieldKey.SkyPin).Name : "-" },
                { "metar", Field != null ? WeatherMetar.Line(localWeather) : "-" },
                { "markerCount", Field?.TapeSource?.Markers.Count ?? 0 },
                { "nextFrontKm", NextMarkerKm() },
                { "localFrontKind", LocalMarkerKind() },
                { "activeCellCount", Field?.ActiveCellCount(0f, 0f, 40000f) ?? 0 },
                { "activeGroupCount", Field?.ActiveGroupCount(0f, 0f, 40000f) ?? 0 },
                { "cloudImmersion", cloudMoisture },
                { "cloudBuffet", cloudBuffet },
                { "viewCloudDensity", viewDensity },
                { "viewPrecipitation", viewPrecipitation },
                { "viewCondensation", viewCondensation },
                { "viewTemperatureC", viewTemperature },
                { "viewDewpointC", viewDewpoint },
                { "viewSnow", viewSnow },
                { "keyClimate", fieldKey != null ? (int)fieldKey.Climate : -1 },
                { "mapClimate", (int)WeatherClimate.Id(WeatherClimate.Local) },
                { "viewLightTransmission", viewTransmission },
                { "sunDiscRed", sunDisc.Shown.r },
                { "sunDiscBlue", sunDisc.Shown.b },
                { "sunShaftsAge", Time.frameCount - sunShafts.ExecutedFrame },
                { "viewWidth", SceneSingleton<CameraStateManager>.i?.mainCamera != null ? SceneSingleton<CameraStateManager>.i.mainCamera.pixelWidth : 0 },
                { "viewHeight", SceneSingleton<CameraStateManager>.i?.mainCamera != null ? SceneSingleton<CameraStateManager>.i.mainCamera.pixelHeight : 0 },
                { "environmentGeneration", environmentGeneration },
                { "environmentFrame", environmentSnapshot.FrameIndex },
                { "cloudTargetWidth", flightClouds?.TargetWidth ?? 0 },
                { "cloudTargetHeight", flightClouds?.TargetHeight ?? 0 },
                { "cloudTargetBytes", flightClouds?.TargetBytes ?? 0L },
                { "probeFaces", Visuals.CloudProbePass.FacesDrawn },
                { "feedCloudFrames", Visuals.CloudProbePass.FeedFramesDrawn },
                { "lightningStrikes", lightning.Strikes },
                { "lightningFlash", lightning.FlashNow },
                { "lightningSuppressed", LightningSuppressed ? 1 : 0 },
                { "lightningStrikesSinceSuspension", lightningSuspended ? Mathf.Max(0, lightning.Strikes - strikesAtSuspension) : 0 },
                { "thunderQueued", lightning.QueuedThunder },
                { "thunderVoices", lightning.ThunderVoices },
                { "auroraLevel", phenomena.AuroraLevel },
                { "auroraParticles", phenomena.AuroraParticles },
                { "spritesFired", phenomena.SpritesFired },
                { "rainAudioReady", rainSound != null && rainSound.ClipsReady ? 1 : 0 },
                { "rainAudioRouted", rainSound != null && rainSound.IsRouted ? 1 : 0 },
                { "rainAudioPlaying", rainSound != null && rainSound.IsPlaying ? 1 : 0 },
                { "rainAudioClip", rainSound != null ? rainSound.ActiveClipName : "" },
                { "rainAudioEffectsMixer", rainSound != null && rainSound.EffectsRouted },
                { "rainRushVolume", rainSound != null ? rainSound.RushVolume : 0f },
                { "rainPatterVolume", rainSound != null ? rainSound.PatterVolume : 0f },
                { "rainExteriorCutoff", rainSound != null ? rainSound.ExteriorCutoff : 22000f },
                { "rainCanopyCutoff", rainSound != null ? rainSound.CanopyCutoff : 22000f },
                { "terrainSurfaces", terrainRain.SurfaceCount },
                { "terrainWetness", terrainRain.Wetness },
            };
            FxBus.Describe(state);
            return state;
        }

        internal void LogAutomation(string message) => logger?.LogInfo("[WeatherAutomation] " + message);

        internal Dictionary<string, object> ProbeCloud(float x, float z, float radius, float minRain)
        {
            if (flightClouds == null || !flightClouds.Active)
                return new Dictionary<string, object> { { "ok", false }, { "found", false }, { "error", "displayed cloud maps unavailable" } };
            bool found = flightClouds.ProbeCloud(x, z, radius, minRain, out Vector3 point, out float density);
            WeatherPoint weather = flightClouds.SampleDisplayedWeather(point.x, point.z);
            Vector3 top = new GlobalPosition(point.x, 10000f, point.z).ToLocalPosition();
            float ground = Physics.Raycast(top, Vector3.down, out RaycastHit hit, 20000f, PhysicsLayers.StaticsMask)
                ? Mathf.Max(0f, hit.point.GlobalY()) : 0f;
            found &= point.y > ground + 250f;
            return new Dictionary<string, object> { { "ok", true }, { "found", found }, { "x", point.x },
                { "y", point.y }, { "z", point.z }, { "density", density }, { "groundY", ground },
                { "seatAltitudeAGL", Mathf.Max(0f, point.y - ground) }, { "rainRate", weather.RainRate } };
        }

        /// <summary>Sims: a held tape at a pinned place (spec 10.4). <paramref name="story"/> / <paramref name="pin"/> 255 = auto / none; <paramref name="offsetKm"/> is D at the origin.</summary>
        internal bool SetFixtureTape(uint seed, byte story, byte pin, float offsetKm, bool dynamic)
        {
            if (!GameAccess.IsServer() || !isManualOverride || !IsCaptured || !RequireClock("fixture tape", out float missionTime)) return false;
            missionSeed = unchecked((int)seed);
            fieldKey = NewFieldKey(missionTime, dynamic, fieldKey.MissionClass, seed, fieldKey.Climate).WithStory(story).WithSkyPin(pin).WithOffset(offsetKm * 1000f);
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            BroadcastSync(missionTime);
            return true;
        }

        internal bool SetFixtureField(uint seed, float modelAge, WeatherRegimeType? fixtureState = null)
        {
            if (!GameAccess.IsServer() || !isManualOverride || !IsCaptured ||
                !RequireClock("fixture field", out float missionTime)) return false;
            missionSeed = unchecked((int)seed);
            // Spec 10.4: the class pins its first sky, the seed is the Seed, and modelAge is a position on the held tape (Offset = v x age).
            fieldKey = ForceWeatherMap.Key(fieldKey, missionTime, fixtureState, null, null, seed, modelAge);
            nextFieldUpdate = 0f;
            lastForecastSampleTime = -999f;
            BroadcastSync(missionTime);
            return true;
        }

        // Transition trace: one line whenever what the player sees changes source, plus a
        // summary each minute, so a visual glitch leaves evidence in LogOutput.log.
        private string traceState;
        private float nextTraceLook, nextLocalSample;
        private float nextTraceSummary;
        private int traceSignature;
        private int traceLines;

        private void Trace()
        {
            if (logger == null || Application.isBatchMode || traceLines > 400) return;
            // Evidence for the log only: four looks a second find a change as surely as sixty, without the field read every frame.
            if (Time.unscaledTime < nextTraceLook) return;
            nextTraceLook = Time.unscaledTime + 0.25f;
            // The state line is only built when something in it changed (or once a minute):
            // building it every frame allocated about half a kilobyte per frame.
            WeatherField traced = Field;
            int signature = (flightClouds == null ? 0 : flightClouds.Active ? 1 : 2) + (clouds.NativeHidden ? 4 : 0) +
                (isManualOverride ? 8 : 0) + ((fieldKey?.GetHashCode() ?? 0) << 4) ^
                (traced == null ? -1 : (int)SkyMath.ClassOf(traced.RowAt(0f, 0f)));
            if (signature == traceSignature && traceState != null && Time.unscaledTime < nextTraceSummary) return;
            traceSignature = signature;
            MissionTimeSource.TryNow(out float missionTime);
            string renderer = flightClouds == null ? "off" :
                flightClouds.Active ? "volume" : "native (" + (flightClouds.HideReason ?? "?") + ")";
            WeatherField live = Field;
            string state = renderer + " | nativeHidden " + clouds.NativeHidden +
                " | key " + (fieldKey == null ? "none" : fieldKey.Seed + "/" + fieldKey.Epoch.ToString("0") +
                    (fieldKey.Dynamic ? " dyn" : " held") + (isManualOverride ? " manual" : "")) +
                " | state " + (live == null ? "-" : SkyMath.ClassOf(live.RowAt(0f, 0f)).ToString());
            bool changed = state != traceState;
            if (!changed && Time.unscaledTime < nextTraceSummary) return;
            traceState = state;
            nextTraceSummary = Time.unscaledTime + 60f;
            traceLines++;
            logger.LogInfo("[Weather] " + (changed ? "state " : "summary ") + "t=" + missionTime.ToString("0") +
                "s " + state +
                " | native cond " + (LevelInfo.i != null ? LevelInfo.i.conditions.ToString("0.00") : "-") +
                " base " + Mathf.RoundToInt(currentCloudHeight) +
                " | local cover " + localWeather.LowCover.ToString("0.00") + " rain cover " + localWeather.RainCover.ToString("0.00") +
                " rain " + EffectiveRainIntensity().ToString("0.00") +
                " | markers " + (live?.TapeSource?.Markers.Count ?? 0) + " cells " + (live?.ActiveCellCount(0f, 0f, 40000f) ?? 0) +
                " | maps " + (flightClouds?.MapUpdates ?? 0) +
                " | precip " + viewPrecipitation.ToString("0.00") + " expo " + rainExposure.ToString("0.00") +
                " cock " + (lastAudioCockpit ? 1 : 0) + " spd " + Mathf.RoundToInt(lastAudioSpeed) +
                (rainSound == null ? " audio none" :
                " audio play " + (rainSound.IsPlaying ? 1 : 0) +
                " vol " + Mathf.Max(rainSound.PatterVolume, rainSound.RushVolume).ToString("0.00") +
                " route " + (rainSound.IsRouted ? 1 : 0) + " clips " + (rainSound.ClipsReady ? 1 : 0)));
        }
    }
}
