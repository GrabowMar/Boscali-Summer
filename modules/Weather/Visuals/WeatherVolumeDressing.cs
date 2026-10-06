using System;
using System.Threading;
using BepInEx.Logging;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using BoscaliSummer.Core.Contracts;
using System.Collections.Generic;
using BoscaliSummer.Core.Fx;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>One world-anchored volume. Small 2D maps from WeatherField control the
    /// front, cell and cumulus density: a 256-square near map over the flight domain and a
    /// coarse far map (the level of detail beyond the mission area) out to the horizon. The
    /// sky is static between weather steps: each step builds one map pair for its settled
    /// state and crossfades into it on the mission clock.
    ///
    /// <para>Normally the sky is marched at half resolution by <see cref="WeatherCloudPass"/>
    /// and the camera-following cube only composites it. Balanced quality returns to native
    /// clouds if that pass is unavailable; a full-resolution march is an explicit opt-in.</para></summary>
    internal sealed class WeatherVolumeDressing : IClientEffect
    {
        public string EffectId => "clouds";
        public FxBudget Budget => new FxBudget(16 * 1024 * 1024, 3, 0, true);
        public void ReleaseFx() => Restore();
        public void DescribeFx(IDictionary<string, object> state)
        {
            state["fx.clouds.active"] = Active;
            state["fx.clouds.targetBytes"] = TargetBytes;
            state["fx.clouds.width"] = TargetWidth;
            state["fx.clouds.height"] = TargetHeight;
            state["fx.clouds.halfResolution"] = HalfResolution;
            state["fx.clouds.maps"] = MapUpdates;
            state["fx.clouds.reason"] = HideReason ?? string.Empty;
        }
        internal const int NoiseSize = 64;
        internal const int NoiseTexels = NoiseSize * NoiseSize * NoiseSize;
        private const float FarSpanScale = 3f;
        private const float MinFarHalf = 240000f;
        private static readonly int MapTexId = Shader.PropertyToID("_WeatherMapTex");
        private static readonly int ProfileTexId = Shader.PropertyToID("_WeatherProfileTex");
        private static readonly int FarMapTexId = Shader.PropertyToID("_WeatherFarMapTex");
        private static readonly int FarProfileTexId = Shader.PropertyToID("_WeatherFarProfileTex");
        private static readonly int EnvelopeTexId = Shader.PropertyToID("_WeatherEnvelopeTex");
        private static readonly int EnvelopeOnId = Shader.PropertyToID("_WeatherEnvelopeOn");
        private static readonly int MapBlendId = Shader.PropertyToID("_WeatherMapBlend");
        private static readonly int MapTargetId = Shader.PropertyToID("_CloudMapTarget");
        private static byte[] sharedNoise;
        private static int generatingNoise;
        private static Mesh cube;

        private readonly ManualLogSource logger;
        private readonly CloudVolumeUniforms uniforms = new CloudVolumeUniforms();
        private readonly WeatherCloudPass pass = new WeatherCloudPass();
        private readonly Action<Camera, Matrix4x4, Matrix4x4> onRender;
        private int mapStep = -1;
        private float fadeStart, fadeLength;
        private Material material, compositeMaterial;
        private Texture3D noise;
        private readonly MapPair near = new MapPair("Weather", CloudMaps.NearSize);
        private readonly MapPair far = new MapPair("Weather Far", CloudMaps.FarSize);
        // Empty space: the union of the fading and the arriving state's envelopes while a
        // fade runs, the arriving one alone once it has landed.
        private Texture2D envelope;
        private byte[] envelopeShown, envelopeTo;
        private bool envelopeUnionDue, envelopeFinalDue;
        private float nextBlend;
        private float blendedAt = -1f;
        private bool blendUnavailable;
        private WeatherKey mapKey;
        private float volumeBottom, volumeTop = CloudMaps.VolumeTop;
        private float horizonCover, previousHorizonCover;
        private float rainMaximum, previousRainMaximum;
        private bool halfResolution;
        private Vector3 lightDirection = Vector3.up;

        /// <summary>0..1 how deep the camera is inside cloud, smoothed; read by the atmosphere.</summary>
        private float previousBottom, previousTop;
        private int mapUpdates;
        private GameObject root;
        private MeshRenderer renderer;
        private WeatherField activeField;
        private float cloudShift;
        private float mapHalf, farHalf;
        private int mapGenerating;
        private int mapRevision;
        private CloudMaps readyMap;
        private bool failed;
        private Material skybox;
        private float nativeSkyClouds;
        private readonly WeatherCloudShadows shadows;
        private readonly Func<float, float, WeatherPoint> displayedWeather;
        private StateParams displayedSky;
        private SkySplit displayedSplit;
        private float displayedHeading;

        /// <summary>Why the volume last stood down; read by the weather trace.</summary>
        internal string HideReason { get; private set; } = "not started";
        internal int BodyCount => Active ? 1 : 0;
        internal int MapUpdates => mapUpdates;
        internal bool ShadowActive => shadows.Active;
        internal int ShadowUpdates => shadows.UpdateCount;
        internal string ShadowFailure => shadows.FailureReason ?? string.Empty;
        internal bool Active => renderer != null && renderer.enabled && near.Current != null && noise != null;
        internal bool DeckActive => Active && activeField.Params.Overcast > 0.75f &&
            activeField.Params.Convective < 0.25f;
        /// <summary>True while the reduced-resolution pass draws the sky.</summary>
        internal bool HalfResolution => Active && halfResolution;
        internal bool RainCurtainsEnabled { get; private set; }
        internal int TargetWidth => pass.Width;
        internal int TargetHeight => pass.Height;
        internal long TargetBytes => pass.TargetBytes + near.BlendBytes + far.BlendBytes;

        /// <summary>False once the shader or noise is known to be unavailable: the caller
        /// keeps native clouds. True before the first frame, so native clouds never pop in
        /// while the volume preloads.</summary>
        internal bool Usable => !failed;

        internal WeatherVolumeDressing(ManualLogSource log)
        {
            logger = log;
            shadows = new WeatherCloudShadows();
            onRender = RenderTimeCamera;
            displayedWeather = SampleDisplayedWeather;
        }

        /// <summary>The shared detail-noise bytes (RGBA per texel), or null until generated.</summary>
        internal static byte[] NoiseData => Volatile.Read(ref sharedNoise);

        internal static void WarmNoise()
        {
            if (Volatile.Read(ref sharedNoise) != null ||
                Interlocked.CompareExchange(ref generatingNoise, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                byte[] data;
                try { data = CloudNoise3D.Generate(NoiseSize, 47); }
                catch { data = Array.Empty<byte>(); }
                Volatile.Write(ref sharedNoise, data);
                Interlocked.Exchange(ref generatingNoise, 0);
            });
        }

        internal void Update(LevelInfo level, WeatherField field, Camera camera,
            float currentCloudHeight, float missionTime, bool wantHalfResolution, bool temporalUpdate,
            float flash = 0f, Vector4 flashA = default, Vector4 flashB = default, bool rainVisualsEnabled = true)
        {
            if (Application.isBatchMode || level == null || field == null || !field.IsBuilt || camera == null)
            {
                Hide(level == null ? "no level" : field == null || !field.IsBuilt ? "no weather field" :
                    camera == null ? "no camera" : "batch mode");
                return;
            }
            if (!Prepare()) { Hide(failed ? "shader or noise unavailable" : "preparing"); return; }
            activeField = field;
            cloudShift = currentCloudHeight - field.Regional().CloudBase;
            float half = Mathf.Max(80000f, Mathf.Max(field.HalfX, field.HalfZ) + 45000f);
            if (half != mapHalf)
            {
                // A different extent cannot reuse the old UV projection.
                Invalidate();
                ReleaseMaps();
                mapKey = null;
                mapStep = -1;
                mapHalf = half;
                farHalf = Mathf.Max(half * FarSpanScale, MinFarHalf);
            }

            TimelineState timeline = field.Timeline;
            if (!field.Key.Equals(mapKey) || timeline.Step != mapStep)
            {
                // A new state (or a new key): build the settled sky of this step once.
                Invalidate();
                mapKey = field.Key;
                mapStep = timeline.Step;
                float settledAt = WeatherTimeline.SettledAt(field.Key, timeline);
                if (near.Current == null)
                {
                    // Preload: the first map is built on this frame, so the mission opens on
                    // its own sky instead of native clouds swapping out a moment later.
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Accept(CloudMaps.Build(field.Key, settledAt, field.HalfX, field.HalfZ, field.HourOfDay,
                        mapHalf, farHalf), missionTime);
                    uniforms.Settle(field);
                    logger?.LogInfo("[Weather] Cloud maps preloaded in " + watch.ElapsedMilliseconds +
                        " ms (near " + Mathf.RoundToInt(mapHalf / 1000f) + " km, far LOD " +
                        Mathf.RoundToInt(farHalf / 1000f) + " km half-span).");
                }
                else QueueMap(field, settledAt);
            }

            CloudMaps ready = Interlocked.Exchange(ref readyMap, null);
            if (ready != null && ready.Revision == Volatile.Read(ref mapRevision)) Accept(ready, missionTime);

            GlobalPosition global = camera.transform.GlobalPosition();
            Vector3 local = camera.transform.position;
            Vector3 offset = new Vector3((float)global.x - local.x,
                (float)global.y - local.y, (float)global.z - local.z);
            // The mesh only supplies a ray for each screen pixel. Keep its far faces well
            // inside the camera clip plane; the shader intersects the world-anchored volume.
            root.transform.position = camera.transform.position;
            root.transform.localScale = Vector3.one * 1000f;
            float mapBlend = FadeWeight(missionTime);
            BlendMaps(mapBlend);
            bool blended = !blendUnavailable && near.BlendReady && far.BlendReady;
            material.SetTexture(MapTexId, blended ? (Texture)near.BlendedStructure : near.Current);
            material.SetTexture(ProfileTexId, blended ? (Texture)near.BlendedProfiles : near.CurrentProfiles);
            material.SetTexture(FarMapTexId, blended ? (Texture)far.BlendedStructure : far.Current);
            material.SetTexture(FarProfileTexId, blended ? (Texture)far.BlendedProfiles : far.CurrentProfiles);
            CloudVolumeUniforms.ApplySpans(material, mapHalf, farHalf);
            UpdateEnvelope(mapBlend);
            material.SetTexture(EnvelopeTexId, envelope);
            material.SetFloat(EnvelopeOnId, envelope != null ? 1f : 0f);

            // Keep native clouds until the reduced pass has proved it runs on this camera.
            bool passAvailable = wantHalfResolution && compositeMaterial != null;
            pass.Bind(camera, renderer, material, compositeMaterial, passAvailable, temporalUpdate, onRender);
            bool passRunning = pass.ExecutedFrame >= 0 && pass.ExecutedFrame >= Time.frameCount - 2;
            halfResolution = passAvailable && passRunning;
            if (wantHalfResolution && !halfResolution)
            {
                // Balanced quality never silently promotes an unavailable reduced pass to full resolution.
                renderer.enabled = false;
                HideReason = "reduced cloud pass unavailable";
                shadows.Restore();
                RestoreSkyClouds();
            }
            renderer.sharedMaterial = halfResolution ? compositeMaterial : material;

            WeatherLight source = WeatherLighting.ResolveCloud(level);
            Vector3 sunDirection = source.Direction;
            Color sunColor = source.Colour;
            lightDirection = sunDirection;
            RainCurtainsEnabled = rainVisualsEnabled &&
                Mathf.Lerp(previousRainMaximum, rainMaximum, DisplayedBlend) > 0.02f;
            var frame = new CloudFrame
            {
                WorldOffset = offset,
                CameraPosition = camera.transform.position,
                CameraForward = camera.transform.forward,
                FieldOfView = camera.fieldOfView,
                PixelHeight = halfResolution && pass.Height > 0 ? pass.Height : camera.pixelHeight,
                CloudShift = cloudShift,
                Bottom = mapBlend < 1f ? Mathf.Min(volumeBottom, previousBottom) : volumeBottom,
                Top = mapBlend < 1f ? Mathf.Max(volumeTop, previousTop) : volumeTop,
                HorizonCover = Mathf.Lerp(previousHorizonCover, horizonCover, mapBlend),
                SunDirection = sunDirection,
                SunColor = sunColor,
                Ambient = RenderSettings.ambientSkyColor * RenderSettings.ambientIntensity * 0.45f +
                    RenderSettings.fogColor * 0.30f + sunColor * (Mathf.Clamp01(sunDirection.y) * 0.09f),
                Ground = RenderSettings.ambientGroundColor * 0.25f + RenderSettings.fogColor * 0.10f,
                Fog = RenderSettings.fogColor,
                // Use the same haze as opaque terrain. Ground-level model visibility
                // cannot be applied again to distant clouds: that would turn a clear
                // mountain view into a uniformly pale cloud wall. RainAtmosphere has
                // already applied the local precipitation response this frame.
                Extinction = RenderSettings.fog ? Mathf.Clamp(RenderSettings.fogDensity, 0.000008f, 0.00055f) : 0.000008f,
                Flash = flash,
                FlashA = flashA,
                FlashB = flashB,
                LowDetail = Mathf.Clamp01(PlayerSettings.graphics.CloudDetail) < 0.5f,
                RainVisualsEnabled = RainCurtainsEnabled,
                DeltaTime = Time.deltaTime,
            };
            uniforms.Apply(material, field, frame, noise);
            displayedSky = field.Params;
            displayedSplit = field.Split;
            displayedHeading = field.PrevailingHeading;
            renderer.enabled = !wantHalfResolution || halfResolution;
            if (renderer.enabled)
            {
                HideReason = null;
                ReplaceSkyClouds(level);
                shadows.Update(level, field, currentCloudHeight, camera);
            }
        }

        private void UpdateEnvelope(float mapBlend)
        {
            if (envelopeTo == null) return;
            byte[] upload = null;
            if (envelopeUnionDue)
            {
                upload = envelopeShown == null ? envelopeTo : new byte[envelopeTo.Length];
                if (envelopeShown != null) CloudMaps.Union(envelopeShown, envelopeTo, upload);
                envelopeUnionDue = false;
                envelopeFinalDue = envelopeShown != null;
            }
            else if (envelopeFinalDue && mapBlend >= 1f)
            {
                upload = envelopeTo;
                envelopeFinalDue = false;
            }
            if (upload == null) return;
            if (envelope == null)
            {
                envelope = new Texture2D(CloudMaps.EnvelopeSize, CloudMaps.EnvelopeSize, TextureFormat.RGBA32, false, true)
                { name = "Boscali Weather Envelope", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            envelope.LoadRawTextureData(upload);
            envelope.Apply(false, false);
            envelopeShown = upload;
        }

        /// <summary>At render time, with the camera's final pose: the reduced-resolution march
        /// rebuilds its rays from the corners of this exact view.</summary>
        private void RenderTimeCamera(Camera camera, Matrix4x4 view, Matrix4x4 projection)
        {
            if (material == null || camera == null) return;
            root.transform.position = view.inverse.GetColumn(3);
            GlobalPosition global = camera.transform.GlobalPosition();
            uniforms.ApplyView(camera, new Vector3((float)global.x, (float)global.y, (float)global.z), view, projection, material);
        }

        internal bool InCloud(float x, float y, float z) => DensityAt(x, y, z) > 0.08f;

        // Explicit tooling query only: bounded search of the maps actually on screen.
        // No view mutation, readback, or calls from an update/render callback.
        internal bool ProbeCloud(float centreX, float centreZ, float radius, float minRain, out Vector3 point, out float density)
        {
            point = Vector3.zero; density = 0f;
            if (!Active || activeField == null) return false;
            radius = Mathf.Clamp(radius, 500f, 15000f);
            float bestInterior = 0f;
            for (int ix = 0; ix < 9; ix++)
            for (int iz = 0; iz < 9; iz++)
            {
                float x = centreX + (ix - 4) * radius * .25f;
                float z = centreZ + (iz - 4) * radius * .25f;
                if (Mathf.Abs(x) > activeField.HalfX - 1000f || Mathf.Abs(z) > activeField.HalfZ - 1000f) continue;
                WeatherPoint weather = SampleDisplayedWeather(x, z);
                if (weather.RainRate < minRain) continue;
                for (int body = 0; body < 2; body++)
                {
                    if (body == 1 && weather.FrontCover <= .02f) continue;
                    float bottom = (body == 0 ? weather.CloudBase : weather.FrontBase) + cloudShift;
                    float top = (body == 0 ? weather.LowTop : weather.FrontTop) + cloudShift;
                    if (top <= bottom + 100f) continue;
                    for (int h = 1; h <= 3; h++)
                    {
                        float y = Mathf.Lerp(bottom, top, h * .25f);
                        float centre = DensityAt(x, y, z);
                        if (centre <= bestInterior) continue;
                        float interior = Mathf.Min(centre, Mathf.Min(DensityAt(x - 500f, y, z), DensityAt(x + 500f, y, z)),
                            Mathf.Min(DensityAt(x, y, z - 500f), DensityAt(x, y, z + 500f)));
                        if (interior <= bestInterior) continue;
                        bestInterior = interior; density = centre; point = new Vector3(x, y, z);
                    }
                }
            }
            return bestInterior >= .15f;
        }

        /// <summary>Explicit arbitrary-position sample. Never reuses the view-only cache.</summary>
        internal float DensityAt(float x, float y, float z)
        {
            if (!Active || activeField == null) return 0f;
            byte[] data = NoiseData;
            if (data == null || data.Length != NoiseTexels * 4) return 0f;
            var bodies = new CloudBodies(data, NoiseSize, displayedSky, displayedHeading,
                displayedSplit, uniforms.FogShown, activeField, uniforms.HeroStrengths, true, displayedWeather);
            return Mathf.Clamp01(bodies.Density(SampleDisplayedWeather(x, z), x, y, z, cloudShift));
        }

        private float DisplayedBlend => !blendUnavailable && near.BlendReady && far.BlendReady
            ? Mathf.Clamp01(blendedAt) : 1f;

        internal WeatherPoint SampleDisplayedWeather(float x, float z)
        {
            CloudBodies.MapWeights(x, z, mapHalf, farHalf, out float nearWeight, out float farFade);
            if (farFade <= 0f) return default;
            // Match the last actual map blit, including held maps while an async replacement builds.
            float blend = DisplayedBlend;
            if (nearWeight >= 1f) return near.Sample(x, z, mapHalf, blend);
            WeatherPoint outer = far.Sample(x, z, farHalf, blend);
            outer.BackgroundCover *= farFade; outer.FrontCover *= farFade; outer.CellShape *= farFade;
            return nearWeight <= 0f ? outer : CloudBodies.BlendMaps(outer, near.Sample(x, z, mapHalf, blend), nearWeight);
        }

        /// <summary>Three bounded light-path taps; presentation estimate, not another ray march.</summary>
        internal float TransmissionAt(float x, float y, float z)
        {
            if (!Active) return 1f;
            float depth = 0f;
            for (int i = 0; i < 3; i++)
            {
                float distance = i == 0 ? 180f : i == 1 ? 850f : 2200f;
                Vector3 p = new Vector3(x, y, z) + lightDirection * distance;
                depth += DensityAt(p.x, p.y, p.z) * (i == 0 ? 340f : 1100f) * 0.0021f;
            }
            return Mathf.Clamp01(Mathf.Exp(-depth));
        }

        internal void Restore()
        {
            RainCurtainsEnabled = false;
            Invalidate();
            pass.Dispose();
            if (root != null) UnityEngine.Object.Destroy(root);
            if (material != null) UnityEngine.Object.Destroy(material);
            if (compositeMaterial != null) UnityEngine.Object.Destroy(compositeMaterial);
            if (noise != null) UnityEngine.Object.Destroy(noise);
            ReleaseMaps();
            mapStep = -1;
            shadows.Restore();
            uniforms.Reset();
            root = null;
            renderer = null;
            material = null;
            compositeMaterial = null;
            noise = null;
            mapKey = null;
            mapHalf = farHalf = 0f;
            mapUpdates = 0;
            activeField = null;
            halfResolution = false;
            failed = false;
            RestoreSkyClouds();
        }

        private void Hide(string reason)
        {
            RainCurtainsEnabled = false;
            HideReason = reason;
            if (renderer != null) renderer.enabled = false;
            pass.Dispose();
            halfResolution = false;
            shadows.Restore();
            RestoreSkyClouds();
        }

        private void Invalidate()
        {
            Interlocked.Increment(ref mapRevision);
            Interlocked.Exchange(ref readyMap, null);
        }

        /// <summary>Crossfades from the sky on screen into a new map pair.</summary>
        private void Accept(CloudMaps result, float missionTime)
        {
            bool first = near.Current == null;
            // Whichever map dominates the screen now becomes the fade's starting point.
            bool shift = !first && FadeWeight(missionTime) >= 0.5f;
            // The union covers the sky on screen now and the one arriving.
            if (first) envelopeShown = null;
            envelopeTo = result.Envelope;
            envelopeUnionDue = true;
            near.Accept(result.Near, result.NearProfiles, first, shift);
            far.Accept(result.Far, result.FarProfiles, first, shift);
            if (first || shift)
            {
                previousBottom = first ? result.Bottom : volumeBottom;
                previousTop = first ? result.Top : volumeTop;
                previousHorizonCover = first ? result.HorizonCover : horizonCover;
                previousRainMaximum = first ? result.RainMaximum : rainMaximum;
            }
            horizonCover = result.HorizonCover;
            rainMaximum = result.RainMaximum;
            volumeBottom = result.Bottom;
            volumeTop = result.Top;
            fadeStart = missionTime;
            // Finish together with the weather: the map shows the settled state.
            fadeLength = first ? 0f : Mathf.Max(mapKey != null ? mapKey.FadeSeconds * 0.5f : 5f,
                result.SettledAt - missionTime);
            nextBlend = 0f;
            blendedAt = -1f;
            mapUpdates++;
        }

        private float FadeWeight(float missionTime)
        {
            if (fadeLength <= 0f) return 1f;
            float t = Mathf.Clamp01((missionTime - fadeStart) / fadeLength);
            return t * t * (3f - 2f * t);
        }

        private void QueueMap(WeatherField field, float settledAt)
        {
            if (Interlocked.CompareExchange(ref mapGenerating, 1, 0) != 0)
            {
                // A worker is still on an older step; ask again next frame.
                mapStep = -1;
                return;
            }
            int revision = Volatile.Read(ref mapRevision);
            WeatherKey key = field.Key;
            float halfX = field.HalfX, halfZ = field.HalfZ, hour = field.HourOfDay;
            float nearHalf = mapHalf, outerHalf = farHalf;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    CloudMaps result = CloudMaps.Build(key, settledAt, halfX, halfZ, hour, nearHalf, outerHalf);
                    if (revision == Volatile.Read(ref mapRevision))
                    {
                        result.Revision = revision;
                        Interlocked.Exchange(ref readyMap, result);
                    }
                }
                catch (Exception e) { logger?.LogWarning("[Weather] Volume map failed: " + e.Message); }
                finally { Interlocked.Exchange(ref mapGenerating, 0); }
            });
        }

        private void ReleaseMaps()
        {
            near.Release();
            far.Release();
            if (envelope != null) UnityEngine.Object.Destroy(envelope);
            envelope = null;
            envelopeShown = envelopeTo = null;
            envelopeUnionDue = envelopeFinalDue = false;
            blendUnavailable = false;
            nextBlend = 0f;
            blendedAt = -1f;
        }

        private void BlendMaps(float blend)
        {
            // The sky is static between steps: once the fade has landed, the blended maps
            // already hold the final state and need no more blits.
            if (blendUnavailable || Time.unscaledTime < nextBlend || blendedAt >= 1f) return;
            nextBlend = Time.unscaledTime + 0.25f;
            if (!near.EnsureBlendTargets() || !far.EnsureBlendTargets())
            {
                near.ReleaseBlend(); far.ReleaseBlend();
                blendUnavailable = true;
                logger?.LogWarning("[Weather] Map blend target unavailable; using unblended weather maps.");
                return;
            }
            // Interpolate the small maps once, instead of repeating four texture
            // fetches in every view and sunlight ray sample at screen resolution.
            RenderTexture saved = RenderTexture.active;
            material.SetFloat(MapBlendId, blend);
            near.Blit(material);
            far.Blit(material);
            RenderTexture.active = saved;
            blendedAt = blend;
        }

        private bool Prepare()
        {
            if (failed) return false;
            if (material == null)
            {
                Shader shader = CanopyShaderBundle.GetFlightCloudShader();
                if (shader == null || !shader.isSupported)
                {
                    logger?.LogWarning("[Weather] Volume shader unavailable; native clouds remain visible.");
                    failed = true;
                    return false;
                }
                // Before vanilla smoke (2998-3001): a missile trail in front of a cloud draws over
                // it instead of vanishing behind the volume. Vanilla cloud puffs sat at 2996-2997.
                material = new Material(shader) { name = "Boscali Weather Volume", renderQueue = 2997 };
                Shader compositeShader = CanopyShaderBundle.GetFlightCloudCompositeShader();
                if (compositeShader != null && compositeShader.isSupported)
                    compositeMaterial = new Material(compositeShader) { name = "Boscali Weather Composite", renderQueue = 2997 };
                else logger?.LogWarning("[Weather] Cloud composite shader unavailable; Balanced retains native clouds.");
            }
            if (noise == null)
            {
                // Normally warmed at the menu. A mission entered before the warm finished
                // pays the generation once here rather than showing an empty sky.
                byte[] ready = Volatile.Read(ref sharedNoise);
                if (ready == null)
                {
                    try { ready = CloudNoise3D.Generate(NoiseSize, 47); }
                    catch { ready = Array.Empty<byte>(); }
                    Volatile.Write(ref sharedNoise, ready);
                }
                if (ready.Length != NoiseSize * NoiseSize * NoiseSize * 4)
                {
                    failed = true;
                    logger?.LogWarning("[Weather] Volume noise generation failed.");
                    return false;
                }
                var pixels = new Color32[NoiseSize * NoiseSize * NoiseSize];
                for (int i = 0; i < pixels.Length; i++)
                {
                    int n = i * 4;
                    pixels[i] = new Color32(ready[n], ready[n + 1], ready[n + 2], 255);
                }
                noise = new Texture3D(NoiseSize, NoiseSize, NoiseSize, TextureFormat.RGBA32, true)
                {
                    name = "Boscali Weather Detail", filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Repeat
                };
                noise.SetPixels32(pixels);
                noise.Apply(true, true);
            }
            if (root == null)
            {
                if (cube == null) cube = BuildCube();
                root = new GameObject("Boscali Weather Volume") { hideFlags = HideFlags.DontSave };
                root.AddComponent<MeshFilter>().sharedMesh = cube;
                renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
            }
            return true;
        }

        private void ReplaceSkyClouds(LevelInfo level)
        {
            Material current = RenderSettings.skybox;
            if (current != skybox)
            {
                RestoreSkyClouds();
                if (current == null || !current.HasProperty("_Conditions")) return;
                skybox = current;
            }
            if (skybox != null)
            {
                nativeSkyClouds = level.conditions;
                skybox.SetFloat("_Conditions", 0f);
            }
        }

        private void RestoreSkyClouds()
        {
            if (skybox != null && skybox.HasProperty("_Conditions") &&
                Mathf.Approximately(skybox.GetFloat("_Conditions"), 0f))
                skybox.SetFloat("_Conditions", nativeSkyClouds);
            skybox = null;
        }

        private static Mesh BuildCube()
        {
            var m = new Mesh { name = "Boscali Weather Bounds" };
            m.vertices = new[] {
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
                new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f),
                new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
            m.triangles = new[] { 0,2,1, 0,3,2, 1,2,6, 1,6,5,
                5,6,7, 5,7,4, 4,7,3, 4,3,0, 3,7,6, 3,6,2,
                4,0,1, 4,1,5 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The older and newer keyframe of one map level and its blended copy.</summary>
        private sealed class MapPair
        {
            private readonly string label;
            private readonly int size;
            private Texture2D previous, previousProfiles;
            private byte[] currentPixels, currentProfiles, previousPixels, previousProfilePixels;

            internal Texture2D Current { get; private set; }
            internal Texture2D CurrentProfiles { get; private set; }
            internal RenderTexture BlendedStructure { get; private set; }
            internal RenderTexture BlendedProfiles { get; private set; }
            internal bool BlendReady => BlendedStructure != null && BlendedProfiles != null &&
                BlendedStructure.IsCreated() && BlendedProfiles.IsCreated();
            internal long BlendBytes => BlendReady ? (long)size * size * 12L : 0L;

            internal MapPair(string label, int size) { this.label = label; this.size = size; }

            /// <summary>The newer slot always receives the snapshot. <paramref name="shift"/>
            /// first moves the newer texture into the older slot; a first keyframe fills both.</summary>
            internal void Accept(byte[] pixels, byte[] profiles, bool first, bool shift)
            {
                if (shift && !first)
                {
                    Texture2D t = previous; previous = Current; Current = t;
                    t = previousProfiles; previousProfiles = CurrentProfiles; CurrentProfiles = t;
                    previousPixels = currentPixels; previousProfilePixels = currentProfiles;
                }
                currentPixels = pixels; currentProfiles = profiles;
                Current = Upload(Current, pixels, label + " Structure");
                CurrentProfiles = Upload(CurrentProfiles, profiles, label + " Heights");
                if (first)
                {
                    previousPixels = pixels; previousProfilePixels = profiles;
                    previous = Upload(previous, pixels, label + " Structure");
                    previousProfiles = Upload(previousProfiles, profiles, label + " Heights");
                }
            }

            internal WeatherPoint Sample(float x, float z, float half, float blend)
            {
                if (currentPixels == null || currentProfiles == null) return default;
                float u = x / (half * 2f) + 0.5f, v = z / (half * 2f) + 0.5f;
                WeatherPoint current = CloudBodies.SampleMap(currentPixels, currentProfiles, size, u, v);
                return blend >= 1f || previousPixels == null ? current : CloudBodies.BlendMaps(
                    CloudBodies.SampleMap(previousPixels, previousProfilePixels, size, u, v), current, blend);
            }

            internal bool EnsureBlendTargets()
            {
                if (BlendedStructure == null) BlendedStructure = Target(label + " Structure Blend", RenderTextureFormat.ARGB32);
                if (BlendedProfiles == null) BlendedProfiles = Target(label + " Height Blend", RenderTextureFormat.ARGBHalf);
                if ((BlendedStructure.IsCreated() || BlendedStructure.Create()) && FxRtPool.Own(BlendedStructure) &&
                    (BlendedProfiles.IsCreated() || BlendedProfiles.Create()) && FxRtPool.Own(BlendedProfiles)) return true;
                ReleaseBlend();
                return false;
            }

            internal void Blit(Material material)
            {
                if (Current == null) return;
                material.SetTexture(MapTargetId, Current);
                Graphics.Blit(previous != null ? previous : Current, BlendedStructure, material, 1);
                material.SetTexture(MapTargetId, CurrentProfiles);
                Graphics.Blit(previousProfiles != null ? previousProfiles : CurrentProfiles, BlendedProfiles, material, 1);
            }

            internal void Release()
            {
                Destroy(Current); Destroy(CurrentProfiles); Destroy(previous); Destroy(previousProfiles);
                Current = CurrentProfiles = previous = previousProfiles = null;
                currentPixels = currentProfiles = previousPixels = previousProfilePixels = null;
                ReleaseBlend();
            }

            internal void ReleaseBlend()
            {
                if (BlendedStructure != null) { FxRtPool.Disown(BlendedStructure); BlendedStructure.Release(); UnityEngine.Object.Destroy(BlendedStructure); }
                if (BlendedProfiles != null) { FxRtPool.Disown(BlendedProfiles); BlendedProfiles.Release(); UnityEngine.Object.Destroy(BlendedProfiles); }
                BlendedStructure = BlendedProfiles = null;
            }

            private Texture2D Upload(Texture2D texture, byte[] pixels, string name)
            {
                if (texture == null) texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                {
                    name = "Boscali " + name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                texture.LoadRawTextureData(pixels);
                texture.Apply(false, false);
                return texture;
            }

            private RenderTexture Target(string name, RenderTextureFormat format)
                => new RenderTexture(size, size, 0, format, RenderTextureReadWrite.Linear)
                { name = "Boscali " + name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

            private static void Destroy(Texture2D texture)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
