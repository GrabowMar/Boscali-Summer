using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>A little residual smoke from the pieces that actually reached a severe dry impact.</summary>
    internal sealed class MegaplaneWreckSmoke : MonoBehaviour
    {
        private MegaplaneWreck wreck;
        private ParticleSystem smoke;
        private MeshFilter engine, hull;
        private float next;
        private int puffs;

        internal static void Add(MegaplaneWreck field)
        {
            MegaplaneCrashRecord impact = field.Impact;
            float closing = -Vector3.Dot(impact.Velocity, impact.Normal);
            if (GameManager.IsHeadless || impact.Water || impact.Age >= 65f ||
                closing < 27f && impact.Velocity.sqrMagnitude < 125f * 125f) return;
            var host = field.gameObject.AddComponent<MegaplaneWreckSmoke>();
            host.wreck = field;
            float bestEngine = float.PositiveInfinity, bestHull = float.PositiveInfinity;
            foreach (var part in field.GetComponentsInChildren<MeshFilter>(true))
            {
                if (part.sharedMesh == null) continue;
                bool pod = part.name.StartsWith("Pod_") && part.name.Length == 6;
                if (!pod && part.name != "Fuselage_Center" && part.name != "Fuselage_Forward" && part.name != "Fuselage_Aft") continue;
                Vector3 center = part.transform.TransformPoint(part.sharedMesh.bounds.center);
                float distance = (center - field.transform.position).sqrMagnitude;
                if (pod && distance < bestEngine) { bestEngine = distance; host.engine = part; }
                if (!pod && distance < bestHull) { bestHull = distance; host.hull = part; }
            }
            host.next = Mathf.Max(4f, impact.Age);
            if (host.engine == null && host.hull == null) Destroy(host);
        }

        private void Update()
        {
            float age = wreck.Age;
            if (age >= 65f)
            {
                if (smoke != null) smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                enabled = false;
                return;
            }
            if (age < next) return;
            float remaining = 1f - age / 65f;
            next = age + Mathf.Lerp(1.1f, .38f, remaining);
            MeshFilter source = engine != null && (hull == null || ++puffs % 3 != 0) ? engine : hull;
            if (source == null) return;
            Bounds bounds = source.sharedMesh.bounds;
            Vector3 point = source.transform.TransformPoint(bounds.center) + Vector3.up * Mathf.Min(bounds.extents.y * .65f, 5f);
            if (point.y <= Datum.LocalSeaY) return;
            if (smoke == null)
            {
                Material material = SmokeFx.Puff;
                if (material == null) { enabled = false; return; }
                smoke = CreateEmitter(material);
            }
            float phase = age * .82f + wreck.Impact.Spawn;
            smoke.Emit(new ParticleSystem.EmitParams {
                position = transform.InverseTransformPoint(point),
                velocity = new Vector3(1.2f + .45f * Mathf.Sin(phase), 2.1f + remaining, -.55f + .32f * Mathf.Cos(phase)),
                startSize = Mathf.Lerp(1.2f, 2.8f, remaining),
                startLifetime = 6f + 2f * remaining,
                startColor = new Color(.26f, .27f, .26f, .12f + .35f * remaining)
            }, 1);
        }

        private ParticleSystem CreateEmitter(Material material)
        {
            var emitter = new GameObject("Residual wreck smoke");
            emitter.SetActive(false);
            emitter.transform.SetParent(transform, false);
            var particles = emitter.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 30;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var growth = particles.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .45f, 1f, 2.8f));
            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var opacity = new Gradient();
            opacity.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .06f),
                    new GradientAlphaKey(.7f, .35f), new GradientAlphaKey(0f, 1f) });
            fade.color = opacity;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            emitter.SetActive(true);
            particles.Play();
            return particles;
        }
    }
}
