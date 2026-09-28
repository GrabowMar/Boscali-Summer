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
        private const int NoiseSize = 64;
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
            material.SetVector("_CloudAltitudeBounds", new Vector2(
                (mapBlend < 1f ? Mathf.Min(volumeBottom, previousBottom) : volumeBottom) + cloudShift,
                (mapBlend < 1f ? Mathf.Max(volumeTop, previousTop) : volumeTop) + cloudShift));
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
            bool lowDetail = Mathf.Clamp01(PlayerSettings.graphics.CloudDetail) < 0.5f;
            material.SetFloat("_CloudSteps", lowDetail ? 64f : 96f);
            material.SetFloat("_CloudFarSteps", lowDetail ? 12f : 20f);
            renderer.enabled = true;
            HideReason = null;
            ReplaceSkyClouds(level);
            shadows.Update(level, field, currentCloudHeight);
        }

        internal bool InCloud(float x, float y, float z)
        {
            if (!Active || activeField == null) return false;
            WeatherPoint point = activeField.Sample(x, z);
            float height = y - cloudShift;
            if (point.FrontCover > 0.48f && height > point.FrontBase + 80f && height < point.FrontTop - 80f)
                return true;
            if (height < point.CloudBase + 80f || height > point.CloudTop - 80f) return false;
            float h = Mathf.Clamp01((height - point.CloudBase) / Mathf.Max(1f, point.CloudTop - point.CloudBase));
            return (point.BackgroundCover > 0.34f && height < point.CloudBase + 1400f) ||
                (point.CellShape > 0.25f + 0.43f * h * h && h < 0.94f);
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
            }
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
                material = new Material(shader) { name = "Boscali Weather Volume", renderQueue = 3000 };
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
