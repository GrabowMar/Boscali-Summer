using System;
using System.Threading;
using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Weather.Visuals
{
    /// <summary>One world-anchored volume. Small 2D maps from WeatherField control the
    /// front, cell and cumulus density: a 256-square near map over the flight domain and a
    /// coarse 128-square far map (the level of detail beyond the mission area) out to the
    /// horizon. The sky is static between weather steps: each step builds one map pair for
    /// its settled state and crossfades into it on the mission clock.</summary>
    internal sealed class WeatherVolumeDressing
    {
        private const int MapSize = 256;
        private const int FarMapSize = 128;
        internal const int NoiseSize = 64;
        internal const int NoiseTexels = NoiseSize * NoiseSize * NoiseSize;
        private const float VolumeTop = 16000f;
        private const float FarSpanScale = 3f;
        private const float MinFarHalf = 240000f;
        private static byte[] sharedNoise;
        private static int generatingNoise;
        private static Mesh cube;

        private readonly ManualLogSource logger;
        private int mapStep = -1;
        private float fadeStart, fadeLength;
        private Material material;
        private Texture3D noise;
        private readonly MapPair near = new MapPair("Weather", MapSize);
        private readonly MapPair far = new MapPair("Weather Far", FarMapSize);
        private float nextBlend;
        private bool blendUnavailable;
        private WeatherKey mapKey;
        private float volumeBottom, volumeTop = VolumeTop;
        private float horizonCover, previousHorizonCover;
        private readonly Vector4[] heroA = new Vector4[Superstructures.MaxCount];
        private readonly Vector4[] heroB = new Vector4[Superstructures.MaxCount];
        // Set-pieces build and decay on screen over ~20 s, so a console change never pops.
        private readonly float[] heroShown = new float[Superstructures.MaxCount];
        private readonly Vector2[] heroSite = new Vector2[Superstructures.MaxCount];
        private float cameraInCloud;
        private float fogShown;

        /// <summary>0..1 how deep the camera is inside cloud, smoothed; read by the atmosphere.</summary>
        internal float CameraInCloud => cameraInCloud;
        private float previousBottom, previousTop;
        private int mapUpdates;
        private GameObject root;
        private MeshRenderer renderer;
        private WeatherField activeField;
        private float cloudShift;
        private float mapHalf, farHalf;
        private int mapGenerating;
        private int mapRevision;
        private MapResult readyMap;
        private bool failed;
        private Material skybox;
        private float nativeSkyClouds;
        private readonly WeatherCloudShadows shadows;

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

        /// <summary>False once the shader or noise is known to be unavailable: the caller
        /// keeps native clouds. True before the first frame, so native clouds never pop in
        /// while the volume preloads.</summary>
        internal bool Usable => !failed;

        internal WeatherVolumeDressing(ManualLogSource log) { logger = log; shadows = new WeatherCloudShadows(); }

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
            float currentCloudHeight, float missionTime)
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
                    Accept(BuildMap(field.Key, settledAt, field.HalfX, field.HalfZ, field.HourOfDay,
                        mapHalf, farHalf), missionTime);
                    logger?.LogInfo("[Weather] Cloud maps preloaded in " + watch.ElapsedMilliseconds +
                        " ms (near " + Mathf.RoundToInt(mapHalf / 1000f) + " km, far LOD " +
                        Mathf.RoundToInt(farHalf / 1000f) + " km half-span).");
                }
                else QueueMap(field, settledAt);
            }

            MapResult ready = Interlocked.Exchange(ref readyMap, null);
            if (ready != null && ready.Revision == Volatile.Read(ref mapRevision)) Accept(ready, missionTime);

            GlobalPosition global = camera.transform.GlobalPosition();
            Vector3 local = camera.transform.position;
            Vector3 offset = new Vector3((float)global.x - local.x,
                (float)global.y - local.y, (float)global.z - local.z);
            // The mesh only supplies a ray for each screen pixel. Keep its far faces well
            // inside the camera clip plane; the shader intersects the world-anchored volume.
            root.transform.position = camera.transform.position;
            root.transform.localScale = Vector3.one * 1000f;
            material.SetTexture("_CloudNoiseTex", noise);
            float mapBlend = FadeWeight(missionTime);
            BlendMaps(mapBlend);
            bool blended = !blendUnavailable && near.BlendReady && far.BlendReady;
            material.SetTexture("_WeatherMapTex", blended ? (Texture)near.BlendedStructure : near.Current);
            material.SetTexture("_WeatherProfileTex", blended ? (Texture)near.BlendedProfiles : near.CurrentProfiles);
            material.SetTexture("_WeatherFarMapTex", blended ? (Texture)far.BlendedStructure : far.Current);
            material.SetTexture("_WeatherFarProfileTex", blended ? (Texture)far.BlendedProfiles : far.CurrentProfiles);
            material.SetFloat("_WeatherMapSpan", mapHalf * 2f);
            material.SetFloat("_WeatherFarSpan", farHalf * 2f);
            material.SetFloat("_CloudBase", field.Params.CloudBase + cloudShift);
            material.SetFloat("_CloudHeightShift", cloudShift);
            material.SetVector("_CloudWorldOffset", offset);
            material.SetVector("_CloudCameraForward", camera.transform.forward);
            float boundsBottom = (mapBlend < 1f ? Mathf.Min(volumeBottom, previousBottom) : volumeBottom) + cloudShift;
            float boundsTop = (mapBlend < 1f ? Mathf.Max(volumeTop, previousTop) : volumeTop) + cloudShift;
            // Scenery storms: static set-pieces the state builds up; they widen the march bounds.
            int heroes = field.SuperstructureCount;
            for (int i = 0; i < heroA.Length; i++)
            {
                if (i >= heroes) { heroA[i] = Vector4.zero; heroB[i] = Vector4.zero; heroShown[i] = 0f; continue; }
                Superstructure s = field.SuperstructureAt(i);
                var site = new Vector2(s.X, s.Z);
                if (site != heroSite[i]) { heroSite[i] = site; heroShown[i] = 0f; }
                heroShown[i] = Mathf.MoveTowards(heroShown[i], s.Strength, Time.deltaTime / 20f);
                heroA[i] = new Vector4(s.X, s.Z, s.Heading, (float)s.Kind);
                heroB[i] = new Vector4(s.Size, s.Top, heroShown[i], s.Extent);
                boundsBottom = Mathf.Min(boundsBottom, 350f);
                boundsTop = Mathf.Max(boundsTop, s.Top + 1500f);
            }
            // The console fog bank eases in and out like the set-pieces.
            fogShown = Mathf.MoveTowards(fogShown, (field.Key.Sets & Superstructures.FogBankSet) != 0 ? 1f : 0f,
                Time.deltaTime / 20f);
            material.SetFloat("_FogBank", fogShown);
            material.SetVectorArray("_HeroA", heroA);
            material.SetVectorArray("_HeroB", heroB);
            material.SetFloat("_HeroCount", heroes);
            material.SetVector("_CloudAltitudeBounds", new Vector2(boundsBottom, boundsTop));
            // Static weather: the detail texture does not crawl either.
            material.SetVector("_CloudWindOffset", Vector2.zero);
            Vector3 sunDirection = level.sun != null ? -level.sun.transform.forward : Vector3.up;
            Color sunColor = level.sun != null ? level.sun.color * level.sun.intensity : Color.white;
            float peak = Mathf.Max(sunColor.r, Mathf.Max(sunColor.g, sunColor.b));
            if (peak > 2f) sunColor *= 2f / peak;
            material.SetVector("_CloudSunDirection", sunDirection.normalized);
            material.SetColor("_CloudSunColor", sunColor * 0.75f);
            material.SetColor("_CloudAmbientColor", RenderSettings.ambientSkyColor * RenderSettings.ambientIntensity * 0.45f +
                RenderSettings.fogColor * 0.30f + sunColor * (Mathf.Clamp01(sunDirection.y) * 0.09f));
            material.SetColor("_CloudGroundColor", RenderSettings.ambientGroundColor * 0.25f +
                RenderSettings.fogColor * 0.10f);
            material.SetColor("_CloudFogColor", RenderSettings.fogColor);
            // Use the same haze as opaque terrain. Ground-level model visibility
            // cannot be applied again to distant clouds: that would turn a clear
            // mountain view into a uniformly pale cloud wall. RainAtmosphere has
            // already applied the local precipitation response this frame.
            float extinction = RenderSettings.fog ? Mathf.Clamp(RenderSettings.fogDensity, 0.000008f, 0.00055f) : 0.000008f;
            material.SetFloat("_CloudAirExtinction", extinction);
            material.SetFloat("_CloudStorm", field.Params.Severity);
            // Cloud genera for the current state (they fade with it).
            StateParams sky = field.Params;
            material.SetFloat("_LayerDepth", sky.LayerDepth);
            material.SetFloat("_LayerSmooth", sky.LayerSmooth);
            material.SetFloat("_MidCover", sky.MidCover);
            material.SetFloat("_MidSheet", sky.MidSheet);
            material.SetFloat("_HighCover", sky.HighCover);
            material.SetFloat("_HighVeil", sky.HighVeil);
            WeatherMath.HeadingToVector(field.PrevailingHeading, out float windX, out float windZ);
            material.SetVector("_CloudWindDir", new Vector2(windX, windZ));
            // Horizon deck: the far ring's cover, plus a distant band of cumulus in fair skies.
            float ring = Mathf.Lerp(previousHorizonCover, horizonCover, mapBlend);
            material.SetFloat("_HorizonCover", Mathf.Clamp01(Mathf.Max(ring, sky.Cumulus * 0.3f + sky.Convective * 0.2f)));
            material.SetFloat("_HorizonDeck", sky.CloudBase + cloudShift + Mathf.Max(300f, sky.LayerDepth) * 0.4f);
            material.SetFloat("_HorizonDepth", Mathf.Max(300f, sky.LayerDepth));
            // The frontal boundary: one side of the map under the deck, the other opening up.
            SkySplit split = field.Split;
            material.SetVector("_SplitA", new Vector4(split.NormalX, split.NormalZ, split.Offset, SkySplit.Width));
            material.SetVector("_SplitB", new Vector4(split.Amount, split.MeanderAmplitude,
                Mathf.Max(1000f, split.MeanderWavelength), split.MeanderPhase));
            // Inside cloud: near-field density and wisps, eased so crossing an edge never pops.
            float depthHere = CloudDensityAt((float)global.x, (float)global.y, (float)global.z);
            cameraInCloud = Mathf.MoveTowards(cameraInCloud, Mathf.Clamp01((depthHere - 0.02f) * 5f), Time.deltaTime * 1.5f);
            material.SetFloat("_CameraInCloud", cameraInCloud);
            material.SetFloat("_CloudPixelAngle",
                2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, camera.pixelHeight));
            bool lowDetail = Mathf.Clamp01(PlayerSettings.graphics.CloudDetail) < 0.5f;
            material.SetFloat("_CloudSteps", lowDetail ? 64f : 96f);
            material.SetFloat("_CloudFarSteps", lowDetail ? 12f : 20f);
            renderer.enabled = true;
            HideReason = null;
            ReplaceSkyClouds(level);
            shadows.Update(level, field, currentCloudHeight, camera);
        }

        internal bool InCloud(float x, float y, float z) => Active && CloudDensityAt(x, y, z) > 0.08f;

        /// <summary>The same bodies the shader draws, at one point: the gaps of a broken deck are clear air.</summary>
        private float CloudDensityAt(float x, float y, float z)
        {
            if (activeField == null) return 0f;
            byte[] data = NoiseData;
            if (data == null || data.Length != NoiseTexels * 4) return 0f;
            return new CloudBodies(data, NoiseSize, activeField.Params, activeField.PrevailingHeading, activeField.Split, fogShown)
                .Density(activeField.Sample(x, z), x, y, z, cloudShift);
        }

        internal void Restore()
        {
            Invalidate();
            if (root != null) UnityEngine.Object.Destroy(root);
            if (material != null) UnityEngine.Object.Destroy(material);
            if (noise != null) UnityEngine.Object.Destroy(noise);
            ReleaseMaps();
            mapStep = -1;
            shadows.Restore();
            root = null;
            renderer = null;
            material = null;
            noise = null;
            mapKey = null;
            mapHalf = farHalf = 0f;
            mapUpdates = 0;
            activeField = null;
            failed = false;
            RestoreSkyClouds();
        }

        private void Hide(string reason)
        {
            HideReason = reason;
            if (renderer != null) renderer.enabled = false;
            shadows.Restore();
            RestoreSkyClouds();
        }

        private void Invalidate()
        {
            Interlocked.Increment(ref mapRevision);
            Interlocked.Exchange(ref readyMap, null);
        }

        /// <summary>Crossfades from the sky on screen into a new map pair.</summary>
        private void Accept(MapResult result, float missionTime)
        {
            bool first = near.Current == null;
            // Whichever map dominates the screen now becomes the fade's starting point.
            bool shift = !first && FadeWeight(missionTime) >= 0.5f;
            near.Accept(result.Pixels, result.Profiles, first, shift);
            far.Accept(result.FarPixels, result.FarProfiles, first, shift);
            if (first || shift)
            {
                previousBottom = first ? result.Bottom : volumeBottom;
                previousTop = first ? result.Top : volumeTop;
                previousHorizonCover = first ? result.HorizonCover : horizonCover;
            }
            horizonCover = result.HorizonCover;
            volumeBottom = result.Bottom;
            volumeTop = result.Top;
            fadeStart = missionTime;
            // Finish together with the weather: the map shows the settled state.
            fadeLength = first ? 0f : Mathf.Max(mapKey != null ? mapKey.FadeSeconds * 0.5f : 5f,
                result.SettledAt - missionTime);
            nextBlend = 0f;
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
                    MapResult result = BuildMap(key, settledAt, halfX, halfZ, hour, nearHalf, outerHalf);
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

        /// <summary>Samples both maps of one settled state. Pure: runs on the worker, or on the
        /// main thread once for the preload.</summary>
        private static MapResult BuildMap(WeatherKey key, float settledAt, float halfX, float halfZ, float hour,
            float nearHalf, float outerHalf)
        {
            var snapshot = new WeatherField();
            snapshot.Build(key, settledAt, halfX, halfZ, hour);
            var result = new MapResult(settledAt, MapSize, FarMapSize);
            float bottom = VolumeTop, top = 0f;
            Fill(snapshot, nearHalf, MapSize, result.Pixels, result.Profiles, ref bottom, ref top);
            Fill(snapshot, outerHalf, FarMapSize, result.FarPixels, result.FarProfiles, ref bottom, ref top);
            result.HorizonCover = RingCover(result.FarPixels, FarMapSize);
            result.Bottom = Math.Max(0f, bottom - 500f);
            result.Top = Math.Min(VolumeTop, Math.Max(bottom + 2000f, top + 1600f));
            return result;
        }

        private static void Fill(WeatherField snapshot, float half, int size, Color32[] pixels, Color32[] profiles,
            ref float bottom, ref float top)
        {
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                float worldX = ((x + 0.5f) / size * 2f - 1f) * half;
                float worldZ = ((z + 0.5f) / size * 2f - 1f) * half;
                WeatherPoint p = snapshot.Sample(worldX, worldZ);
                pixels[z * size + x] = new Color32(Byte(p.BackgroundCover), Byte(p.FrontCover),
                    Byte(p.CellShape), Byte(p.CloudTop / VolumeTop));
                profiles[z * size + x] = new Color32(Byte(p.FrontBase / VolumeTop),
                    Byte(p.FrontTop / VolumeTop), Byte(p.CloudBase / VolumeTop), Byte(p.RainRate / 100f));
                if (p.Cover > 0.02f)
                {
                    bottom = Math.Min(bottom, p.FrontCover > 0.02f ? Math.Min(p.CloudBase, p.FrontBase) : p.CloudBase);
                    top = Math.Max(top, Math.Max(p.CloudTop, p.FrontTop));
                }
            }
        }

        /// <summary>Mean sheet and front cover on the far map's outer ring: what the horizon deck
        /// continues with, so the two agree where they meet.</summary>
        private static float RingCover(Color32[] pixels, int size)
        {
            float sum = 0f;
            int n = 0;
            int ring = size / 10;
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                if (x >= ring && x < size - ring && z >= ring && z < size - ring) continue;
                Color32 c = pixels[z * size + x];
                float sheet = c.r / 255f, front = c.g / 255f;
                sum += 1f - (1f - sheet) * (1f - front);
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        private static byte Byte(float v) => (byte)(Math.Max(0f, Math.Min(1f, v)) * 255f + 0.5f);

        private void ReleaseMaps()
        {
            near.Release();
            far.Release();
            blendUnavailable = false;
            nextBlend = 0f;
        }

        private void BlendMaps(float blend)
        {
            if (blendUnavailable || Time.unscaledTime < nextBlend) return;
            nextBlend = Time.unscaledTime + 0.25f;
            if (!near.EnsureBlendTargets() || !far.EnsureBlendTargets())
            {
                blendUnavailable = true;
                logger?.LogWarning("[Weather] Map blend target unavailable; using unblended weather maps.");
                return;
            }
            // Interpolate the small maps once, instead of repeating four texture
            // fetches in every view and sunlight ray sample at screen resolution.
            RenderTexture saved = RenderTexture.active;
            material.SetFloat("_WeatherMapBlend", blend);
            near.Blit(material);
            far.Blit(material);
            RenderTexture.active = saved;
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
                    pixels[i] = new Color32(ready[n], ready[n + 1], 0, 255);
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

            internal Texture2D Current { get; private set; }
            internal Texture2D CurrentProfiles { get; private set; }
            internal RenderTexture BlendedStructure { get; private set; }
            internal RenderTexture BlendedProfiles { get; private set; }
            internal bool BlendReady => BlendedStructure != null && BlendedProfiles != null &&
                BlendedStructure.IsCreated() && BlendedProfiles.IsCreated();

            internal MapPair(string label, int size) { this.label = label; this.size = size; }

            /// <summary>The newer slot always receives the snapshot. <paramref name="shift"/>
            /// first moves the newer texture into the older slot; a first keyframe fills both.</summary>
            internal void Accept(Color32[] pixels, Color32[] profiles, bool first, bool shift)
            {
                if (shift && !first)
                {
                    Texture2D t = previous; previous = Current; Current = t;
                    t = previousProfiles; previousProfiles = CurrentProfiles; CurrentProfiles = t;
                }
                Current = Upload(Current, pixels, label + " Structure");
                CurrentProfiles = Upload(CurrentProfiles, profiles, label + " Heights");
                if (first)
                {
                    previous = Upload(previous, pixels, label + " Structure");
                    previousProfiles = Upload(previousProfiles, profiles, label + " Heights");
                }
            }

            internal bool EnsureBlendTargets()
            {
                if (BlendedStructure == null) BlendedStructure = Target(label + " Structure Blend", RenderTextureFormat.ARGB32);
                if (BlendedProfiles == null) BlendedProfiles = Target(label + " Height Blend", RenderTextureFormat.ARGBHalf);
                return (BlendedStructure.IsCreated() || BlendedStructure.Create()) &&
                    (BlendedProfiles.IsCreated() || BlendedProfiles.Create());
            }

            internal void Blit(Material material)
            {
                if (Current == null) return;
                material.SetTexture("_CloudMapTarget", Current);
                Graphics.Blit(previous != null ? previous : Current, BlendedStructure, material, 1);
                material.SetTexture("_CloudMapTarget", CurrentProfiles);
                Graphics.Blit(previousProfiles != null ? previousProfiles : CurrentProfiles, BlendedProfiles, material, 1);
            }

            internal void Release()
            {
                Destroy(Current); Destroy(CurrentProfiles); Destroy(previous); Destroy(previousProfiles);
                Current = CurrentProfiles = previous = previousProfiles = null;
                if (BlendedStructure != null) { BlendedStructure.Release(); UnityEngine.Object.Destroy(BlendedStructure); }
                if (BlendedProfiles != null) { BlendedProfiles.Release(); UnityEngine.Object.Destroy(BlendedProfiles); }
                BlendedStructure = BlendedProfiles = null;
            }

            private Texture2D Upload(Texture2D texture, Color32[] pixels, string name)
            {
                if (texture == null) texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                {
                    name = "Boscali " + name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                texture.SetPixels32(pixels);
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

        private sealed class MapResult
        {
            internal int Revision;
            internal readonly float SettledAt;
            internal readonly Color32[] Pixels, Profiles, FarPixels, FarProfiles;
            internal float Bottom, Top;
            internal float HorizonCover;

            internal MapResult(float settledAt, int size, int farSize)
            {
                SettledAt = settledAt;
                Pixels = new Color32[size * size];
                Profiles = new Color32[size * size];
                FarPixels = new Color32[farSize * farSize];
                FarProfiles = new Color32[farSize * farSize];
            }
        }
    }
}
