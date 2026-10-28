using System;
using System.Threading;
using BepInEx.Logging;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using BoscaliSummer.Core.Contracts;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>One world-anchored volume. Small 2D maps from WeatherField control the
    /// front, cell and cumulus density: a 256-square near map over the flight domain and a
    /// coarse far map (the level of detail beyond the mission area) out to the horizon. The
    /// tape is held: the maps are rebuilt only when the tape identity, the frame heading or D
    /// changes, and the new pair replaces the old in one frame (no crossfade, one map slot).
    ///
    /// <para>Normally the sky is marched at half resolution by <see cref="WeatherCloudPass"/>
    /// and the camera-following cube only composites it. Balanced quality returns to native
    /// clouds if that pass is unavailable; a full-resolution march is an explicit opt-in.</para></summary>
    internal sealed class WeatherVolumeDressing : IClientEffect
    {
        public string EffectId => "clouds";
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
        private static byte[] sharedNoise;
        private static int generatingNoise;
        private static Mesh cube;

        private readonly ManualLogSource logger;
        private readonly CloudVolumeUniforms uniforms = new CloudVolumeUniforms();
        private readonly WeatherCloudPass pass = new WeatherCloudPass();
        private readonly CloudProbePass probePass = new CloudProbePass();
        private readonly Action<Camera, Matrix4x4, Matrix4x4> onRender;
        private Material material, compositeMaterial;
        private Texture3D noise;
        private readonly MapPair near = new MapPair("Weather", CloudMaps.NearSize);
        private readonly MapPair far = new MapPair("Weather Far", CloudMaps.FarSize);
        // Empty space (CloudMaps.Envelope) of the maps on screen.
        private Texture2D envelope;
        // The signature the maps were built for: tape identity plus frame heading (SameTape) and D (spec 9.2).
        private WeatherKey mapKey;
        private float mapOffset;
        private float rainMaximum;
        private bool halfResolution;
        private Vector3 lightDirection = Vector3.up;
        private const int SightTaps = 96, SightTapsPerCall = 12;
        private const float SightReach = 30000f, SightCeiling = 14000f;
        private float sightDepth, sightOpen = 1f, nextSight;
        private int sightTap;
        private Vector3 sightOrigin, sightDirection;
        private float sightStep;

        private int mapUpdates;
        private GameObject root;
        private MeshRenderer renderer;
        private WeatherField activeField;
        private float mapHalf, farHalf;
        private int mapGenerating;
        private int mapRevision;
        private CloudMaps readyMap;
        private bool failed;
        private Material skybox;
        private float nativeSkyClouds;
        private readonly WeatherCloudShadows shadows;
        private readonly Func<float, float, WeatherPoint> displayedWeather;
        private TapeSampler displayedTape;
        private TapeFrame displayedFrame;
        private float displayedHeading;

        /// <summary>Why the volume last stood down; read by the weather trace.</summary>
        internal string HideReason { get; private set; } = "not started";
        internal int BodyCount => Active ? 1 : 0;
        internal int MapUpdates => mapUpdates;
        internal bool ShadowActive => shadows.Active;
        internal int ShadowUpdates => shadows.UpdateCount;
        internal string ShadowFailure => shadows.FailureReason ?? string.Empty;
        internal bool Active => renderer != null && renderer.enabled && near.Current != null && noise != null;
        internal bool DeckActive => Active && activeField.RowAt(0f, 0f).SheetCover > 0.75f &&
            activeField.RowAt(0f, 0f).Convective < 0.25f;
        /// <summary>True while the reduced-resolution pass draws the sky.</summary>
        internal bool HalfResolution => Active && halfResolution;
        internal bool RainCurtainsEnabled { get; private set; }
        internal int TargetWidth => pass.Width;
        internal int TargetHeight => pass.Height;
        internal long TargetBytes => pass.TargetBytes;

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
            float missionTime, bool wantHalfResolution, bool temporalUpdate,
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
            float half = Mathf.Max(80000f, Mathf.Max(field.HalfX, field.HalfZ) + 45000f);
            if (half != mapHalf)
            {
                // A different extent cannot reuse the old UV projection.
                Invalidate();
                ReleaseMaps();
                mapKey = null;
                mapHalf = half;
                farHalf = Mathf.Max(half * FarSpanScale, MinFarHalf);
            }

            float mapOffsetNow = field.Frame.Offset;
            if (!field.Key.SameTape(mapKey) || mapOffsetNow != mapOffset)
            {
                // A new tape, a new frame heading or a new D: rebuild the maps once. Kinematic edits that leave D
                // alone (Epoch, SystemSpeed, Dynamic, Sets, the anchor) only update uniforms.
                Invalidate();
                mapKey = field.Key;
                mapOffset = mapOffsetNow;
                if (near.Current == null)
                {
                    // Preload: the first map is built on this frame, so the mission opens on
                    // its own sky instead of native clouds swapping out a moment later.
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Accept(CloudMaps.Build(field.Key, missionTime, field.HalfX, field.HalfZ, field.HourOfDay,
                        mapHalf, farHalf));
                    uniforms.Settle(field);
                    logger?.LogInfo("[Weather] Cloud maps preloaded in " + watch.ElapsedMilliseconds +
                        " ms (near " + Mathf.RoundToInt(mapHalf / 1000f) + " km, far LOD " +
                        Mathf.RoundToInt(farHalf / 1000f) + " km half-span).");
                }
                else QueueMap(field, missionTime);
            }

            CloudMaps ready = Interlocked.Exchange(ref readyMap, null);
            if (ready != null && ready.Revision == Volatile.Read(ref mapRevision)) Accept(ready);

            GlobalPosition global = camera.transform.GlobalPosition();
            Vector3 local = camera.transform.position;
            Vector3 offset = new Vector3(global.x - local.x,
                global.y - local.y, global.z - local.z);
            // The mesh only supplies a ray for each screen pixel. Keep its far faces well
            // inside the camera clip plane; the shader intersects the world-anchored volume.
            // Parent, don't chase: the camera rides the interpolated aircraft hierarchy and
            // moves after LateUpdate, past the transform sync. A child relationship carries
            // the cube through the sync, so culling always sees it on the render pose.
            if (root.transform.parent != camera.transform)
            {
                root.transform.SetParent(camera.transform, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one * 1000f;
            }
            CloudVolumeUniforms.ApplySpans(material, mapHalf, farHalf);

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
            RainCurtainsEnabled = rainVisualsEnabled && rainMaximum > 0.02f;
            var frame = new CloudFrame
            {
                WorldOffset = offset,
                CameraPosition = camera.transform.position,
                CameraForward = camera.transform.forward,
                FieldOfView = camera.fieldOfView,
                PixelHeight = halfResolution && pass.Height > 0 ? pass.Height : camera.pixelHeight,
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
            displayedTape = field.Tape;
            displayedFrame = field.Frame;
            displayedHeading = field.PrevailingHeading;
            renderer.enabled = !wantHalfResolution || halfResolution;
            probePass.Bind(renderer.enabled ? material : null);
            if (renderer.enabled)
            {
                HideReason = null;
                ReplaceSkyClouds(level);
                shadows.Update(level, field, camera);
            }
        }

        /// <summary>At render time, with the camera's final pose: the reduced-resolution march
        /// rebuilds its rays from the corners of this exact view.</summary>
        private void RenderTimeCamera(Camera camera, Matrix4x4 view, Matrix4x4 projection)
        {
            if (material == null || camera == null) return;
            root.transform.position = view.inverse.GetColumn(3);
            GlobalPosition global = camera.transform.GlobalPosition();
            uniforms.ApplyView(camera, new Vector3(global.x, global.y, global.z), view, projection, material);
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
                float bottom = weather.CloudBase;
                float top = weather.LowTop;
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
            return bestInterior >= .15f;
        }

        /// <summary>Explicit arbitrary-position sample. Never reuses the view-only cache.</summary>
        internal float DensityAt(float x, float y, float z)
        {
            if (!Active || activeField == null) return 0f;
            byte[] data = NoiseData;
            if (data == null || data.Length != NoiseTexels * 4) return 0f;
            var bodies = new CloudBodies(data, NoiseSize, displayedTape, displayedFrame, displayedHeading,
                uniforms.FogShown, activeField, uniforms.HeroStrengths, true, displayedWeather);
            return Mathf.Clamp01(bodies.Density(SampleDisplayedWeather(x, z), x, y, z));
        }

        internal WeatherPoint SampleDisplayedWeather(float x, float z)
        {
            CloudBodies.MapWeights(x, z, mapHalf, farHalf, out float nearWeight, out float farFade);
            if (farFade <= 0f) return default;
            // The maps on screen, including the held pair while an async replacement builds.
            if (nearWeight >= 1f) return near.Sample(x, z, mapHalf);
            WeatherPoint outer = far.Sample(x, z, farHalf);
            outer.BackgroundCover *= farFade; outer.CellShape *= farFade;
            return nearWeight <= 0f ? outer : CloudBodies.BlendMaps(outer, near.Sample(x, z, mapHalf), nearWeight);
        }

        /// <summary>
        /// Light reaching a point from the sun or moon: the whole line through the cloud layers, a dozen taps per call
        /// (one line every eight calls), so a cloud 20 km down the line hides the sun too. Call once per frame.
        /// </summary>
        internal float TransmissionAt(float x, float y, float z)
        {
            if (!Active) { sightTap = 0; sightDepth = 0f; return sightOpen = 1f; }
            // Out of the top of the cloud layers, or 30 km for a low sun: at 90 km the taps sat a kilometre apart and
            // a scattered field always read as solid, which put the setting sun out over an open horizon.
            float reach = Mathf.Min(SightReach, (SightCeiling - y) / Mathf.Max(lightDirection.y, 0.03f));
            if (reach <= 0f) { sightTap = 0; sightDepth = 0f; return sightOpen = 1f; }
            // At most 75 measuring calls a second (every frame at 75 fps or less): above that the same line only refreshes a little less often,
            // and the sun disc already eases the result.
            if (Time.unscaledTime < nextSight) return sightOpen;
            nextSight = Time.unscaledTime + 1f / 75f;
            if (sightTap == 0)
            {
                sightOrigin = new Vector3(x, y, z);
                sightDirection = lightDirection;
                sightStep = reach / SightTaps;
            }
            for (int i = 0; i < SightTapsPerCall; i++)
            {
                // Fixed midpoints on one held ray: alternating phases published different cloud depths every
                // eight calls, which the bright sun's optical-depth response amplified into visible blinking.
                Vector3 p = sightOrigin + sightDirection * (sightStep * (sightTap + 0.5f));
                sightDepth += DensityAt(p.x, p.y, p.z) * sightStep * 0.0021f;
                if (++sightTap < SightTaps) continue;
                sightOpen = Mathf.Clamp01(Mathf.Exp(-sightDepth));
                sightDepth = 0f; sightTap = 0;
            }
            return sightOpen;
        }

        internal void Restore()
        {
            RainCurtainsEnabled = false;
            sightTap = 0; sightDepth = nextSight = 0f; sightOpen = 1f;
            Invalidate();
            probePass.Dispose();
            pass.Dispose();
            if (root != null) UnityEngine.Object.Destroy(root);
            if (material != null) UnityEngine.Object.Destroy(material);
            if (compositeMaterial != null) UnityEngine.Object.Destroy(compositeMaterial);
            if (noise != null) UnityEngine.Object.Destroy(noise);
            ReleaseMaps();
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
            sightTap = 0; sightDepth = nextSight = 0f; sightOpen = 1f;
            HideReason = reason;
            if (renderer != null) renderer.enabled = false;
            pass.Dispose();
            probePass.Bind(null);
            halfResolution = false;
            shadows.Restore();
            RestoreSkyClouds();
        }

        private void Invalidate()
        {
            Interlocked.Increment(ref mapRevision);
            Interlocked.Exchange(ref readyMap, null);
        }

        /// <summary>Commits a map pair: the new maps replace the old in one frame (a cut), with their envelope and
        /// volume bounds. The material is bound here; a rebuild reuses the same textures.</summary>
        private void Accept(CloudMaps result)
        {
            near.Accept(result.Near, result.NearProfiles);
            far.Accept(result.Far, result.FarProfiles);
            if (envelope == null)
            {
                envelope = new Texture2D(CloudMaps.EnvelopeSize, CloudMaps.EnvelopeSize, TextureFormat.RGBA32, false, true)
                { name = "Boscali Weather Envelope", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            envelope.LoadRawTextureData(result.Envelope);
            envelope.Apply(false, false);
            material.SetTexture(MapTexId, near.Current);
            material.SetTexture(ProfileTexId, near.CurrentProfiles);
            material.SetTexture(FarMapTexId, far.Current);
            material.SetTexture(FarProfileTexId, far.CurrentProfiles);
            material.SetTexture(EnvelopeTexId, envelope);
            material.SetFloat(EnvelopeOnId, 1f);
            rainMaximum = result.RainMaximum;
            mapUpdates++;
        }

        private void QueueMap(WeatherField field, float time)
        {
            if (Interlocked.CompareExchange(ref mapGenerating, 1, 0) != 0)
            {
                // A worker is still on an older build; ask again next frame.
                mapKey = null;
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
                    CloudMaps result = CloudMaps.Build(key, time, halfX, halfZ, hour, nearHalf, outerHalf);
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
            if (material != null) material.SetFloat(EnvelopeOnId, 0f);
        }

        private bool Prepare()
        {
            if (failed) return false;
            if (!TapeTexture.Supported)
            {
                // The tape is a float texture: without the format the volume cannot read its rows. Native clouds stay.
                logger?.LogWarning("[Weather] RGBAFloat textures unsupported; native clouds remain visible.");
                failed = true;
                return false;
            }
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
                    pixels[i] = new Color32(ready[n], ready[n + 1], ready[n + 2], ready[n + 3]);
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

        /// <summary>The one map slot of one level: the texture pair on screen and its bytes for CPU sampling.</summary>
        private sealed class MapPair
        {
            private readonly string label;
            private readonly int size;
            private byte[] currentPixels, currentProfiles;

            internal Texture2D Current { get; private set; }
            internal Texture2D CurrentProfiles { get; private set; }

            internal MapPair(string label, int size) { this.label = label; this.size = size; }

            /// <summary>Replaces the slot's pixels (the textures are reused).</summary>
            internal void Accept(byte[] pixels, byte[] profiles)
            {
                currentPixels = pixels; currentProfiles = profiles;
                Current = Upload(Current, pixels, label + " Structure");
                CurrentProfiles = Upload(CurrentProfiles, profiles, label + " Heights");
            }

            internal WeatherPoint Sample(float x, float z, float half)
            {
                if (currentPixels == null || currentProfiles == null) return default;
                float u = x / (half * 2f) + 0.5f, v = z / (half * 2f) + 0.5f;
                return CloudBodies.SampleMap(currentPixels, currentProfiles, size, u, v);
            }

            internal void Release()
            {
                Destroy(Current); Destroy(CurrentProfiles);
                Current = CurrentProfiles = null;
                currentPixels = currentProfiles = null;
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

            private static void Destroy(Texture2D texture)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
