using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Dust wake (2026-10-27): an aircraft fast and very low over land throws a rolling dust cloud up behind it. The land twin
    /// of Weather's <c>SeaSpray</c>, with the same over-land test (radar altitude well under the height above the sea), so the
    /// two never both fire. One shared billboard system in Datum space on the shared smoke puff, at most
    /// <see cref="MaxParticles"/> alive, emitted by hand per aircraft; no collisions, lights or shadows.
    /// ponytail: one sandy tint everywhere; sample the terrain splat if snow or grass should throw different colours.
    /// </summary>
    internal sealed class DustWake
    {
        private const int MaxParticles = 900;
        private const float Reach = 6000f, RatePerSecond = 260f, MinSpeed = 60f;
        private static readonly Color Dust = new Color(.6f, .53f, .43f);

        private GameObject root;
        private ParticleSystem system;
        private readonly Dictionary<int, float> carry = new Dictionary<int, float>();
        private uint noise = 0x2545F491u;

        internal int Particles => system != null ? system.particleCount : 0;

        internal void Tick(Vector3 eye, bool enabled, float light, float dt)
        {
            List<Aircraft> aircraft = UnitRegistry.allAircraft;
            if (enabled && aircraft != null)
            {
                float seaY = Datum.LocalSeaY;
                for (int i = 0; i < aircraft.Count; i++)
                {
                    Aircraft plane = aircraft[i];
                    if (plane == null || plane.rb == null) continue;
                    float speed = plane.speed, agl = plane.radarAlt, radius = plane.maxRadius;
                    if (speed < MinSpeed || agl > 10f + radius * 2f) continue;
                    Vector3 at = plane.transform.position;
                    if (agl > at.y - seaY - 3f) continue; // over the sea: SeaSpray's job
                    if ((at - eye).sqrMagnitude > Reach * Reach) continue;
                    float wake = Mathf.Clamp01((speed - MinSpeed) / 120f) * Mathf.Clamp01(1f - agl / (10f + radius * 2f));
                    if (wake > .02f) Emit(plane, at, agl, radius, wake, light, dt);
                }
            }
            if (root != null && root.activeSelf && system.particleCount == 0 && !enabled) root.SetActive(false);
        }

        private void Emit(Aircraft plane, Vector3 at, float agl, float radius, float wake, float light, float dt)
        {
            if (!Ensure()) return;
            if (!root.activeSelf) root.SetActive(true);
            int id = plane.GetInstanceID();
            carry.TryGetValue(id, out float owed);
            owed += RatePerSecond * wake * dt;
            int count = Mathf.Min((int)owed, 24);
            carry[id] = owed - (int)owed;
            if (count <= 0) return;
            Vector3 velocity = plane.rb.velocity;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 right = Vector3.Cross(Vector3.up, flat.sqrMagnitude > 1f ? flat.normalized : plane.transform.forward);
            float groundY = at.y - agl;
            Color tint = Dust * light;
            Transform datum = Datum.origin;
            for (int n = 0; n < count; n++)
            {
                // Lifted just behind the aircraft across its span, curling outward and upward, then hanging as it spreads.
                float side = Next(-1f, 1f);
                Vector3 point = new Vector3(at.x - flat.x * Next(.05f, .25f), groundY + .5f, at.z - flat.z * Next(.05f, .25f)) + right * side * radius * .8f;
                Vector3 kick = Vector3.up * Next(2f, 7f) * (.5f + wake) + flat * Next(.05f, .18f) + right * side * Next(3f, 9f);
                tint.a = Next(.35f, .6f) * wake;
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = datum.InverseTransformPoint(point),
                    velocity = datum.InverseTransformDirection(kick),
                    startSize = Next(5f, 11f) * (.7f + .6f * wake),
                    startLifetime = Next(3f, 6f),
                    rotation = Next(0f, 360f),
                    startColor = tint,
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        private float Next(float min, float max)
        {
            noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
            return min + (max - min) * (noise & 0xFFFFFF) / 16777216f;
        }

        private bool Ensure()
        {
            if (system != null) return true;
            Material puff = SmokeFx.Puff;
            if (puff == null) return false;
            root = new GameObject("BoscaliDustWake") { hideFlags = HideFlags.DontSave };
            system = root.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = MaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.startSpeed = 0f;
            main.gravityModifier = -.02f; // warm dust lifts a little as it slows
            var emission = system.emission;
            emission.rateOverTime = 0f;
            var shape = system.shape;
            shape.enabled = false;
            // Gentle drag only: SeaSpray found a strong damp kills the kick within a few frames.
            var drag = system.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = .3f;
            drag.multiplyDragByParticleSize = false;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .5f, 1f, 3f));
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .08f), new GradientAlphaKey(.6f, .5f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = puff;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = .6f;
            system.Play();
            return true;
        }

        internal void Reset()
        {
            carry.Clear();
            if (root != null) Object.Destroy(root);
            root = null;
            system = null;
        }
    }
}
