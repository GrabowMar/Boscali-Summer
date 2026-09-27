using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Weather.Visuals
{
    /// <summary>World-space cloud bodies. Their bounds and density stay fixed while an aircraft moves through them.</summary>
    internal sealed class FlightCloudDressing
    {
        private const int NoiseSize = 64;
        private const int GridMetres = 6000;
        private const int GridRadius = 6;
        private const int MaxBodies = 30;
        private static byte[] sharedNoise;
        private static int generatingNoise;
        private static Mesh cube;

        private readonly ManualLogSource logger;
        private readonly List<Body> bodies = new List<Body>(MaxBodies + 1);
        private readonly List<Candidate> candidates = new List<Candidate>(169);
        private Material material;
        private Texture3D noise;
        private bool failed;
        private float nextLayout;
        private int previousGridX = int.MinValue, previousGridZ = int.MinValue;
        private WeatherKey previousKey;
        private int previousPhase = -1;
        private int activeCount;
        private bool deckActive;
        private Material skybox;
        private float nativeSkyClouds;

        internal int BodyCount => activeCount;
        internal bool Active => activeCount > 0 && material != null && noise != null;

        internal FlightCloudDressing(ManualLogSource log) { logger = log; }

        internal void Update(LevelInfo level, WeatherField field, Camera camera,
            float currentCloudHeight, float missionTime)
        {
            if (Application.isBatchMode || level == null || field == null || !field.IsBuilt || camera == null)
            {
                Hide();
                return;
            }
            if (!Prepare()) { Hide(); return; }

            GlobalPosition global = camera.transform.GlobalPosition();
            Vector3 local = camera.transform.position;
            Vector3 offset = new Vector3((float)global.x - local.x,
                (float)global.y - local.y, (float)global.z - local.z);
            WeatherMath.HeadingToVector(WeatherMath.Hash01(field.Key.Seed, 41) * 360f,
                out float driftX, out float driftZ);
            driftX *= WeatherField.PatchDrift * missionTime;
            driftZ *= WeatherField.PatchDrift * missionTime;
            int gridX = Mathf.FloorToInt(((float)global.x - driftX) / GridMetres);
            int gridZ = Mathf.FloorToInt(((float)global.z - driftZ) / GridMetres);
            int phase = Mathf.Clamp(Mathf.FloorToInt(field.Regime.Blend * 4.001f), 0, 4);
            if (Time.unscaledTime >= nextLayout || gridX != previousGridX || gridZ != previousGridZ ||
                previousKey == null || !previousKey.Equals(field.Key) || phase != previousPhase)
            {
                BuildLayout(field, (float)global.x, (float)global.z, gridX, gridZ,
                    currentCloudHeight - field.Regional().CloudBase, driftX, driftZ);
                nextLayout = Time.unscaledTime + 0.8f;
                previousGridX = gridX;
                previousGridZ = gridZ;
                previousKey = field.Key;
                previousPhase = phase;
            }

            material.SetTexture("_CloudNoiseTex", noise);
            material.SetVector("_CloudWorldOffset", offset);
            material.SetVector("_CloudCameraForward", camera.transform.forward);
            material.SetVector("_CloudWindOffset", new Vector2(driftX, driftZ));
            Vector3 sunDirection = level.sun != null ? -level.sun.transform.forward : Vector3.up;
            Color sunColor = level.sun != null ? level.sun.color * level.sun.intensity : Color.white;
            material.SetVector("_CloudSunDirection", sunDirection.normalized);
            float sunPeak = Mathf.Max(sunColor.r, Mathf.Max(sunColor.g, sunColor.b));
            if (sunPeak > 2f) sunColor *= 2f / sunPeak;
            material.SetColor("_CloudSunColor", sunColor * 0.64f);
            material.SetColor("_CloudAmbientColor", RenderSettings.ambientLight * 0.45f +
                RenderSettings.fogColor * 0.10f);
            material.SetColor("_CloudFogColor", RenderSettings.fogColor);
            material.SetFloat("_CloudStorm", Mathf.Clamp01(
                (field.Regime.Params.Overcast - 0.45f) * 1.5f));
            material.SetFloat("_CloudSteps", Mathf.Clamp01(PlayerSettings.graphics.CloudDetail) < 0.5f ? 16f : 24f);

            for (int i = 0; i < activeCount; i++)
            {
                Body body = bodies[i];
                body.Root.transform.position = body.GlobalCenter - offset;
                body.Root.transform.localScale = body.Size;
                body.Renderer.enabled = true;
            }
            for (int i = activeCount; i < bodies.Count; i++) bodies[i].Renderer.enabled = false;
            ReplaceSkyClouds(level);
        }

        internal bool InCloud(float x, float y, float z)
        {
            if (!Active) return false;
            for (int i = 0; i < activeCount; i++)
            {
                Body b = bodies[i];
                Vector3 d = new Vector3(x, y, z) - b.GlobalCenter;
                if (Mathf.Abs(d.y) >= b.Size.y * 0.43f) continue;
                if (b.Deck || (d.x * d.x / (b.Size.x * b.Size.x) +
                    d.z * d.z / (b.Size.z * b.Size.z)) < 0.11f) return true;
            }
            return false;
        }

        internal void Restore()
        {
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i].Root != null) UnityEngine.Object.Destroy(bodies[i].Root);
            bodies.Clear();
            candidates.Clear();
            activeCount = 0;
            deckActive = false;
            previousKey = null;
            previousGridX = previousGridZ = int.MinValue;
            previousPhase = -1;
            if (material != null) UnityEngine.Object.Destroy(material);
            if (noise != null) UnityEngine.Object.Destroy(noise);
            material = null;
            noise = null;
            failed = false;
            RestoreSkyClouds();
        }

        private void Hide()
        {
            for (int i = 0; i < bodies.Count; i++) bodies[i].Renderer.enabled = false;
            activeCount = 0;
            RestoreSkyClouds();
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

        private bool Prepare()
        {
            if (failed) return false;
            if (material == null)
            {
                Shader shader = CanopyShaderBundle.GetFlightCloudShader();
                if (shader == null || !shader.isSupported)
                {
                    logger?.LogWarning("[Weather] Flight cloud shader unavailable; native clouds remain visible.");
                    failed = true;
                    return false;
                }
                material = new Material(shader) { name = "Boscali Flight Clouds", renderQueue = 3000 };
            }
            if (Volatile.Read(ref sharedNoise) == null &&
                Interlocked.CompareExchange(ref generatingNoise, 1, 0) == 0)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    byte[] data;
                    try { data = CloudNoise3D.Generate(NoiseSize, 47); }
                    catch { data = Array.Empty<byte>(); }
                    Volatile.Write(ref sharedNoise, data);
                    Interlocked.Exchange(ref generatingNoise, 0);
                });
            }
            if (noise != null) return true;
            byte[] ready = Volatile.Read(ref sharedNoise);
            if (ready == null) return false;
            if (ready.Length != NoiseSize * NoiseSize * NoiseSize * 4)
            {
                failed = true;
                logger?.LogWarning("[Weather] Flight cloud noise generation failed.");
                return false;
            }
            var pixels = new Color32[NoiseSize * NoiseSize * NoiseSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                int n = i * 4;
                pixels[i] = new Color32(ready[n], ready[n + 1], 0, 255);
            }
            noise = new Texture3D(NoiseSize, NoiseSize, NoiseSize, TextureFormat.RGBA32, false)
            {
                name = "Boscali Flight Cloud Density", filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            noise.SetPixels32(pixels);
            noise.Apply(false, true);
            return true;
        }

        private void BuildLayout(WeatherField field, float cameraX, float cameraZ,
            int gridX, int gridZ, float altitudeShift, float driftX, float driftZ)
        {
            candidates.Clear();
            for (int dz = -GridRadius; dz <= GridRadius; dz++)
            for (int dx = -GridRadius; dx <= GridRadius; dx++)
            {
                int gx = gridX + dx, gz = gridZ + dz;
                float jitterX = (Hash01(gx, gz, field.Key.Seed) - 0.5f) * 5000f;
                float jitterZ = (Hash01(gz, gx, field.Key.Seed ^ 17u) - 0.5f) * 5000f;
                float x = (gx + 0.5f) * GridMetres + jitterX + driftX;
                float z = (gz + 0.5f) * GridMetres + jitterZ + driftZ;
                WeatherPoint point = field.Sample(x, z);
                float chance = Hash01(gx + 193, gz - 47, field.Key.Seed);
                if (point.Cover < 0.14f || chance > point.Cover * 0.94f) continue;
                float distance = (x - cameraX) * (x - cameraX) + (z - cameraZ) * (z - cameraZ);
                candidates.Add(new Candidate(x, z, point, distance,
                    Hash01(gx - 17, gz + 71, field.Key.Seed)));
            }
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            activeCount = 0;
            WeatherPoint regional = field.Sample(cameraX, cameraZ);
            deckActive = regional.FrontCover > 0.5f || field.Regime.Params.Overcast > 0.78f;
            if (deckActive)
            {
                float baseY = regional.CloudBase + altitudeShift;
                ConfigureBody(activeCount++, new Vector3(cameraX, baseY + 650f, cameraZ),
                    new Vector3(105000f, 1300f, 105000f), 0.34f * regional.Cover, true);
            }
            int limit = deckActive ? 10 : MaxBodies;
            for (int i = 0; i < candidates.Count && activeCount < limit; i++)
            {
                Candidate c = candidates[i];
                if (deckActive && c.Point.CoreDepth < 0.18f && c.Shape > 0.11f) continue;
                float baseY = c.Point.CloudBase + altitudeShift;
                float height = Mathf.Clamp(c.Point.CloudTop - c.Point.CloudBase,
                    1100f, 8500f);
                float width = Mathf.Lerp(2600f, 5200f, c.Shape) + c.Point.CoreDepth * 1700f;
                ConfigureBody(activeCount++, new Vector3(c.X, baseY + height * 0.5f, c.Z),
                    new Vector3(width, height, width * Mathf.Lerp(0.8f, 1.2f, c.Shape)),
                    Mathf.Clamp01(0.38f + 0.27f * c.Point.Cover + 0.18f * c.Point.CoreDepth), false);
            }
        }

        private void ConfigureBody(int index, Vector3 center, Vector3 size, float density, bool deck)
        {
            while (bodies.Count <= index) bodies.Add(CreateBody());
            Body b = bodies[index];
            b.GlobalCenter = center;
            b.Size = size;
            b.Deck = deck;
            b.Properties.SetFloat("_CloudDensity", density);
            b.Properties.SetFloat("_CloudType", deck ? 1f : 0f);
            b.Renderer.SetPropertyBlock(b.Properties);
        }

        private Body CreateBody()
        {
            if (cube == null) cube = BuildCube();
            var root = new GameObject("Boscali Flight Cloud");
            root.hideFlags = HideFlags.DontSave;
            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = cube;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return new Body { Root = root, Renderer = renderer,
                Properties = new MaterialPropertyBlock() };
        }

        private static Mesh BuildCube()
        {
            var m = new Mesh { name = "Boscali Cloud Volume" };
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

        private static float Hash01(int x, int z, uint seed)
        {
            uint h = (uint)x * 374761393u + (uint)z * 668265263u + seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / 4294967295f;
        }

        private sealed class Body
        {
            internal GameObject Root;
            internal MeshRenderer Renderer;
            internal MaterialPropertyBlock Properties;
            internal Vector3 GlobalCenter, Size;
            internal bool Deck;
        }

        private readonly struct Candidate
        {
            internal readonly float X, Z, Distance, Shape;
            internal readonly WeatherPoint Point;
            internal Candidate(float x, float z, WeatherPoint point, float distance, float shape)
            { X = x; Z = z; Point = point; Distance = distance; Shape = shape; }
        }
    }
}
