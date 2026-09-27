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
        private const int FrontSegmentMetres = 40000;
        private const int MaxBodies = 16;
        private static byte[] sharedNoise;
        private static int generatingNoise;
        private static Mesh cube;

        private readonly ManualLogSource logger;
        private readonly List<Body> bodies = new List<Body>(MaxBodies);
        private readonly List<Candidate> candidates = new List<Candidate>(32);
        private Material material;
        private Texture3D noise;
        private bool failed;
        private float nextLayout;
        private WeatherKey previousKey;
        private int activeCount;
        private bool deckActive;
        private WeatherField activeField;
        private float cloudShift;
        private Material skybox;
        private float nativeSkyClouds;

        internal int BodyCount => activeCount;
        internal bool Active => activeCount > 0 && material != null && noise != null;
        internal bool DeckActive => Active && deckActive;

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
            activeField = field;
            cloudShift = currentCloudHeight - field.Regional().CloudBase;
            if (Time.unscaledTime >= nextLayout || previousKey == null || !previousKey.Equals(field.Key))
            {
                BuildLayout(field, (float)global.x, (float)global.z, cloudShift);
                nextLayout = Time.unscaledTime + 1f;
                previousKey = field.Key;
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
            material.SetColor("_CloudAmbientColor", RenderSettings.ambientLight * 0.42f +
                RenderSettings.fogColor * 0.34f +
                Color.white * (Mathf.Clamp01(sunDirection.y * 1.5f) * 0.20f));
            material.SetColor("_CloudFogColor", RenderSettings.fogColor);
            material.SetFloat("_CloudStorm", Mathf.Clamp01(Mathf.Max(
                field.Regime.Params.Severity, field.Regime.Params.Frontal * 0.45f)));
            material.SetFloat("_CloudSteps", Mathf.Clamp01(PlayerSettings.graphics.CloudDetail) < 0.5f ? 20f : 32f);

            for (int i = 0; i < activeCount; i++)
            {
                Body body = bodies[i];
                body.Root.transform.position = body.GlobalCenter - offset;
                body.Root.transform.rotation = Quaternion.Euler(0f, body.Yaw, 0f);
                body.Root.transform.localScale = body.Size;
                body.Renderer.enabled = true;
            }
            for (int i = activeCount; i < bodies.Count; i++) bodies[i].Renderer.enabled = false;
            ReplaceSkyClouds(level);
        }

        internal bool InCloud(float x, float y, float z)
        {
            if (!Active || activeField == null) return false;
            WeatherPoint point = activeField.Sample(x, z);
            return point.Cover > 0.48f && y > point.CloudBase + cloudShift + 80f &&
                y < point.CloudTop + cloudShift - 80f;
        }

        internal void Restore()
        {
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i].Root != null) UnityEngine.Object.Destroy(bodies[i].Root);
            bodies.Clear();
            candidates.Clear();
            activeCount = 0;
            deckActive = false;
            activeField = null;
            previousKey = null;
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
            noise = new Texture3D(NoiseSize, NoiseSize, NoiseSize, TextureFormat.RGBA32, true)
            {
                name = "Boscali Flight Cloud Density", filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            noise.SetPixels32(pixels);
            noise.Apply(true, true);
            return true;
        }

        private void BuildLayout(WeatherField field, float cameraX, float cameraZ, float altitudeShift)
        {
            candidates.Clear();
            WeatherPoint local = field.Sample(cameraX, cameraZ);
            deckActive = local.BackgroundCover > 0.42f;
            if (deckActive)
            {
                float baseY = field.Regional().CloudBase + altitudeShift;
                candidates.Add(new Candidate(0f, baseY + 850f, 0f,
                    110000f, 1700f, 110000f, 0f, 0.24f * local.BackgroundCover,
                    true, 0f));
            }

            for (int i = 0; i < field.FrontCount; i++)
            {
                FrontState front = field.Front(i);
                if (front.Strength < 0.15f) continue;
                float cameraAlong = -cameraX * front.NormalZ + cameraZ * front.NormalX;
                int centreSegment = Mathf.FloorToInt(cameraAlong / FrontSegmentMetres);
                float behind = front.Kind == FrontKind.Warm ? -32000f : 14000f;
                float width = front.Kind == FrontKind.Warm ? 100000f : 52000f;
                float height = front.Kind == FrontKind.Warm ? 2300f : 1800f;
                float yaw = Mathf.Atan2(-front.NormalZ, front.NormalX) * Mathf.Rad2Deg;
                for (int segment = centreSegment - 1; segment <= centreSegment + 1; segment++)
                {
                    float along = (segment + 0.5f) * FrontSegmentMetres;
                    float across = front.OffsetAtAlong(along) - behind;
                    float x = front.NormalX * across - front.NormalZ * along;
                    float z = front.NormalZ * across + front.NormalX * along;
                    float nearestAcross = Mathf.Max(0f, Mathf.Abs(front.SignedDistance(cameraX, cameraZ) - behind) - width * 0.5f);
                    float nearestAlong = Mathf.Max(0f, Mathf.Abs(cameraAlong - along) - FrontSegmentMetres * 0.55f);
                    float distance = nearestAcross * nearestAcross + nearestAlong * nearestAlong;
                    if (distance > 50000f * 50000f) continue;
                    WeatherPoint point = field.Sample(x, z);
                    candidates.Add(new Candidate(x, point.CloudBase + altitudeShift + height * 0.5f, z,
                        width, height, FrontSegmentMetres * 1.1f, yaw,
                        front.Strength * (front.Kind == FrontKind.Warm ? 0.42f : 0.58f), true, distance));
                }
            }

            for (int i = 0; i < field.CellCount; i++)
            {
                StormCell cell = field.Cell(i);
                float dx = cell.X - cameraX, dz = cell.Z - cameraZ;
                float distance = dx * dx + dz * dz;
                if (distance > 55000f * 55000f || cell.CloudLevel < 0.03f) continue;
                float height = Mathf.Max(1400f, cell.Top - cell.Base);
                float width = cell.Radius * 3.2f;
                candidates.Add(new Candidate(cell.X, cell.Base + altitudeShift + height * 0.5f, cell.Z,
                    width, height, width * 0.85f, 0f,
                    cell.CloudLevel * (cell.Severe ? 0.82f : 0.70f), false, distance));
                if (cell.Severe && cell.Age > 0.4f && cell.Age < 0.95f)
                    candidates.Add(new Candidate(cell.X + cell.VelocityX * 120f,
                        cell.Top + altitudeShift - 400f, cell.Z + cell.VelocityZ * 120f,
                        width * 1.8f, 800f, width * 1.5f, 0f,
                        cell.CloudLevel * 0.48f, true, distance + 1f));
            }

            for (int i = 0; i < field.CloudClusterCount; i++)
            {
                DryCloudCluster cloud = field.CloudCluster(i);
                float dx = cloud.X - cameraX, dz = cloud.Z - cameraZ;
                float distance = dx * dx + dz * dz;
                if (distance > 55000f * 55000f) continue;
                float height = cloud.Top - cloud.Base;
                candidates.Add(new Candidate(cloud.X, cloud.Base + altitudeShift + height * 0.5f,
                    cloud.Z, cloud.Radius * 2.8f, height, cloud.Radius * 2.4f, 0f,
                    cloud.Strength * 0.8f, false, distance));
            }

            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            activeCount = Mathf.Min(candidates.Count, MaxBodies);
            for (int i = 0; i < activeCount; i++) ConfigureBody(i, candidates[i]);
        }

        private void ConfigureBody(int index, Candidate c)
        {
            while (bodies.Count <= index) bodies.Add(CreateBody());
            Body b = bodies[index];
            b.GlobalCenter = new Vector3(c.X, c.Y, c.Z);
            b.Size = new Vector3(c.Width, c.Height, c.Length);
            b.Yaw = c.Yaw;
            b.Properties.SetFloat("_CloudDensity", c.Density);
            b.Properties.SetFloat("_CloudType", c.Deck ? 1f : 0f);
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

        private sealed class Body
        {
            internal GameObject Root;
            internal MeshRenderer Renderer;
            internal MaterialPropertyBlock Properties;
            internal Vector3 GlobalCenter, Size;
            internal float Yaw;
        }

        private readonly struct Candidate
        {
            internal readonly float X, Y, Z, Width, Height, Length, Yaw, Density, Distance;
            internal readonly bool Deck;
            internal Candidate(float x, float y, float z, float width, float height, float length,
                float yaw, float density, bool deck, float distance)
            {
                X = x; Y = y; Z = z; Width = width; Height = height; Length = length;
                Yaw = yaw; Density = density; Deck = deck; Distance = distance;
            }
        }
    }
}
