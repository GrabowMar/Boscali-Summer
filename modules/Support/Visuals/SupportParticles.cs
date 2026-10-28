using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Support.Visuals
{
    // Shared by the three support bursts. Local presentation only; no damage or sensors.
    internal static class SupportParticles
    {
        private static Material glow, smoke, lightning;

        public static Material Lightning
        {
            get
            {
                if (lightning != null) return lightning;
                lightning = new Material(Material(true)) { name = "Support.Lightning" };
                if (lightning.HasProperty("_BaseMap")) lightning.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (lightning.HasProperty("_MainTex")) lightning.SetTexture("_MainTex", Texture2D.whiteTexture);
                return lightning;
            }
        }

        public static Material Material(bool additive)
        {
            if (additive && glow != null) return glow;
            if (!additive && smoke != null) return smoke;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = additive ? "Support.Glow" : "Support.Dust" };
            Configure(material, additive);
            if (additive) glow = material; else smoke = material;
            return material;
        }

        public static void Configure(Material material, bool additive)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            Set("_Surface", 1); Set("_Blend", additive ? 2 : 0);
            Set("_Mode", additive ? 4 : 2);
            Set("_SrcBlend", (float)BlendMode.SrcAlpha);
            Set("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            Set("_ZWrite", 0); Set("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", ProceduralVfxTextures.SoftPuffTexture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", ProceduralVfxTextures.SoftPuffTexture);
            void Set(string key, float value) { if (material.HasProperty(key)) material.SetFloat(key, value); }
        }

        public static ParticleSystem Layer(Transform parent, string name, bool additive,
            int budget, float lifetime, float size, Color color, float gravity = 0f,
            Gradient fade = null)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = budget; main.startLifetime = lifetime;
            main.startSpeed = 0; main.startSize = size; main.startColor = color;
            main.gravityModifier = gravity;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var rotation = ps.rotationOverLifetime; rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var growth = ps.sizeOverLifetime; growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.Linear(0, 0.45f, 1, additive ? 1.1f : 2.8f));
            var fadeOverLifetime = ps.colorOverLifetime; fadeOverLifetime.enabled = true;
            if (fade != null)
            {
                fadeOverLifetime.color = fade;
            }
            else
            {
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.06f),
                        new GradientAlphaKey(0.7f, 0.35f), new GradientAlphaKey(0, 1) });
                fadeOverLifetime.color = gradient;
            }
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Material(additive);
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            go.SetActive(true); ps.Play();
            return ps;
        }

        public static void Ring(ParticleSystem ps, int count, float radius, float speed, float lift)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2 / count;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                ps.Emit(new ParticleSystem.EmitParams {
                    position = direction * radius + Vector3.up * Random.Range(0f, 5f),
                    velocity = direction * speed * Random.Range(0.92f, 1.08f) + Vector3.up * lift,
                    rotation = Random.Range(0f, 360f)
                }, 1);
            }
        }

        /// <summary>A finished pooled effect: out of the active list, switched off, onto the free stack.</summary>
        public static void Recycle<T>(List<T> active, Stack<T> free, T effect) where T : Component
        {
            active.Remove(effect);
            effect.gameObject.SetActive(false);
            free.Push(effect);
        }

        /// <summary>Scene teardown of a pooled effect: every active and free object destroyed, both collections emptied.</summary>
        public static void DestroyPool<T>(List<T> active, Stack<T> free) where T : Component
        {
            for (int i = 0; i < active.Count; i++) if (active[i] != null) Object.Destroy(active[i].gameObject);
            active.Clear();
            while (free.Count > 0) { T e = free.Pop(); if (e != null) Object.Destroy(e.gameObject); }
        }

        public static void Reset()
        {
            Object.Destroy(glow); Object.Destroy(smoke); Object.Destroy(lightning);
            glow = smoke = lightning = null;
        }
    }
}
