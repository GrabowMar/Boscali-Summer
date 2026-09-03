using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// A bounded, pooled collapse accent. It deliberately uses particle-rendered dust
    /// instead of Rigidbody debris, so a city-wide destruction wave cannot create a
    /// physics or allocation storm.
    /// </summary>
    internal sealed class CollapseBurstPool
    {
        private sealed class Visual
        {
            public GameObject Root;
            public ParticleSystem Dust;
            public ParticleSystem DebrisDust;
            public float Expires;
            public bool Active;
        }

        private readonly List<Visual> visuals = new List<Visual>(4);
        private Material dustMaterial;
        private bool searched;

        public void Emit(GlobalPosition position, Vector2 halfExtents)
        {
            if (GameManager.IsHeadless) return;
            Visual visual = null;
            for (int i = 0; i < visuals.Count; i++)
                if (!visuals[i].Active) { visual = visuals[i]; break; }
            if (visual == null)
            {
                if (visuals.Count >= Plugin.Settings.FireAndDestruction.MaximumCollapseBursts) return;
                visual = Create();
                if (visual == null) return;
                visuals.Add(visual);
            }

            visual.Active = true;
            visual.Expires = Time.timeSinceLevelLoad + 6f;
            visual.Root.transform.position = position.ToLocalPosition() + Vector3.up * 0.45f;
            visual.Root.SetActive(true);
            ConfigureShape(visual.Dust, halfExtents, 0.9f);
            ConfigureShape(visual.DebrisDust, halfExtents, 0.55f);
            visual.Dust.Clear(true);
            visual.DebrisDust.Clear(true);
            visual.Dust.Play(true);
            visual.DebrisDust.Play(true);
        }

        public void Update(float now)
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                Visual visual = visuals[i];
                if (!visual.Active || now < visual.Expires) continue;
                visual.Active = false;
                visual.Dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                visual.DebrisDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                visual.Root.SetActive(false);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < visuals.Count; i++)
                if (visuals[i].Root != null) Object.Destroy(visuals[i].Root);
            visuals.Clear();
            dustMaterial = null;
            searched = false;
        }

        private Visual Create()
        {
            FindDustMaterial();
            if (dustMaterial == null) return null;
            var root = new GameObject("BoscaliSummer.CollapseBurst");
            root.transform.SetParent(Datum.origin, false);
            ParticleSystem dust = CreateLayer(root.transform, "CollapseDust", 42, 3.5f, 6.5f,
                7f, 17f, new Color(0.30f, 0.285f, 0.255f, 0.62f), -0.04f, 0f);
            ParticleSystem debrisDust = CreateLayer(root.transform, "EjectedDust", 24, 1.8f, 3.8f,
                2.8f, 7f, new Color(0.18f, 0.17f, 0.155f, 0.72f), 0.32f, 0.18f);
            root.SetActive(false);
            return new Visual { Root = root, Dust = dust, DebrisDust = debrisDust };
        }

        private ParticleSystem CreateLayer(
            Transform parent, string name, short count, float life, float speed,
            float sizeMin, float sizeMax, Color color, float gravity, float burstDelay)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            ParticleSystem system = gameObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.72f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.55f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.maxParticles = count + 8;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(burstDelay, count) });
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.randomDirectionAmount = 0.72f;

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = 1.25f;
            noise.frequency = 0.24f;

            ParticleSystem.ColorOverLifetimeModule lifetimeColor = system.colorOverLifetime;
            lifetimeColor.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color * 0.72f, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(color.a, 0.08f),
                    new GradientAlphaKey(color.a * 0.72f, 0.58f), new GradientAlphaKey(0f, 1f) });
            lifetimeColor.color = gradient;

            ParticleSystemRenderer renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = dustMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        private static void ConfigureShape(ParticleSystem system, Vector2 halfExtents, float scale)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.scale = new Vector3(
                Mathf.Clamp(halfExtents.x * 1.5f * scale, 5f, 42f),
                Mathf.Clamp(Mathf.Min(halfExtents.x, halfExtents.y) * 0.18f, 1.2f, 5f),
                Mathf.Clamp(halfExtents.y * 1.5f * scale, 5f, 42f));
        }

        private void FindDustMaterial()
        {
            if (searched) return;
            searched = true;
            int bestScore = int.MinValue;
            DamageParticles[] effects = Resources.FindObjectsOfTypeAll<DamageParticles>();
            for (int e = 0; e < effects.Length; e++)
            {
                if (effects[e] == null) continue;
                ParticleSystem[] systems = effects[e].GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                {
                    ParticleSystemRenderer renderer = systems[i].GetComponent<ParticleSystemRenderer>();
                    if (renderer == null || renderer.sharedMaterial == null) continue;
                    string descriptor = (effects[e].name + "/" + systems[i].name + "/" +
                        renderer.sharedMaterial.name).ToLowerInvariant();
                    int score = descriptor.Contains("dust") ? 300 : descriptor.Contains("smoke") ? 90 : 0;
                    if (descriptor.Contains("collapse") || descriptor.Contains("debris") ||
                        descriptor.Contains("impact")) score += 80;
                    if (descriptor.Contains("tire") || descriptor.Contains("trail") ||
                        descriptor.Contains("engine")) score -= 240;
                    if (score > bestScore) { bestScore = score; dustMaterial = renderer.sharedMaterial; }
                }
            }
        }
    }
}
