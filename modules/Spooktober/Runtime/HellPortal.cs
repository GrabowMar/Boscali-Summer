using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
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
        private readonly HellPortalGround ground;
        private static Gradient ashColour, cinderColour;
        public float Scale { get; private set; } = 1f;
        internal bool IsDimensionExit { get; set; }

        private HellPortal(Vector3 worldPos, Quaternion facing, float radius)
        {
            Radius = Mathf.Max(10f, radius);
            Root = new GameObject("HellPortal");
            if (Datum.origin != null) Root.transform.SetParent(Datum.origin, true);
            Root.transform.SetPositionAndRotation(worldPos, facing);
            ground = new HellPortalGround();
            ground.Open(worldPos, Radius);
            Shader shader = SpookAssets.HellPortal;
            if (shader != null)
            {
                GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(disc.GetComponent<Collider>());
                disc.name = "Abyss";
                disc.transform.SetParent(Root.transform, false);
                disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                disc.transform.localScale = Vector3.one * (Radius * 2f);
                material = new Material(shader) { name = "BoscaliSummer.HellPortal" };
                MeshRenderer r = disc.GetComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            // Cosmetic failures must leave a registered, closable gate.
            try
            {
                FallingAsh(Radius);
                Cinders(Radius);
            }
            catch (System.Exception e) { Plugin.Logger.LogWarning("[Spooktober] hell rift atmosphere: " + e.Message); }
        }

        /// <summary>Grows on the ground instead of lifting away from it.</summary>
        public void SetScale(float scale)
        {
            if (Root == null) return;
            Scale = Mathf.Clamp(scale, 1f, 3f);
            Root.transform.localScale = Vector3.one * Scale;
            ground.Open(Root.transform.position, Radius * Scale);
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
        public static HellPortal Spawn(Vector3 worldPos, Quaternion facing, float radius = 60f)
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
            if (Root != null) Object.Destroy(Root);
            Open.Remove(this);
        }
        public static void CloseAll() { for (int i = Open.Count - 1; i >= 0; i--) Open[i].Close(); }
        public static int Count => Open.Count;
        public static IReadOnlyList<HellPortal> All => Open;
        public static HellPortal First => Open.Count > 0 ? Open[0] : null;

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
        }

        private static Gradient AshColour => ashColour ?? (ashColour = DustGradient(new Color(.075f, .065f, .06f), .42f));
        private static Gradient CinderColour => cinderColour ?? (cinderColour = DustGradient(new Color(.14f, .003f, .001f), .55f));

        private static Gradient DustGradient(Color color, float alpha)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color * .35f, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, .18f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }
    }
}
