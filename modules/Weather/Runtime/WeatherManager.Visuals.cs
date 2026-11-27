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
    // LateUpdate: clouds, rain, terrain dressing and the published flight-environment snapshot. Update and the native wiring stay in WeatherManager.cs.
    internal sealed partial class WeatherManager
    {
        private void LateUpdate()
        {
            bool hasMissionClock = MissionTimeSource.TryNow(out float weatherTime);
            bool liveWeather = !Application.isBatchMode && LevelInfo.i != null && settings != null &&
                settings.Enabled.Value && hasMissionClock;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera view = cameras != null ? cameras.mainCamera : null;
            Camera renderView = WorldEffectCamera.Main;
            if (liveWeather && view != null && Field != null)
            {
                GlobalPosition position = view.transform.GlobalPosition();
                fieldPosition = new Vector2(position.x, position.z);
                // The field varies over kilometres: sampled 15 times a second the view moves some 20 m between samples at jet speed,
                // far inside one lattice cell, and every consumer (rain, shade, audio, haze) eases its own target. The full sample is
                // a storm-cell and cumulus-lattice walk; once per frame it was a good part of this method.
                if (Time.unscaledTime >= nextLocalSample)
                {
                    nextLocalSample = Time.unscaledTime + 1f / 15f;
                    localWeather = Field.Sample(position.x, position.z);
                    localRainIntensity = Mathf.Clamp01(localWeather.RainRate / 20f);
                }
            }
            float atmosHaze = !SkyYielded && settings != null ? settings.AtmosphericHaze.Value : 0f;
            // Clouds, rain and terrain must read the same atmosphere for this frame.
            if (liveWeather) { UpdateRainSystems(weatherTime); sunDisc.Tick(LevelInfo.i, renderView, viewTransmission); }
            else { environmentSnapshot = default; TeardownRainSystems(); atmosphere.Restore(); HighAltitudeSky.Clear(); sunDisc.Release(); }
            if (!Application.isBatchMode && settings != null && settings.Enabled.Value) heatHaze.Update(Time.unscaledTime);
            else heatHaze.Restore();
            sunShafts.Tick(LevelInfo.i, renderView, liveWeather && settings.SunShaftsEnabled.Value && !SkyYielded, viewTransmission, atmosHaze);
            bool wantClouds = liveWeather && settings.CinematicCloudsEnabled.Value && !SkyYielded;
            if (wantClouds)
            {
                flightClouds ??= new WeatherVolumeDressing(logger);
                flightClouds.Update(LevelInfo.i, Field, renderView,
                    weatherTime, settings.CloudHalfResolution.Value,
                    settings.CloudTemporalUpdate.Value, settings.CloudSteps.Value, settings.CloudFarSteps.Value,
                    lightning.FlashNow, lightning.FlashA, lightning.FlashB,
                    RainVisualsEnabled && (!forcedRainIntensity.HasValue || forcedRainIntensity.Value > .001f));
                if (flightClouds.Active)
                {
                    if (clouds.Applied) clouds.Restore();
                    clouds.SetNativeHidden(LevelInfo.i, true);
                }
                else
                {
                    clouds.SetNativeHidden(null, false);
                    clouds.Apply(LevelInfo.i, localWeather.CoreDepth);
                }
            }
            else
            {
                flightClouds?.Restore();
                clouds.Restore();
                HighAltitudeSky.Clear();
            }
            float skyHeight = renderView != null ? renderView.transform.position.GlobalY() : 0f;
            float skyHaze = Mathf.Clamp01(Mathf.InverseLerp(80f, 10f, localWeather.VisibilityKm) +
                atmosHaze * 0.45f * Mathf.Exp(-Mathf.Max(0f, skyHeight) / 2500f));
            skyAtmosphere.Tick(LevelInfo.i, renderView, wantClouds && flightClouds != null && flightClouds.Active,
                skyHaze, atmosphere.LastNightLight);
            Trace();

            LevelInfo terrainLevel = LevelInfo.i;
            bool wetTerrain = liveWeather && settings.TerrainRainEnabled.Value;
            bool detailTerrain = !Application.isBatchMode && settings != null && settings.TerrainDetailEnabled.Value &&
                terrainLevel != null && terrainLevel.LoadedMapSettings != null && view != null &&
                GameManager.gameState != GameState.Encyclopedia;
            if (wetTerrain || detailTerrain)
            {
                WeatherLight groundLight = liveWeather
                    ? new WeatherLight(sunDirection, terrainSunColor, false) : WeatherLighting.Resolve(terrainLevel);
                terrainRain.SetLighting(groundLight.Direction, groundLight.Colour, RenderSettings.fogColor * lightLevel,
                    RenderSettings.fogDensity, Time.time); // render-only: the ripple phase follows the game's time scale
                terrainRain.Update(terrainLevel.LoadedMapSettings != null ? terrainLevel.LoadedMapSettings.transform : null,
                    view, wetTerrain ? EffectiveRainIntensity() : 0f, Time.deltaTime,
                    wetTerrain ? Field : null, weatherTime, wetTerrain ? forcedRainIntensity : null,
                    wetTerrain, detailTerrain, detailTerrain ? SurfaceShaderBundle.Terrain : null,
                    detailTerrain ? SurfaceShaderBundle.Detail : null,
                    WeatherClimate.Local != null && Field != null ? localWeather.Temperature : (float?)null);
            }
            else terrainRain.Reset();
            if (!liveWeather) atmosphere.Restore();
            PublishEnvironment(cameras, view, weatherTime, liveWeather);
        }

        private void PublishEnvironment(CameraStateManager cameras, Camera camera, float missionTime, bool active)
        {
            if (!active || camera == null || cameras == null || Field == null)
            {
                environmentSnapshot = default;
                publishedMissionTime = -1f;
                return;
            }
            // Replays and mission-clock resets cannot reuse a previous atmosphere binding.
            if (publishedMissionTime >= 0f && (missionTime < publishedMissionTime ||
                missionTime - publishedMissionTime > 2.5f))
                environmentGeneration = environmentGeneration == int.MaxValue ? 1 : environmentGeneration + 1;
            publishedMissionTime = missionTime;
            Aircraft followed = cameras.followingUnit as Aircraft;
            environmentSnapshot = new FlightEnvironmentSnapshot(Time.frameCount, environmentGeneration,
                followed != null ? followed.GetInstanceID() : 0, camera.GetInstanceID(), missionTime,
                cameras.currentState == cameras.cockpitState, true, viewPrecipitation, viewDensity,
                viewCondensation, rainExposure, viewTemperature, viewDewpoint,
                currentWind,
                Mathf.Max(0f, localWeather.Gust), Mathf.Clamp01(localWeather.Turbulence),
                viewTransmission, cloudShade);
        }

        private void UpdateRainSystems(float missionTime)
        {
            // Regional fronts and cells supply local rain; manual override may force it.
            float rainIntensity;

            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera currentCam = cameras != null ? cameras.mainCamera : null;
            if (currentCam == null) { TeardownRainSystems(); return; }
            bool isCockpit = cameras.currentState == cameras.cockpitState;
            GlobalPosition cloudPosition = currentCam.transform.GlobalPosition();
            viewDensity = flightClouds != null && flightClouds.Active
                ? flightClouds.DensityAt(cloudPosition.x, cloudPosition.y, cloudPosition.z)
                : clouds.InCloud(LevelInfo.i) ? 0.5f : 0f;
            viewTransmission = flightClouds != null && flightClouds.Active
                ? flightClouds.TransmissionAt(cloudPosition.x, cloudPosition.y, cloudPosition.z)
                : Mathf.Clamp01(1f - LevelInfo.i.GetCloudOcclusion(currentCam.transform.position));
            WeatherPoint airPoint = localWeather;
            if (Field == null)
            {
                airPoint.RainRate = WeatherForecast.RainIntensityFromConditions(currentConditions) * 20f;
                airPoint.CloudBase = currentCloudHeight;
                airPoint.CloudTop = currentCloudHeight + 1500f;
                airPoint.RainBase = airPoint.CloudBase;
                airPoint.RainTop = airPoint.CloudTop;
                airPoint.Temperature = 15f;
                airPoint.Dewpoint = 6f;
            }
            // Under a deck the air goes dim and grey; above it, the light is back.
            float shadeTarget = Field != null ? OvercastLook.Shade(localWeather.SkyCover, cloudPosition.y, localWeather.NativeDeckTop) : 0f;
            cloudShade = Mathf.MoveTowards(cloudShade, shadeTarget, Time.deltaTime * 0.1f);
            bool underwaterView = currentCam.transform.position.y < Datum.LocalSeaY;
            bool hazeEnabled = settings == null || settings.RainAtmosphereEnabled.Value;
            FlightWeatherAirMass airMass = FlightWeatherAirMass.Evaluate(airPoint,
                cloudPosition.y, viewDensity, forcedRainIntensity);
            viewTemperature = airPoint.Temperature - Mathf.Max(0f, cloudPosition.y) * 0.0065f;
            viewDewpoint = Mathf.Min(viewTemperature, airPoint.Dewpoint - Mathf.Max(0f, cloudPosition.y) * 0.002f);
            // On a map with a climate the precipitation at the viewer turns to snow below about 1 C (0 on a vanilla map, where every factor below is then exactly 1).
            float snow = WeatherClimate.SnowNow(viewTemperature);
            viewSnow = snow;
            rainIntensity = airMass.Rain;
            rainIntensity = Mathf.Clamp01(rainIntensity * RainVisualMath.GustFactor(missionTime, missionSeed));
            float incomingRain = rainIntensity;
            cloudMoisture = Mathf.MoveTowards(cloudMoisture,
                airMass.CloudMoisture, Time.deltaTime * 0.65f);
            float visualIntensity = RainVisualsEnabled ? rainIntensity : 0f;
            lastVisualIntensity = visualIntensity;

            Aircraft localAircraft = cameras.followingUnit as Aircraft;
            if (localAircraft == null && (!GameManager.GetLocalAircraft(out localAircraft) || localAircraft == null))
            {
                if (GameManager.GetLocalPlayer<NuclearOption.Networking.Player>(out var localPlayer) && localPlayer != null)
                {
                    localAircraft = localPlayer.Aircraft;
                }
            }
            // Riding along in another aircraft's cockpit gets the same canopy treatment.
            if (localAircraft == null && isCockpit && cameras.followingUnit is Aircraft followed && !followed.disabled)
                localAircraft = followed;

            // Nuclear Option detaches cockpit parts from the aircraft hierarchy at runtime.
            Transform cockpitRoot = localAircraft != null && localAircraft.cockpit != null
                ? localAircraft.cockpit.transform : null;
            Camera glassCamera = cameras.cockpitCamRender != null && cameras.cockpitCamRender.enabled
                ? cameras.cockpitCamRender : currentCam;

            Vector3 airVel = localAircraft != null && localAircraft.rb != null ? localAircraft.rb.velocity : Vector3.zero;
            float ias = localAircraft != null ? localAircraft.speed : airVel.magnitude;

            // Shelter and ground distance share a four-Hz check. Ownship canopy receives
            // patter; an actual roof leaves a quiet, muffled bed of outside weather.
            if (Time.unscaledTime >= nextShelterCheck)
            {
                nextShelterCheck = Time.unscaledTime + 0.25f;
                int hits = Physics.RaycastNonAlloc(currentCam.transform.position, Vector3.up, shelterHits,
                    80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                sheltered = HasShelter(shelterHits, hits, localAircraft != null ? localAircraft.transform : null,
                    cockpitRoot);
                heightAboveGround = Mathf.Max(0f, currentCam.transform.position.y - Datum.LocalSeaY);
                if (Physics.Raycast(currentCam.transform.position, Vector3.down, out RaycastHit groundHit,
                    8000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                    heightAboveGround = Mathf.Min(heightAboveGround, groundHit.distance);
            }
            rainExposure = Mathf.MoveTowards(rainExposure, sheltered ? 0f : 1f, Time.deltaTime * 2f);
            // Cloud buffet: view-only cockpit shake (vanilla AddShake, which also drives the
            // cockpit rattle). Adding target * decay * dt settles at target against vanilla's
            // 5/s and 4/s decay. Airspeed and the cloud's convection scale it (sheets ride
            // nearly smooth, cumulus and storm cores jolt); density only fades it in.
            cloudBuffet = 0f;
            // Punching through a cloud face: a short patter swell and one jolt, both ways.
            bool inCloud = wasInCloud ? viewDensity > 0.03f : viewDensity > 0.06f;
            if (inCloud != wasInCloud && isCockpit && localAircraft != null && ias > 60f)
            {
                cloudPunch = Mathf.Clamp01(ias / 200f) * (inCloud ? 1f : 0.75f);
                cameras.cockpitState.AddShake(cloudPunch * 0.44f, cloudPunch * 0.31f);
            }
            wasInCloud = inCloud;
            cloudPunch = Mathf.MoveTowards(cloudPunch, 0f, Time.deltaTime * 1.2f);
            if (isCockpit && localAircraft != null && Time.deltaTime > 0f)
            {
                // Squared noise: a high wing loading rides gusts as sharp jolts, not a steady shake.
                float jolt = Mathf.PerlinNoise(missionTime * 1.6f, 0.37f);
                float bump = 0.4f + 1.9f * jolt * jolt;
                float buffet = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.5f, viewDensity)) *
                    Mathf.Clamp01(ias / 200f) * 0.8f * RainVisualMath.CloudRoughness(
                        Mathf.Max(localWeather.ClusterCover, localWeather.CellCover), localWeather.CoreDepth,
                        localWeather.Turbulence) * bump;
                cloudBuffet = buffet;
                if (buffet > 0.005f)
                    cameras.cockpitState.AddShake(buffet * 5f * Time.deltaTime, buffet * 0.8f * 4f * Time.deltaTime);
            }
            rainIntensity *= rainExposure;
            visualIntensity *= rainExposure;
            viewPrecipitation = rainIntensity;
            viewCondensation = cloudMoisture * rainExposure;
            // Snow does not bead on the glass: only the liquid share wets it, the rest feeds the frost.
            float liquidRain = rainIntensity * (1f - snow);
            float glassMoisture = Mathf.Max(liquidRain, viewCondensation);

            // Haze first: everything below reads the fog colour it produces.
            float viewHeight = currentCam.transform.position.GlobalY();
            float atmosHaze = !SkyYielded && settings != null ? settings.AtmosphericHaze.Value : 0f;
            if (hazeEnabled)
                atmosphere.Apply(RainVisualsEnabled
                    ? rainIntensity : 0f, underwaterView, cloudShade, LevelInfo.i != null ? LevelInfo.i.sun : null,
                    LevelInfo.i != null && LevelInfo.i.moon != null ? LevelInfo.i.moon.intensity : 0f,
                    DarkNightsEnabled, viewHeight, WeatherClimate.Local != null ? WeatherClimate.Local.hazeScale : 1f, atmosHaze);
            else atmosphere.Restore();
            // From very high up: the curved Earth past the far plane, and cloud layers that stop at its horizon.
            HighAltitudeSky.Publish(viewHeight, LevelInfo.i != null ? LevelInfo.i.sun : null);
            UpdateLighting();

            // Night sky: occasional aurora on clear nights, and sprites and
            // jets that the lightning below fires over distant night storms. A strategic burst owns
            // the sky for the rest of the mission, so ordinary storm displays stand down.
            bool nuclear = NuclearAftermath.Active;
            bool skyPhenomena = settings.SkyPhenomenaEnabled.Value && !nuclear;
            lightning.Phenomena = skyPhenomena ? phenomena : null;
            if (skyPhenomena)
            {
                uint skySeed = unchecked((uint)missionSeed);
                float auroraNight = WeatherMath.Hash01(skySeed, 77, 1) < 0.2f ? 0.4f + 0.45f * WeatherMath.Hash01(skySeed, 78, 1) : 0f;
                float sunUp = LevelInfo.i != null && LevelInfo.i.sun != null ? -LevelInfo.i.sun.transform.forward.y : 1f;
                Light nightMoon = LevelInfo.i != null ? LevelInfo.i.moon : null;
                float moonlight = nightMoon != null && nightMoon.isActiveAndEnabled && -nightMoon.transform.forward.y > 0f ? nightMoon.intensity : 0f;
                phenomena.Tick(currentCam, sunUp, Mathf.Clamp01(1f - localWeather.SkyCover), auroraNight, skySeed, missionTime, moonlight);
            }
            else phenomena.Reset();

            // Storms remain visible/audible from dry air; they do not depend on a rain emitter.
            bool bolts = settings.LightningEnabled.Value && !SkyYielded && !nuclear;
            bool thunder = settings.RainAudioEnabled.Value && !SkyYielded && !nuclear;
            // A climate with no storms (the arctic) has no lightning either, whatever cell the tape still holds.
            if (nuclear)
            {
                if (!lightningSuspended)
                {
                    lightning.Suspend();
                    strikesAtSuspension = lightning.Strikes;
                    lightningSuspended = true;
                }
            }
            else if ((bolts || thunder) && WeatherClimate.Storms(WeatherClimate.Id(WeatherClimate.Local)))
            {
                lightningSuspended = false;
                lightning.Tick(Field, missionTime, cloudPosition, currentCam,
                    settings.ReducedFlashes.Value || !bolts,
                    thunder, isCockpit);
            }
            else
            {
                lightningSuspended = false;
                lightning.Reset();
            }

            float cloudMist = RainVisualsEnabled ? cloudMoisture * rainExposure : 0f;
            bool wantSystems = visualIntensity > 0.02f || canopyWetness > 0.001f || cloudMist > 0.02f ||
                (RainVisualsEnabled && settings.CanopyRainEnabled.Value && glassMoisture > 0.001f) ||
                (settings.RainAudioEnabled.Value && (incomingRain > 0.02f || (isCockpit && cloudMoisture > 0.02f)));
            if (rainRoot == null && !wantSystems) return;
            if (rainRoot == null) EnsureRainSystems();

            int aircraftId = localAircraft != null ? localAircraft.gameObject.GetInstanceID() : 0;
            if (aircraftId != canopyAircraftId)
            {
                canopyAircraftId = aircraftId;
                canopySurfaces = null;
                canopyWetness = 0f;
                CanopyGlassResolver.ResetForScene();
                canopyShader.Detach();
                canopyShaderLogged = false;
            }
            // Discovery uses the cockpit eye, never the orbit camera far behind the aircraft.
            if (isCockpit && localAircraft != null)
                canopySurfaces = CanopyGlassResolver.Resolve(cockpitRoot, glassCamera.transform.position, glassCamera.cullingMask);
            bool hasGlass = canopySurfaces != null && canopySurfaces.Count > 0;
            bool canopyEnabled = settings != null && RainVisualsEnabled && settings.CanopyRainEnabled.Value;
            bool shaderRequested = canopyEnabled && settings.CanopyShaderEnabled.Value && hasGlass;
            canopyWetness = canopyEnabled && hasGlass
                ? CanopyShaderParams.Wetness(canopyWetness, glassMoisture, ias / 250f, Time.deltaTime) : 0f;

            if (rainEmitter != null)
            {
                rainEmitter.SetTint(RenderSettings.fogColor);
                rainEmitter.UpdateRain(isCockpit ? airVel : Vector3.zero, currentWind, visualIntensity, currentCam,
                    settings != null ? settings.RainDensity.Value : 1f, 1f, lightLevel, cloudMist, snow);
            }
            // Impact sound follows incoming water, independently of optional glass rendering.
            lastAudioCockpit = isCockpit;
            lastAudioSpeed = cameras.followingUnit is Aircraft ? ias : 0f;
            rainSound?.UpdateAudio(incomingRain * (1f - 0.85f * snow), Mathf.Max(cloudMoisture, cloudPunch), glassMoisture, isCockpit,
                settings.RainAudioEnabled.Value && !underwaterView, cameras.followingUnit is Aircraft ? ias : 0f,
                heightAboveGround, rainExposure);
            // The sim remains current outside cockpit view, but submits at most 30 blits/s per pane.
            Vector3 glassAirflow = currentWind - airVel;
            float speedNorm = Mathf.Clamp01(glassAirflow.magnitude / 250f);
            if (!shaderRequested || canopyWetness <= 0.001f) canopyShader.ClearWater();
            else
                // Cloud droplets feed their own thin-film channel; only falling rain seeds beads.
                canopyShader.UpdateSim(canopySurfaces, liquidRain, speedNorm,
                    glassAirflow, Time.deltaTime, viewCondensation,
                    CanopyShaderParams.Evaporation(viewTemperature, viewDewpoint));
            bool shaderDrawn = false;
            if (shaderRequested)
                canopyShader.SetColdMoisture(viewTemperature, glassMoisture, Time.deltaTime, rainIntensity * snow);
            else canopyShader.Detach();
            if (isCockpit && shaderRequested)
            {
                canopyShader.SetLighting(sunDirection, sunColor, RenderSettings.fogColor, sceneRefraction);
                canopyScene.Arm(glassCamera);
                canopyShader.SetScene(canopyScene.Ready(glassCamera) ? canopyScene.Texture : null);

                shaderDrawn = canopyShader.Draw(canopySurfaces, glassCamera, canopyWetness, lightLevel);

                if (shaderDrawn && !canopyShaderLogged) LogCanopyShaderOnce("Canopy rain active on " + canopySurfaces.Count + " glass submeshes; native materials preserved.");
            }
            canopyShaderActive = shaderDrawn;
            if (!shaderDrawn) canopyScene.Dispose(); // dry or outside: no per-frame copy
            if (shaderDrawn && canopyDrawnFrames < int.MaxValue) canopyDrawnFrames++;
            if (canopyDrops != null)
                canopyDrops.UpdateDrops(hasGlass ? canopySurfaces[0] : default,
                    isCockpit && !shaderDrawn ? canopyWetness : 0f);


        }

        internal static bool HasShelter(RaycastHit[] hits, int count, Transform aircraftRoot, Transform cockpitRoot)
        {
            // A full buffer can contain only ownship parts. Overflow alone is not a roof.
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i].collider;
                if (collider != null && (aircraftRoot == null || !collider.transform.IsChildOf(aircraftRoot)) &&
                    (cockpitRoot == null || !collider.transform.IsChildOf(cockpitRoot))) return true;
            }
            return false;
        }
    }
}
