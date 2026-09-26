using System;
using BepInEx.Logging;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// Owns the weather key and the one <see cref="WeatherField"/> every renderer reads.
    ///
    /// <para>The host builds the key (seed, schedule rules, gameplay switches, override
    /// keyframes), publishes it, and drives vanilla's synced channels — conditions, cloud height,
    /// wind, turbulence — from the field's regional values, so vanilla's fog, IR, ambient and
    /// SAR keep working. A foreign write to <c>conditions</c> (a mission's ModifyEnvironment
    /// beat) becomes an override keyframe, so rain and cells follow the authored beat.</para>
    ///
    /// <para>Clients take the key from the host and derive everything else. Until a key has
    /// arrived a client renders no weather of its own rather than a wrong one.</para>
    /// </summary>
    internal sealed class SynopticWeather : MonoBehaviour, ISceneService
    {
        private const float WriteInterval = 0.25f;
        private const float ConditionsRate = 0.01f;
        private const float CloudBaseRate = 15f;
        private const float WindRate = 0.3f;
        private const float HeadingRate = 3f;
        private const float TurbulenceRate = 0.02f;
        private const float AdoptEpsilon = 0.02f;
        private const int StrikeBuffer = 64;

        private WeatherSettings settings;
        private ManualLogSource log;
        private WeatherKeyNet net;

        private readonly WeatherField field = new WeatherField();
        private readonly Strike[] strikes = new Strike[StrikeBuffer];
        private string missionIdentity;
        private WeatherKey hostKey;
        private WeatherKey remoteKey;
        private uint sessionSalt;
        private float nextWrite;
        private float lastConditionsWritten = -1f;
        private float previousStrikeTime = float.NaN;

        public WeatherField Field => field;
        public WeatherKey Key { get; private set; }
        public bool Ready => Key != null && field.IsBuilt;

        /// <summary>
        /// True while this engine owns vanilla's sky: dynamic schedule on with a built
        /// field. The environment engine yields its own sky writes (but not its visuals)
        /// while this holds, so the two never fight over the same channels.
        /// </summary>
        public bool Driving => settings != null && settings.DynamicWeather.Value && Ready;
        public bool HostAuthority { get; private set; }
        public float MissionTime { get; private set; }
        public float HourOfDay { get; private set; } = 12f;

        /// <summary>The sky at the camera this frame.</summary>
        public WeatherPoint Local { get; private set; }

        /// <summary>Camera position in map coordinates (x east, z north) and altitude above sea.</summary>
        public Vector3 CameraGlobal { get; private set; }

        /// <summary>Strikes whose time fell inside this frame (read by lightning, audio, HUD).</summary>
        public int StrikeCount { get; private set; }
        public Strike StrikeAt(int index) => strikes[index];

        public WeatherSettings Settings => settings;

        /// <summary>Lightning light this frame (HDR-ish colour, 0 when dark); written by the lightning director.</summary>
        public Color Flash { get; set; }

        /// <summary>
        /// Debug only, client-local: when ≥ 0, the rain rate the renderers are told about at the
        /// camera. Never touches the field, the key or the network.
        /// </summary>
        public float RainPreview
        {
            get => settings != null ? settings.DebugRainPreview.Value : -1f;
            set { if (settings != null) settings.DebugRainPreview.Value = Mathf.Clamp(value, -1f, 150f); }
        }

        private WeatherPoint ApplyPreview(WeatherPoint p)
        {
            if (RainPreview < 0f) return p;
            p.RainRate = RainPreview;
            if (RainPreview > 0f)
            {
                p.Cover = Mathf.Max(p.Cover, 0.9f);
                p.CloudTop = Mathf.Max(p.CloudTop, CameraGlobal.y + 2000f);
                p.Hail = RainPreview >= 90f;
                float extinction = 3.912f / 20f + 0.25f * Mathf.Pow(RainPreview, 0.66f);
                p.VisibilityKm = Mathf.Min(p.VisibilityKm, 3.912f / extinction);
            }
            return p;
        }

        public void Configure(WeatherSettings weatherSettings, WeatherKeyNet network, ManualLogSource logger)
        {
            settings = weatherSettings;
            net = network;
            log = logger;
            sessionSalt = unchecked((uint)Environment.TickCount * 2654435761u);
            net.Configure(ApplyRemoteKey);
        }

        public void ResetForScene()
        {
            missionIdentity = null;
            hostKey = null;
            remoteKey = null;
            Key = null;
            nextWrite = 0f;
            lastConditionsWritten = -1f;
            previousStrikeTime = float.NaN;
            StrikeCount = 0;
            net?.ResetScene();
        }

        /// <summary>Host: blend the sky into a regime now (debug window, scripted beats).</summary>
        public void ForceRegime(WeatherRegime regime)
        {
            if (!HostAuthority || hostKey == null) return;
            hostKey = hostKey.WithOverride(MissionTime, regime);
            log?.LogInfo("[Weather] Forced " + RegimeTable.Name(regime) + " at mission time " + MissionTime.ToString("0"));
        }

        /// <summary>Host: drop every override and return to the seeded chain.</summary>
        public void ReleaseOverrides()
        {
            if (!HostAuthority || hostKey == null) return;
            hostKey = hostKey.WithoutOverrides();
        }

        private void ApplyRemoteKey(WeatherKey key)
        {
            if (key == null) return;
            if (!key.Equals(remoteKey)) log?.LogInfo("[Weather] Received host weather key (seed " + key.Seed + ", " + key.OverrideCount + " override(s)).");
            remoteKey = key;
        }

        private void Update()
        {
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (mission == null || level == null || !MissionManager.IsRunning)
            {
                if (missionIdentity != null) ResetForScene();
                return;
            }

            HostAuthority = GameAccess.IsServer();
            string identity = MissionIdentity(level);
            if (!string.Equals(identity, missionIdentity, StringComparison.Ordinal))
            {
                ResetForScene();
                missionIdentity = identity;
                if (HostAuthority) hostKey = BuildHostKey(identity, level);
            }

            MissionTime = mission.MissionTime;
            HourOfDay = level.timeOfDay;

            if (HostAuthority)
            {
                if (hostKey == null) hostKey = BuildHostKey(identity, level);
                hostKey = ApplyHostRules(hostKey);
                Key = hostKey;
            }
            else
            {
                Key = remoteKey;
            }
            if (Key == null) return;

            Vector2 span = TheaterFrame.Resolve();
            field.Build(Key, MissionTime, span.x * 0.5f, span.y * 0.5f, HourOfDay, HazeScale());

            CameraGlobal = CameraPosition();
            Local = ApplyPreview(field.Sample(CameraGlobal.x, CameraGlobal.z));

            CollectStrikes();

            if (HostAuthority)
            {
                net.Publish(Key);
                if (Time.unscaledTime >= nextWrite)
                {
                    float dt = nextWrite <= 0f ? WriteInterval : Time.unscaledTime - nextWrite + WriteInterval;
                    nextWrite = Time.unscaledTime + WriteInterval;
                    DriveVanilla(level, Mathf.Clamp(dt, 0f, 1f));
                }
            }
        }

        private void CollectStrikes()
        {
            float now = MissionTime;
            if (float.IsNaN(previousStrikeTime) || now < previousStrikeTime || now - previousStrikeTime > 5f)
            {
                previousStrikeTime = now;
                StrikeCount = 0;
                return;
            }
            StrikeCount = LightningSchedule.Collect(field, previousStrikeTime, now, strikes);
            previousStrikeTime = now;
        }

        private WeatherKey BuildHostKey(string identity, LevelInfo level)
        {
            uint seed = unchecked(Deterministic.HashString(identity) ^ sessionSalt);
            byte start = WeatherKey.AutoRegime;
            if (settings.StartRegime.Value > 0) start = (byte)(settings.StartRegime.Value - 1);
            else start = (byte)RegimeTable.FromConditions(level.conditions);
            var key = new WeatherKey(seed, 0f, settings.DynamicWeather.Value, start, Flags());
            log?.LogInfo("[Weather] Sky for '" + identity + "': opens " + RegimeTable.Name(RegimeTable.Clamp(start)) +
                         (key.Dynamic ? ", dynamic" : ", held") + ", seed " + seed + ".");
            lastConditionsWritten = level.conditions;
            return key;
        }

        /// <summary>Keeps the host key in step with the host's own settings as they change.</summary>
        private WeatherKey ApplyHostRules(WeatherKey key)
        {
            WeatherFlags flags = Flags();
            if (key.Flags != flags) key = key.WithFlags(flags);
            if (key.Dynamic != settings.DynamicWeather.Value) key = key.WithSchedule(settings.DynamicWeather.Value, key.StartRegime);
            return key;
        }

        private WeatherFlags Flags()
        {
            WeatherFlags flags = WeatherFlags.None;
            if (settings.StormTurbulence.Value) flags |= WeatherFlags.StormTurbulence;
            if (settings.SensorEffects.Value) flags |= WeatherFlags.SensorEffects;
            if (settings.LightningHazard.Value) flags |= WeatherFlags.LightningHazard;
            return flags;
        }

        private void DriveVanilla(LevelInfo level, float dt)
        {
            // A write we did not make is authored weather: fold it into the schedule.
            if (lastConditionsWritten >= 0f && Mathf.Abs(level.conditions - lastConditionsWritten) > AdoptEpsilon)
            {
                WeatherRegime regime = RegimeTable.FromConditions(level.conditions);
                hostKey = hostKey.WithOverride(MissionTime, regime);
                Key = hostKey;
                log?.LogInfo("[Weather] Adopted authored weather " + level.conditions.ToString("0.00") + " as " +
                             RegimeTable.Name(regime) + " at mission time " + MissionTime.ToString("0") + ".");
                lastConditionsWritten = level.conditions;
                return;
            }

            float conditions = Step(level.conditions, field.MeanCover(4), ConditionsRate * dt);
            level.Networkconditions = conditions;
            lastConditionsWritten = conditions;

            float cloudBase = Step(level.cloudHeight, field.Regime.Params.CloudBase, CloudBaseRate * dt);
            level.NetworkcloudHeight = cloudBase;

            field.MeanWind(out float speed, out float heading, out float turbulence);
            level.SetWindSpeed(Step(level.windSpeed, speed, WindRate * dt));
            level.SetWindTurbulence(Step(level.windTurbulence, turbulence, TurbulenceRate * dt));

            float liveHeading = WeatherMath.VectorToHeading(level.windVelocity.x, level.windVelocity.z);
            float delta = WeatherMath.DeltaAngle(liveHeading, heading);
            float stepped = liveHeading + Mathf.Clamp(delta, -HeadingRate * dt, HeadingRate * dt);
            if (Mathf.Abs(delta) > 0.05f) level.SetWindHeading(WeatherMath.WrapHeading(stepped));
        }

        private static float Step(float current, float target, float maxDelta)
            => Mathf.MoveTowards(current, target, Mathf.Max(maxDelta, 0f));

        private static float HazeScale()
        {
            if (!ModServices.TryGet(out IFireSuppressionService fires) || fires == null) return 1f;
            return 1f - 0.5f * Mathf.Clamp01(fires.ActiveFireCount / 24f);
        }

        private static Vector3 CameraPosition()
        {
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera == null) return Vector3.zero;
            GlobalPosition g = camera.transform.GlobalPosition();
            return new Vector3(g.x, g.y, g.z);
        }

        private static string MissionIdentity(LevelInfo level)
        {
            Mission mission = MissionManager.CurrentMission;
            if (mission != null && !string.IsNullOrEmpty(mission.Name)) return mission.Name;
            MapSettings map = level != null ? level.LoadedMapSettings : null;
            return map != null ? "map:" + map.name : "unknown";
        }
    }
}
