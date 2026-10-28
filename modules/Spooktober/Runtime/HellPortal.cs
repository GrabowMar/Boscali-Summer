using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// The hell portal's look: a vertical vortex disc (Boscali/HellPortal on a quad, facing its forward axis) and fire embers
    /// spiralling in from the rim. Local to this peer, parented under the floating origin so it stays put on a shift. Visual
    /// only: no collider, no trigger (flying through it is the Hell's Army director's job, by distance to <see cref="Root"/>).
    /// </summary>
    internal sealed class HellPortal
    {
        private static readonly List<HellPortal> Open = new List<HellPortal>();

        public readonly GameObject Root;
        public readonly float Radius;
        private readonly Material material;
        private readonly Vector3 baseLocal;

        /// <summary>How much the gate has grown (1 = <see cref="Radius"/>).</summary>
        public float Scale { get; private set; } = 1f;

        private HellPortal(Vector3 worldPos, Quaternion facing, float radius)
        {
            Radius = radius;
            Root = new GameObject("HellPortal");
            if (Datum.origin != null) Root.transform.SetParent(Datum.origin, true);
            Root.transform.SetPositionAndRotation(worldPos, facing);

            Shader shader = SpookAssets.HellPortal;
            if (shader != null)
            {
                GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(disc.GetComponent<Collider>());
                disc.name = "Vortex";
                disc.transform.SetParent(Root.transform, false);
                disc.transform.localScale = Vector3.one * (radius * 2f);
                material = new Material(shader) { name = "BoscaliSummer.HellPortal" };
                MeshRenderer r = disc.GetComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            baseLocal = Root.transform.localPosition;
            // Cosmetic: a particle failure must not leave a half-made gate outside Open, where nothing can close it.
            try
            {
                Swirl(radius);
                Pillar(radius);
            }
            catch (System.Exception e) { Plugin.Logger.LogWarning("[Spooktober] hell portal embers: " + e.Message); }
        }

        /// <summary>Grows the gate to <paramref name="scale"/> times its radius, lifting it so its rim stays clear of the ground.</summary>
        public void SetScale(float scale)
        {
            if (Root == null) return;
            Scale = Mathf.Max(1f, scale);
            Root.transform.localScale = Vector3.one * Scale;
            Root.transform.localPosition = baseLocal + Vector3.up * (Radius * (Scale - 1f));
        }

        /// <summary>The gate tearing open: a flat ring of fire racing out along the ground from its foot.</summary>
        public void Shockwave()
        {
            if (Root == null || Datum.origin == null) return;
            ParticleSystem ps = RadGhostLook.NewEmbers("HellShockwave", Datum.origin, 10f, 900, RadGhostLook.FireColour);
            ps.gameObject.SetActive(false);
            ps.transform.position = Root.transform.position - Vector3.up * Radius;
            ps.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // the circle shape lies in local XY: turn it flat
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(140f, 220f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 10f;
            shape.radiusThickness = 0f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(700 * FxBus.Scales.Particles)) });
            ps.gameObject.SetActive(true);
            ps.Play();
            Object.Destroy(ps.gameObject, 3.5f);
        }

        /// <summary>Is the portal (still) there?</summary>
        public bool Alive => Root != null;

        /// <summary>Opens a portal of <paramref name="radius"/> m at a world point, its disc facing <paramref name="facing"/>'s forward.</summary>
        public static HellPortal Spawn(Vector3 worldPos, Quaternion facing, float radius = 60f)
        {
            var portal = new HellPortal(worldPos, facing, radius);
            Open.Add(portal);
            return portal;
        }

        public void Close()
        {
            if (material != null) Object.Destroy(material);
            if (Root != null) Object.Destroy(Root);
            Open.Remove(this);
        }

        /// <summary>Plugin teardown or scene reset: every portal goes (before the shader's bundle unloads).</summary>
        public static void CloseAll()
        {
            for (int i = Open.Count - 1; i >= 0; i--) Open[i].Close();
        }

        /// <summary>How many portals are open (sim readout).</summary>
        public static int Count => Open.Count;

        /// <summary>Every open portal (the map sign).</summary>
        public static IReadOnlyList<HellPortal> All => Open;

        /// <summary>The oldest open portal, or null (sim camera).</summary>
        public static HellPortal First => Open.Count > 0 ? Open[0] : null;

        /// <summary>A pillar of fire rising from the gate's top, hundreds of metres high: the gate is visible from far away.</summary>
        private void Pillar(float radius)
        {
            ParticleSystem ps = RadGhostLook.NewEmbers("HellPillar", Root.transform, 20f, 1600, RadGhostLook.FireColour);
            ps.gameObject.SetActive(false);
            ps.transform.localPosition = Vector3.up * radius;
            ps.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // circle flat, emitting round the gate's top
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.35f;
            ParticleSystem.VelocityOverLifetimeModule rise = ps.velocityOverLifetime;
            rise.space = ParticleSystemSimulationSpace.World;
            rise.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            rise.y = new ParticleSystem.MinMaxCurve(60f, 110f);
            rise.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 180f * FxBus.Scales.Particles;
            ps.gameObject.SetActive(true);
            ps.Play();
        }

        /// <summary>Embers born on the rim circle, orbiting the axis and falling inwards to the core within their life.</summary>
        private void Swirl(float radius)
        {
            float scale = radius / 15f;
            ParticleSystem ps = RadGhostLook.NewEmbers("HellPortalEmbers", Root.transform, scale, 1200, RadGhostLook.FireColour);
            ps.gameObject.SetActive(false); // module edits on a playing system warn; re-enabled below
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle; // the circle lies in local XY, the quad's plane
            shape.radius = radius;
            shape.radiusThickness = 0f; // the rim only
            ParticleSystem.VelocityOverLifetimeModule swirl = ps.velocityOverLifetime;
            swirl.space = ParticleSystemSimulationSpace.Local;
            swirl.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            swirl.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            swirl.z = new ParticleSystem.MinMaxCurve(-0.05f * radius, 0.05f * radius); // a little depth off the disc
            swirl.orbitalZ = new ParticleSystem.MinMaxCurve(0.8f, 1.4f); // rad/s round the axis
            swirl.radial = new ParticleSystem.MinMaxCurve(-0.45f * radius, -0.35f * radius); // inwards, reaching the core in ~2.5 s
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 400f * FxBus.Scales.Particles;
            ps.gameObject.SetActive(true);
            ps.Play();
        }
    }
}
