using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>Map-scale shock condensation, boiling dust and incandescent debris around the true mushroom volume.
    /// Particle poses are derived from site age, so late snapshots and acceptance age jumps show the same effect immediately.</summary>
    [DefaultExecutionOrder(1900)]
    internal sealed class Sa74Particles : MonoBehaviour, ISceneService
    {
        private const int MaximumSites = 2;
        private readonly List<Rig> rigs = new List<Rig>(MaximumSites);
        private static Sa74Particles instance;
        private void Awake() => instance = this;
        internal static bool DrawsPressure(uint siteId)
        {
            if (instance == null || !Ready) return false;
            foreach (Rig rig in instance.rigs)
                if (rig.Site.Id == siteId && rig.Root != null && rig.Root.activeInHierarchy) return true;
            return false;
        }
        private Material condensation, dust, heat, pressure;
        private Texture2D billow;
        private Mesh pressureShell;
        private static int groundQueries;
        private static int groundHits, groundMisses, groundPending;
        private static Layer debugDust;
        internal static bool Ready { get; private set; }
        internal static int Systems { get; private set; }
        internal static int Particles { get; private set; }
        internal static int ActiveSites { get; private set; }
        internal static float OuterRadius { get; private set; }
        internal static int CondensationParticles { get; private set; }
        internal static int EjectaParticles { get; private set; }
        internal static int DustParticles { get; private set; }
        internal static int PressureParticles { get; private set; }

        private enum Kind { PressureMist, Condensation, GroundDust, Ejecta, StemDust, SettlingDust, PressureShell }
        private sealed class Layer
        {
            internal Kind Kind;
            internal ParticleSystem System;
            internal ParticleSystem.Particle[] Buffer;
            internal Vector2[] GroundAt;
            internal float[] GroundHeight;
            internal ParticleSystemRenderer Renderer;
        }
        private sealed class Rig
        {
            internal Sa74Fallout.Site Site;
            internal GameObject Root;
            internal Layer[] Layers;
            internal bool Wet;
        }

        public void ResetForScene() => Clear();

        private bool Prepare()
        {
            if (Ready && condensation != null && dust != null && heat != null) return true;
            Material smokeSource = SmokeFx.Puff, glowSource = GlowFx.Glow;
            if (smokeSource == null || glowSource == null) return false;
            billow = MakeBillow();
            condensation = new Material(smokeSource) { name = "SA-74N pressure condensation", renderQueue = 3001 };
            dust = new Material(smokeSource) { name = "SA-74N boiling ejecta dust", renderQueue = 3000 };
            heat = new Material(glowSource) { name = "SA-74N incandescent cores", renderQueue = 3003 };
            pressure = new Material(smokeSource) { name = "SA-74N continuous pressure shell", renderQueue = 3002 };
            pressureShell = MakePressureShell();
            Bind(condensation, billow, new Color(1f, 1.03f, 1.04f));
            Bind(dust, billow, new Color(.80f, .77f, .72f));
            dust.DisableKeyword("_SOFTPARTICLES_ON"); dust.DisableKeyword("_FADING_ON");
            if (dust.HasProperty("_SoftParticlesEnabled")) dust.SetFloat("_SoftParticlesEnabled", 0f);
            if (dust.HasProperty("_CameraFadingEnabled")) dust.SetFloat("_CameraFadingEnabled", 0f);
            Bind(pressure, billow, new Color(1.32f, 1.35f, 1.38f));
            if (heat.HasProperty("_BaseColor")) heat.SetColor("_BaseColor", new Color(3.5f, 2.7f, 1.5f));
            if (heat.HasProperty("_Color")) heat.SetColor("_Color", new Color(3.5f, 2.7f, 1.5f));
            Ready = true;
            return true;
        }

        private void LateUpdate()
        {
            if (Application.isBatchMode || Datum.origin == null) return;
            Systems = Particles = ActiveSites = CondensationParticles = EjectaParticles = DustParticles = PressureParticles = 0;
            groundQueries = 256;
            groundPending = 0;
            OuterRadius = 0f;
            if (Sa74Fallout.Sites.Count == 0) { Clear(); return; }
            if (!Prepare()) return;
            float retireAge = Mathf.Max(180f, Domain.Sa74BlastShape.ArrivalSeconds(Sa74Fallout.ShockRadius) + 20f);
            int first = Mathf.Max(0, Sa74Fallout.Sites.Count - MaximumSites);
            for (int i = rigs.Count - 1; i >= 0; i--)
            {
                Rig rig = rigs[i];
                int index = Sa74Fallout.Sites.IndexOf(rig.Site);
                if (index >= first && Time.time - rig.Site.Started < retireAge) continue;
                if (rig.Root != null) Destroy(rig.Root);
                rigs.RemoveAt(i);
            }
            for (int i = first; i < Sa74Fallout.Sites.Count; i++)
            {
                Sa74Fallout.Site site = Sa74Fallout.Sites[i];
                if (Time.time - site.Started >= retireAge || HasRig(site)) continue;
                rigs.Add(Build(site));
            }
            foreach (Rig rig in rigs)
            {
                float age = Mathf.Max(0f, Time.time - rig.Site.Started);
                float growth = 1f - Mathf.Exp(-age / 72f);
                float height = 7000f + 51000f * growth;
                float radius = Sa74Fallout.FrontRadius(age);
                OuterRadius = Mathf.Max(OuterRadius, radius);
                Vector3 origin = Datum.origin.InverseTransformPoint(rig.Site.Position.ToLocalPosition());
                rig.Root.transform.position = rig.Site.Position.ToLocalPosition();
                foreach (Layer layer in rig.Layers)
                {
                    Pose(layer, rig.Site.Id, origin, age, growth, height, radius, rig.Site.GroundClearance, rig.Wet);
                    Systems++;
                    Particles += layer.System.particleCount;
                    if (layer.Kind == Kind.PressureMist || layer.Kind == Kind.PressureShell) PressureParticles += layer.System.particleCount;
                    else if (layer.Kind == Kind.Condensation) CondensationParticles += layer.System.particleCount;
                    else if (layer.Kind == Kind.Ejecta) EjectaParticles += layer.System.particleCount;
                    else if (layer.Kind != Kind.PressureMist) DustParticles += layer.System.particleCount;
                }
                ActiveSites++;
            }
        }

        private bool HasRig(Sa74Fallout.Site site)
        {
            foreach (Rig rig in rigs) if (ReferenceEquals(rig.Site, site)) return true;
            return false;
        }

        private Rig Build(Sa74Fallout.Site site)
        {
            var root = new GameObject("SA-74N cinematic shock / " + site.Id);
            root.transform.SetParent(Datum.origin, false);
            root.transform.position = site.Position.ToLocalPosition();
            // A burst whose surface is the sea lofts white spray and mist instead of brown dirt (matches Sa74Mushroom).
            Vector3 at = site.Position.ToLocalPosition();
            return new Rig { Site = site, Root = root, Wet = at.y - site.GroundClearance <= Datum.LocalSeaY + 1f, Layers = new[]
            {
                Make(root.transform, Kind.PressureMist, 480, condensation),
                Make(root.transform, Kind.PressureShell, 1, pressure),
                Make(root.transform, Kind.Condensation, 256, condensation),
                Make(root.transform, Kind.GroundDust, 1152, dust),
                Make(root.transform, Kind.Ejecta, 96, heat),
                Make(root.transform, Kind.StemDust, 320, dust),
                Make(root.transform, Kind.SettlingDust, 8, dust)
            } };
        }

        private Layer Make(Transform parent, Kind kind, int budget, Material material)
        {
            var root = new GameObject("SA-74N " + kind);
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            ParticleSystem system = root.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = false; main.loop = false; main.startSpeed = 0f;
            main.startLifetime = 10000f; main.maxParticles = budget;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = system.emission; emission.enabled = false;
            var shape = system.shape; shape.enabled = false;
            system.useAutoRandomSeed = false; system.randomSeed = 7400u + (uint)kind;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.minParticleSize = 0f; renderer.maxParticleSize = kind == Kind.Ejecta ? .2f : kind == Kind.GroundDust ? .3f : .5f;
            if (kind == Kind.PressureShell)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = pressureShell;
                renderer.alignment = ParticleSystemRenderSpace.World;
            }
            if (kind == Kind.Ejecta)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 2f; renderer.velocityScale = .18f;
                renderer.cameraVelocityScale = 0f;
            }
            root.SetActive(true);
            system.Play(); system.Pause();
            var layer = new Layer { Kind = kind, System = system, Renderer = renderer, Buffer = new ParticleSystem.Particle[budget] };
            if (kind == Kind.GroundDust || kind == Kind.SettlingDust)
            {
                layer.GroundAt = new Vector2[budget];
                layer.GroundHeight = new float[budget];
                for (int i = 0; i < budget; i++) layer.GroundHeight[i] = float.NaN;
                renderer.allowOcclusionWhenDynamic = false;
            }
            return layer;
        }

        private static void Pose(Layer layer, uint id, Vector3 origin, float age, float growth, float height, float front, float groundClearance, bool wet)
        {
            int count = 0;
            Vector3 minimum = Vector3.positiveInfinity, maximum = Vector3.negativeInfinity;
            float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 2f));
            if (layer.GroundHeight != null)
            {
                float groundAge = age - Domain.Sa74BlastShape.ArrivalSeconds(groundClearance);
                if (groundAge <= 0f) { layer.System.SetParticles(layer.Buffer, 0); return; }
                appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(groundAge / 2f));
                front = Mathf.Sqrt(Mathf.Max(0f, front * front - groundClearance * groundClearance));
            }
            float disperse = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(75f, 180f, age));
            // Condensation is a transient humidity response behind the pressure boundary, never a luminous permanent torus.
            float condensationFade = Mathf.Exp(-age / 11f) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(35f, 55f, age)));
            float arrival = Domain.Sa74BlastShape.ArrivalSeconds(Sa74Fallout.ShockRadius);
            float pressureFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(arrival, arrival + 20f, age));
            // A layer that has faded out is emptied once, not rebuilt particle by particle every frame until the rig retires.
            bool gone = layer.Kind == Kind.Condensation ? condensationFade < .004f
                : layer.Kind == Kind.PressureShell ? pressureFade <= 0f
                : layer.Kind == Kind.PressureMist && pressureFade <= 0f && condensationFade < .004f;
            if (gone) { if (layer.System.particleCount > 0) layer.System.SetParticles(layer.Buffer, 0); return; }
            for (int i = 0; i < layer.Buffer.Length; i++)
            {
                uint seed = unchecked(id * 7919u + (uint)i * 104729u + (uint)layer.Kind * 4937u);
                float a = Hash(seed), b = Hash(seed + 7u), c = Hash(seed + 37u);
                float angle = i * 2.39996323f + a * .7f;
                Vector3 position = Vector3.zero, velocity = Vector3.zero;
                Color color = Color.white;
                float size = 1f;
                switch (layer.Kind)
                {
                    case Kind.PressureMist:
                    {
                        int band = i % 6;
                        angle = (i / 6f) * (Mathf.PI * 2f / 80f) + a * .022f;
                        angle += (b - .5f) * .055f;
                        float elevation = (groundClearance > 120f ? -.35f + band * .18f : .025f + band * .105f) + (c - .5f) * .065f;
                        float ripple = Mathf.Sin(angle * 13f + age * .075f) * 140f + Mathf.Sin(angle * 29f) * 70f;
                        float radius = front - 180f - b * 420f + ripple;
                        float radial = Mathf.Cos(elevation) * radius;
                        position = Ring(angle, radial, Mathf.Sin(elevation) * radius);
                        size = Mathf.Max(650f, radial * .075f) * (.65f + a * .70f);
                        float patch = .35f + .65f * Mathf.SmoothStep(0f, 1f, .5f + .5f * Mathf.Sin(angle * 5f + band));
                        color = new Color(.91f, .91f, .89f, (.05f * pressureFade + .18f * condensationFade) * patch * appear);
                        break;
                    }
                    case Kind.PressureShell:
                    {
                        // One continuous curved front bridges the billows. Its UV alpha feathers the bottom and top of the shell.
                        position = Vector3.zero;
                        size = front;
                        color = new Color(.93f, .96f, 1f, .20f * pressureFade * appear);
                        break;
                    }
                    case Kind.Condensation:
                    {
                        // Cross-sections of the expanding sphere through two humid layers, rather than a ring floating under the cap.
                        float altitude = i % 2 == 0 ? 2200f : 5600f;
                        if (front <= altitude || condensationFade < .004f) continue;
                        angle = (i / 2f) * (Mathf.PI * 2f / 128f) + a * .018f;
                        float radial = Mathf.Sqrt(front * front - altitude * altitude) - b * 380f;
                        position = Ring(angle, radial, altitude + Mathf.Sin(angle * 11f + age * .08f) * 150f);
                        size = Mathf.Max(700f, radial * .075f) * (.85f + b * .4f);
                        float formation = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((front - altitude) / 900f));
                        color = new Color(.88f, .91f, .92f, .30f * condensationFade * formation * appear);
                        break;
                    }
                    case Kind.GroundDust:
                    {
                        // Three overlapping dirt masses: leading wall, broad turbulent wake, and a lingering blanket inside it.
                        int band = i % 3;
                        angle = (i / 3f) * (Mathf.PI * 2f / 384f) + b * .012f;
                        float roll = .5f + .5f * Mathf.Sin(age * (.15f + b * .08f) + angle * 3f);
                        float radial;
                        if (band == 0)
                        {
                            radial = Mathf.Max(0f, front - 500f - a * Mathf.Min(1800f, front * .10f));
                            position = Ring(angle, radial, 180f + roll * 700f + b * 300f);
                            size = Mathf.Clamp(front * .05f, 1000f, 2800f) * (.85f + .3f * a);
                        }
                        else if (band == 1)
                        {
                            radial = front * (.72f + .20f * a) - roll * 250f;
                            position = Ring(angle + .008f, Mathf.Max(0f, radial), 220f + roll * 850f + b * 450f);
                            size = Mathf.Clamp(front * .07f, 1500f, 3800f) * (.9f + .3f * roll);
                        }
                        else
                        {
                            radial = Mathf.Sqrt(a) * front * .78f;
                            position = Ring(angle + age * .001f, radial, 100f + roll * 350f + b * 200f);
                            size = Mathf.Clamp(front * .08f, 1800f, 4500f) * (.9f + .25f * b);
                        }
                        color = Color.Lerp(new Color(.56f, .43f, .29f), new Color(.39f, .38f, .35f), Mathf.Clamp01(age / 180f));
                        color.a = (band == 0 ? .62f : band == 1 ? .42f : .20f) * appear * disperse;
                        if (wet) color = new Color(.84f, .87f, .90f, color.a * .40f);
                        break;
                    }
                    case Kind.Ejecta:
                    {
                        // One finite launch, ballistic motion and cooling. Age never wraps to launch another firework.
                        float travel = age - a * 2.5f;
                        if (travel <= 0f || travel >= 50f) continue;
                        float horizontal = 250f + b * 550f, vertical = 360f + c * 480f;
                        float aboveGround = vertical * travel - 4.905f * travel * travel;
                        if (aboveGround <= 0f) continue;
                        position = Ring(angle, horizontal * travel, aboveGround);
                        velocity = Ring(angle, horizontal, vertical - 9.81f * travel);
                        size = 110f + a * 240f;
                        color = Color.Lerp(new Color(1f, .94f, .74f), new Color(.55f, .12f, .02f), Mathf.Clamp01(travel / 28f));
                        color.a = .72f * Mathf.Exp(-travel / 10f) *
                            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(22f, 50f, travel))) * appear;
                        break;
                    }
                    case Kind.StemDust:
                    {
                        float rise = Mathf.Repeat(age / (90f + b * 140f) + c, 1f);
                        float lifeFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rise * 7f)) *
                            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.78f, 1f, rise)));
                        angle += age * (.015f + b * .008f) + rise * 4f;
                        // Rides the surface of the shader's broad multi-megaton stem.
                        float radial = (1500f + growth * 9000f) * (1f + rise * .25f) + Mathf.Sin(angle * 4f + age * .08f) * 450f;
                        position = Ring(angle, radial, 250f + rise * height * .46f);
                        size = (1000f + a * 1800f) * (1f + rise * .35f);
                        color = Color.Lerp(new Color(.34f, .29f, .24f), new Color(.36f, .37f, .38f), rise);
                        color.a = .23f * lifeFade * appear * disperse;
                        if (wet) color = Color.Lerp(color, new Color(.80f, .82f, .84f, color.a), .7f);
                        break;
                    }
                    default:
                    {
                        position = Ring(angle, Mathf.Sqrt(a) * (1500f + growth * 8000f), 200f + b * 550f);
                        size = 3800f + a * 4500f;
                        color = wet ? new Color(.74f, .76f, .78f, .07f * appear * disperse) : new Color(.26f, .26f, .27f, .16f * appear * disperse);
                        break;
                    }
                }
                if (color.a <= .001f) continue;
                Vector3 particlePosition = origin + position;
                if (layer.GroundHeight != null) particlePosition = GroundHug(layer, i, particlePosition, position.y);
                if (layer.GroundHeight != null)
                {
                    Vector3 half = Vector3.one * (size * .5f);
                    minimum = Vector3.Min(minimum, particlePosition - half);
                    maximum = Vector3.Max(maximum, particlePosition + half);
                }
                layer.Buffer[count++] = new ParticleSystem.Particle
                {
                    position = particlePosition, velocity = velocity, startSize = size,
                    startColor = color, rotation = layer.Kind == Kind.PressureShell ? 0f : a * 360f + age * (b - .5f) * 3f,
                    startLifetime = 10000f, remainingLifetime = 10000f, randomSeed = seed + 1u
                };
            }
            layer.System.SetParticles(layer.Buffer, count);
            if (layer.GroundHeight != null && count > 0)
            {
                // Simulation is paused: provide conservative bounds from the positions actually submitted,
                // so an emitter-sized culling box cannot hide the manually posed wake.
                Vector3 center = Datum.origin.TransformPoint((minimum + maximum) * .5f);
                layer.Renderer.localBounds = new Bounds(layer.System.transform.InverseTransformPoint(center), maximum - minimum);
                if (layer.Kind == Kind.GroundDust) debugDust = layer;
            }
        }

        private static Vector3 GroundHug(Layer layer, int index, Vector3 point, float height)
        {
            Vector2 at = new Vector2(point.x, point.z);
            // No per-frame allocation or full-map search: refresh a puff's terrain sample only after it travels 250 metres.
            if (groundQueries > 0 && (float.IsNaN(layer.GroundHeight[index]) || (layer.GroundAt[index] - at).sqrMagnitude > 250f * 250f))
            {
                groundQueries--;
                Vector3 ray = Datum.origin.TransformPoint(new Vector3(point.x, 20000f, point.z));
                bool found = Physics.Raycast(ray, Vector3.down, out RaycastHit hit, 25000f,
                    PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
                if (found) groundHits++; else groundMisses++;
                layer.GroundHeight[index] = found ? Datum.origin.InverseTransformPoint(hit.point).y : 0f;
                layer.GroundAt[index] = at;
            }
            point.y = float.IsNaN(layer.GroundHeight[index]) ? point.y : layer.GroundHeight[index] + height;
            if (float.IsNaN(layer.GroundHeight[index])) groundPending++;
            return point;
        }

        internal static Dictionary<string, object> Diagnostics()
        {
            var result = new Dictionary<string, object>
            {
                ["groundHits"] = groundHits, ["groundMisses"] = groundMisses, ["groundPending"] = groundPending
            };
            if (debugDust?.System == null || Datum.origin == null) return result;
            Bounds bounds = debugDust.Renderer.bounds;
            Vector3 sample = Datum.origin.TransformPoint(debugDust.Buffer[0].position);
            result["sampleWorld"] = new[] { sample.x, sample.y, sample.z };
            result["sampleGround"] = debugDust.GroundHeight[0];
            result["sampleGroundWorldY"] = debugDust.GroundHeight[0] + Datum.origin.position.y;
            result["datumWorld"] = new[] { Datum.origin.position.x, Datum.origin.position.y, Datum.origin.position.z };
            result["boundsCenter"] = new[] { bounds.center.x, bounds.center.y, bounds.center.z };
            result["boundsSize"] = new[] { bounds.size.x, bounds.size.y, bounds.size.z };
            result["visible"] = debugDust.Renderer.isVisible;
            result["enabled"] = debugDust.Renderer.enabled && debugDust.Renderer.gameObject.activeInHierarchy;
            result["shader"] = debugDust.Renderer.sharedMaterial.shader.name;
            result["queue"] = debugDust.Renderer.sharedMaterial.renderQueue;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera camera = cameras != null ? cameras.mainCamera : Camera.main;
            if (camera != null)
            {
                Vector3 eye = Datum.origin.InverseTransformPoint(camera.transform.position), nearest = Vector3.zero;
                float distance = float.PositiveInfinity;
                int nearby = 0;
                for (int i = 0; i < debugDust.System.particleCount; i++)
                {
                    Vector3 delta = debugDust.Buffer[i].position - eye;
                    float squared = delta.sqrMagnitude;
                    if (squared < distance) { distance = squared; nearest = debugDust.Buffer[i].position; }
                    if (squared < 5000f * 5000f) nearby++;
                }
                Vector3 world = Datum.origin.TransformPoint(nearest);
                result["nearestDustMeters"] = Mathf.Sqrt(distance);
                result["dustWithin5km"] = nearby;
                result["nearestDustWorld"] = new[] { world.x, world.y, world.z };
                result["cameraWorld"] = new[] { camera.transform.position.x, camera.transform.position.y, camera.transform.position.z };
            }
            return result;
        }

        private static Mesh MakePressureShell()
        {
            const int sectors = 128, rows = 10;
            var vertices = new Vector3[(sectors + 1) * (rows + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[sectors * rows * 6];
            for (int row = 0; row <= rows; row++)
                for (int sector = 0; sector <= sectors; sector++)
                {
                    float t = row / (float)rows, angle = sector * Mathf.PI * 2f / sectors;
                    // Break the silhouette into rolling banks, keeping the lower lip continuous.
                    float edge = 1f + .16f * Mathf.Sin(angle * 7f) + .09f * Mathf.Sin(angle * 13f + .9f);
                    // A thin broken condensation lip rides above the dense dust wake.
                    // A broad luminous mesh here reads as a solid dome in the lateral view.
                    float elevation = (t - .18f) * .105f * edge + .007f * Mathf.Sin(angle * 8f);
                    int index = row * (sectors + 1) + sector;
                    vertices[index] = Ring(angle, Mathf.Cos(elevation), Mathf.Sin(elevation));
                    uv[index] = new Vector2(.5f + .25f * Mathf.Sin(angle * 9f + t * 3f), t);
                }
            int triangle = 0;
            for (int row = 0; row < rows; row++)
                for (int sector = 0; sector < sectors; sector++)
                {
                    int a = row * (sectors + 1) + sector, b = a + sectors + 1;
                    triangles[triangle++] = a; triangles[triangle++] = b; triangles[triangle++] = a + 1;
                    triangles[triangle++] = a + 1; triangles[triangle++] = b; triangles[triangle++] = b + 1;
                }
            var mesh = new Mesh { name = "SA-74N continuous pressure hemisphere", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.UploadMeshData(false); // Native mesh-particle batching may read the shared vertex buffer.
            return mesh;
        }

        private static Vector3 Ring(float angle, float radius, float height) => new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
        private static float Hash(uint value)
        {
            unchecked { value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu; value ^= value >> 16; }
            return (value & 0x00ffffffu) / 16777215f;
        }

        private static void Bind(Material material, Texture texture, Color color)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private static Texture2D MakeBillow()
        {
            const int side = 128;
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, true, true)
            { name = "SA-74N original billow alpha", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var pixels = new Color[side * side];
            for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
            {
                float u = (x + .5f) / side, v = (y + .5f) / side;
                float radius = new Vector2(u * 2f - 1f, v * 2f - 1f).magnitude;
                float noise = Mathf.PerlinNoise(u * 4.1f + .7f, v * 4.1f + .3f) * .58f +
                    Mathf.PerlinNoise(u * 10.7f + .1f, v * 10.7f + .9f) * .28f +
                    Mathf.PerlinNoise(u * 23.9f + .4f, v * 23.9f + .2f) * .14f;
                float edge = 1f - radius + (noise - .5f) * .40f;
                float rim = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.72f, 1f, radius));
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / .42f)) *
                    Mathf.Lerp(.25f, 1f, noise * noise) * rim;
                pixels[y * side + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels(pixels); texture.Apply(true, true);
            return texture;
        }

        private void Clear()
        {
            foreach (Rig rig in rigs) if (rig.Root != null) Destroy(rig.Root);
            rigs.Clear(); Systems = Particles = ActiveSites = CondensationParticles = EjectaParticles = DustParticles = PressureParticles = 0; OuterRadius = 0f;
            groundHits = groundMisses = groundPending = 0; debugDust = null;
        }
        private void OnDestroy()
        {
            if (instance == this) instance = null;
            Clear();
            if (condensation != null) Destroy(condensation);
            if (dust != null) Destroy(dust);
            if (heat != null) Destroy(heat);
            if (pressure != null) Destroy(pressure);
            if (pressureShell != null) Destroy(pressureShell);
            if (billow != null) Destroy(billow);
            condensation = dust = heat = pressure = null; pressureShell = null; billow = null; Ready = false;
        }
    }
}
