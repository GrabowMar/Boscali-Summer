using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Spooktober.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>A ground rift: an irregular black well, torn dark lip and sparse falling ash. Root.forward remains the
    /// invasion's march direction; the visual disc lies in the ground plane. Terrain hole edits are restored on close.</summary>
    internal sealed class HellPortal
    {
        private static readonly List<HellPortal> Open = new List<HellPortal>();
        public readonly GameObject Root;
        public readonly float Radius;
        private readonly Material material;
        private readonly Mesh surfaceMesh;
        private readonly Transform surface;
        private readonly HellPortalGround ground;
        private readonly List<ParticleSystem> effects = new List<ParticleSystem>(4);
        private static Gradient ashColour, cinderColour;
        public float Scale { get; private set; } = 1f;
        internal bool IsDimensionExit { get; set; }
        internal bool AuthoredEffects { get; private set; }
        internal int ParticleBudget { get; private set; }
        internal int EffectSystems => effects.Count;
        internal int ParticleCount
        {
            get { int count = 0; foreach (ParticleSystem effect in effects) if (effect != null) count += effect.particleCount; return count; }
        }

        private HellPortal(Vector3 worldPos, Quaternion facing, float radius)
        {
            Radius = Mathf.Max(10f, radius);
            Root = new GameObject("HellPortal");
            if (Datum.origin != null) Root.transform.SetParent(Datum.origin, true);
            Root.transform.SetPositionAndRotation(worldPos, facing);
            ground = new HellPortalGround();
            Shader shader = SpookAssets.HellPortal;
            if (shader != null)
            {
                var disc = new GameObject("Abyss");
                disc.transform.SetParent(Root.transform, false);
                surface = disc.transform;
                // Sample before opening a true Terrain hole, and cover every future growth stage once.
                surfaceMesh = BuildSurface();
                disc.AddComponent<MeshFilter>().sharedMesh = surfaceMesh;
                material = new Material(shader) { name = "BoscaliSummer.HellPortal" };
                material.SetFloat("_GroundMesh", 1f);
                material.SetFloat("_ExtentScale", HellRules.MaxGateScale);
                MeshRenderer r = disc.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            ground.Open(worldPos, Radius);
            // Cosmetic failures must leave a registered, closable gate.
            try
            {
                if (!AuthoredAtmosphere())
                {
                    FallingAsh(Radius);
                    Cinders(Radius);
                }
            }
            catch (System.Exception e) { Plugin.Logger.LogWarning("[Spooktober] hell rift atmosphere: " + e.Message); }
        }

        /// <summary>Grows on the ground instead of lifting away from it.</summary>
        public void SetScale(float scale)
        {
            if (Root == null) return;
            Scale = Mathf.Clamp(scale, 1f, HellRules.MaxGateScale);
            Root.transform.localScale = Vector3.one * Scale;
            // The shader grows over a fixed terrain envelope; neither hills nor roads move as the gate grows.
            if (surface != null) surface.localScale = Vector3.one / Scale;
            if (material != null) material.SetFloat("_ExtentScale", HellRules.MaxGateScale / Scale);
            ground.Open(Root.transform.position, Radius * Scale);
        }

        private Mesh BuildSurface()
        {
            const int side = 129;
            var vertices = new Vector3[side * side];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(side - 1) * (side - 1) * 6];
            float extent = Radius * HellRules.MaxGateScale;
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    uv[i] = new Vector2(x / (float)(side - 1), z / (float)(side - 1));
                    Vector3 world = Root.transform.TransformPoint(new Vector3((uv[i].x * 2f - 1f) * extent, 0f,
                        (uv[i].y * 2f - 1f) * extent));
                    Vector3 from = new Vector3(world.x, Root.transform.position.y + 750f, world.z);
                    world.y = HellTerritory.Surface(from, out RaycastHit hit, out bool _) ? hit.point.y + .5f :
                        Root.transform.position.y - 2.5f;
                    vertices[i] = Root.transform.InverseTransformPoint(world);
                    if (x == side - 1 || z == side - 1) continue;
                    int t = (z * (side - 1) + x) * 6;
                    triangles[t] = i; triangles[t + 1] = i + side; triangles[t + 2] = i + 1;
                    triangles[t + 3] = i + 1; triangles[t + 4] = i + side; triangles[t + 5] = i + side + 1;
                }
            HellGroundEnvelope.Raise(vertices, null, side, Root.transform.localToWorldMatrix,
                Root.transform.position.y + 750f, .5f);
            var mesh = new Mesh { name = "HellRiftTerrainEnvelope", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A loose, low ash disturbance on opening or collapse; the retained name is the director's cue API.</summary>
        public void Shockwave()
        {
            if (Root == null || Datum.origin == null) return;
            Material ashMaterial = SmokeFx.Puff;
            if (ashMaterial == null) return;
            ParticleSystem ps = RadGhostLook.NewEmbers("HellRiftDisturbance", Datum.origin, 1f, 96, AshColour);
            ps.gameObject.SetActive(false);
            ps.transform.position = Root.transform.position;
            ps.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(.4f, 1.1f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Radius * Scale * .85f;
            shape.radiusThickness = .25f;
            ParticleSystem.VelocityOverLifetimeModule fall = ps.velocityOverLifetime;
            fall.space = ParticleSystemSimulationSpace.Local;
            fall.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            fall.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            fall.z = new ParticleSystem.MinMaxCurve(.25f, .75f);
            fall.radial = new ParticleSystem.MinMaxCurve(-2f, -1f);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(48 * FxBus.Scales.Particles)) });
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ashMaterial;
            renderer.minParticleSize = 0f;
            ps.gameObject.SetActive(true);
            ps.Play();
            Object.Destroy(ps.gameObject, 4.5f);
        }

        public bool Alive => Root != null;
        /// <summary>A horizontal rift just above terrain. Facing sets the invasion's exit direction.</summary>
        public static HellPortal Spawn(Vector3 worldPos, Quaternion facing, float radius = 145f)
        {
            var portal = new HellPortal(worldPos, facing, radius);
            Open.Add(portal);
            return portal;
        }
        public void Close()
        {
            ground.Close();
            if (Root != null && Application.isPlaying) Shockwave();
            if (material != null) Object.Destroy(material);
            if (surfaceMesh != null) Object.Destroy(surfaceMesh);
            if (Root != null) Object.Destroy(Root);
            Open.Remove(this);
        }
        public static void CloseAll() { for (int i = Open.Count - 1; i >= 0; i--) Open[i].Close(); }
        public static int Count => Open.Count;
        public static IReadOnlyList<HellPortal> All => Open;
        public static HellPortal First => Open.Count > 0 ? Open[0] : null;

        private bool AuthoredAtmosphere()
        {
            GameObject prefab = SpookAssets.PortalParticles;
            Material smoke = SmokeFx.Puff, sparks = GlowFx.Glow;
            if (prefab == null || smoke == null || sparks == null) return false;
            var holder = new GameObject("RQL Hell Rift Atmosphere");
            holder.SetActive(false); // no emission before shared materials and all caps have been assigned
            holder.transform.SetParent(Root.transform, false);
            holder.transform.localScale = Vector3.one * (Radius / 145f);
            try
            {
                GameObject instance = Object.Instantiate(prefab, holder.transform, false);
                instance.name = "HellPortalParticles";
                instance.SetActive(true);
                ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                if (systems.Length != 4) throw new System.InvalidOperationException("Authored rift must contain four particle systems");
                float quality = Mathf.Clamp01(FxBus.Scales.Particles);
                var found = new HashSet<string>();
                foreach (ParticleSystem ps in systems)
                {
                    ps.gameObject.SetActive(true);
                    int cap = ps.name == "Smoke" ? 96 : ps.name == "Ash" ? 120 : ps.name == "Cinders" ? 32 : ps.name == "Arcs" ? 12 : 0;
                    if (cap == 0 || !found.Add(ps.name)) throw new System.InvalidOperationException("Unexpected rift emitter " + ps.name);
                    ParticleSystem.MainModule main = ps.main;
                    main.playOnAwake = false;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.maxParticles = Mathf.Max(1, Mathf.FloorToInt(cap * quality));
                    bool smoky = ps.name == "Smoke" || ps.name == "Ash";
                    ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
                    renderer.sharedMaterial = smoky ? smoke : sparks;
                    renderer.trailMaterial = renderer.sharedMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.minParticleSize = 0f;
                    ParticleSystem.EmissionModule emission = ps.emission;
                    emission.rateOverTimeMultiplier *= quality;
                    emission.rateOverDistanceMultiplier *= quality;
                    ParticleSystem.CollisionModule collision = ps.collision; collision.enabled = false;
                    ParticleSystem.TriggerModule triggers = ps.trigger; triggers.enabled = false;
                    ParticleSystem.LightsModule lights = ps.lights; lights.enabled = false;
                    ParticleSystem.SubEmittersModule subEmitters = ps.subEmitters; subEmitters.enabled = false;
                    ParticleSystem.VelocityOverLifetimeModule pull = ps.velocityOverLifetime;
                    pull.enabled = true;
                    pull.space = ParticleSystemSimulationSpace.Local;
                    pull.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                    pull.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                    pull.y = ps.name == "Smoke" ? new ParticleSystem.MinMaxCurve(-1.1f, -.4f)
                        : ps.name == "Ash" ? new ParticleSystem.MinMaxCurve(-2.8f, -1.2f) : new ParticleSystem.MinMaxCurve(-.5f, .5f);
                    pull.radial = ps.name == "Smoke" ? new ParticleSystem.MinMaxCurve(-16f, -8f)
                        : ps.name == "Arcs" ? new ParticleSystem.MinMaxCurve(-70f, -35f) : new ParticleSystem.MinMaxCurve(-10f, -4f);
                    ParticleSystem.ColorOverLifetimeModule tint = ps.colorOverLifetime;
                    tint.enabled = true;
                    tint.color = ps.name == "Smoke" ? SmokeColour : ps.name == "Ash" ? AshColour : ps.name == "Arcs" ? ArcColour : CinderColour;
                    if (ps.name == "Smoke") ps.transform.localPosition += Vector3.up * 9f;
                    effects.Add(ps);
                    ParticleBudget += main.maxParticles;
                }
                AuthoredEffects = true;
                holder.SetActive(true);
                foreach (ParticleSystem effect in effects) effect.Play(false);
                return true;
            }
            catch (System.Exception error)
            {
                holder.SetActive(false);
                Object.Destroy(holder);
                effects.Clear(); ParticleBudget = 0; AuthoredEffects = false;
                Plugin.Logger.LogWarning("[Spooktober] authored rift atmosphere: " + error.Message);
                return false;
            }
        }

        private void FallingAsh(float radius)
        {
            Material ashMaterial = SmokeFx.Puff;
            if (ashMaterial == null) return;
            ParticleSystem ps = RadGhostLook.NewEmbers("HellRiftAsh", Root.transform, 1f, 80, AshColour);
            ps.gameObject.SetActive(false);
            ps.transform.localPosition = Vector3.up * (radius * .07f);
            ps.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.25f, .7f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * .86f;
            shape.radiusThickness = .25f;
            ParticleSystem.VelocityOverLifetimeModule fall = ps.velocityOverLifetime;
            fall.space = ParticleSystemSimulationSpace.Local;
            fall.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            fall.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            fall.z = new ParticleSystem.MinMaxCurve(1.2f, 2.5f); // local +Z points down into the horizontal rift
            fall.radial = new ParticleSystem.MinMaxCurve(-radius * .04f, -radius * .02f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.strength = .12f;
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ashMaterial;
            renderer.minParticleSize = 0f; // distant ash must not inflate into visible white specks
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 12f * FxBus.Scales.Particles;
            ps.gameObject.SetActive(true);
            ps.Play();
            effects.Add(ps);
            ParticleBudget += main.maxParticles;
        }

        private void Cinders(float radius)
        {
            ParticleSystem ps = RadGhostLook.NewEmbers("HellRiftCinders", Root.transform, 1f, 12, CinderColour);
            ps.gameObject.SetActive(false);
            ps.transform.localPosition = Vector3.up * 2f;
            ps.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(.12f, .25f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * .84f;
            shape.radiusThickness = .3f;
            ParticleSystem.VelocityOverLifetimeModule fall = ps.velocityOverLifetime;
            fall.space = ParticleSystemSimulationSpace.Local;
            fall.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            fall.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            fall.z = new ParticleSystem.MinMaxCurve(.5f, 1f);
            fall.radial = new ParticleSystem.MinMaxCurve(-radius * .05f, -radius * .025f);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.strength = .1f;
            ps.GetComponent<ParticleSystemRenderer>().minParticleSize = 0f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 1.25f * FxBus.Scales.Particles;
            ps.gameObject.SetActive(true);
            ps.Play();
            effects.Add(ps);
            ParticleBudget += main.maxParticles;
        }

        private static Gradient AshColour => ashColour ?? (ashColour = DustGradient(new Color(.075f, .065f, .06f), .42f));
        private static Gradient smokeColour, arcColour;
        private static Gradient SmokeColour => smokeColour ?? (smokeColour = DustGradient(new Color(.035f, .027f, .024f), .38f));
        private static Gradient CinderColour => cinderColour ?? (cinderColour = DustGradient(new Color(.8f, .03f, .008f), .65f));
        private static Gradient ArcColour => arcColour ?? (arcColour = DustGradient(new Color(1.5f, .022f, .005f), .75f));

        private static Gradient DustGradient(Color color, float alpha)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color * .35f, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, .18f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }
    }
}
