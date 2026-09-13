using System.Collections.Generic;
using BoscaliSummer.Features.Support.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Visuals
{
    // Particle corona and branching electrical front. Gameplay jamming belongs to EmpAction.
    internal sealed class EmpVisualEffect : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private static AudioClip empAudioClip;
        private static readonly List<EmpVisualEffect> active = new List<EmpVisualEffect>(4);
        private readonly LineRenderer[] arcs = new LineRenderer[12];
        private readonly LineRenderer[] wavefronts = new LineRenderer[3];
        private readonly Vector3[] points = new Vector3[25];
        private readonly Vector3[] ringPoints = new Vector3[48];
        private float born, radius, lifetime, nextArc, nextCockpit;
        private Light flash;

        public static void Trigger(Vector3 point, float radiusMeters)
        {
            if (GameManager.IsHeadless) return;
            if (active.Count >= 4) return;
            var go = new GameObject("BoscaliSummer.EMP");
            go.transform.SetParent(Datum.origin, false);
            go.transform.position = point;
            var effect = go.AddComponent<EmpVisualEffect>();
            active.Add(effect);
            effect.Initialize(radiusMeters);
        }

        private void Initialize(float range)
        {
            radius = Mathf.Clamp(range, 1000f, 60000f);
            lifetime = SupportEffectPolicy.EmpDuration + 4f;
            born = Time.time;
            var core = SupportParticles.Layer(transform, "Ionization flash", true, 32, 4f, 240f, new Color(2f, 3f, 4f));
            core.Emit(24);
            var corona = SupportParticles.Layer(transform, "Turbulent blue corona", true, 160, 5.5f, 95f, new Color(0.12f, 0.7f, 1.8f, 0.5f));
            SupportParticles.Ring(corona, 160, 30f, 100f, 8f);
            var front = SupportParticles.Layer(transform, "Electrical pressure front", true, 384, 5.5f, 120f, new Color(0.4f, 1.2f, 2f, 0.6f));
            SupportParticles.Ring(front, 384, 20f, radius / 5.5f, 0f);
            var vapor = SupportParticles.Layer(transform, "Atmospheric ripple", false, 192, 4.8f, 180f, new Color(0.58f, 0.78f, 0.95f, 0.25f));
            SupportParticles.Ring(vapor, 192, 20f, radius / 6f, -6f);
            for (int i = 0; i < arcs.Length; i++)
            {
                var go = new GameObject("Lightning branch");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = SupportParticles.Lightning;
                line.useWorldSpace = false; line.positionCount = points.Length;
                line.startWidth = 12f; line.endWidth = 2f;
                arcs[i] = line;
            }
            for (int i = 0; i < wavefronts.Length; i++)
            {
                var go = new GameObject("Ionization wavefront");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = SupportParticles.Lightning;
                line.useWorldSpace = false; line.positionCount = ringPoints.Length;
                line.loop = true; line.startWidth = 22f; line.endWidth = 22f;
                wavefronts[i] = line;
            }
            flash = gameObject.AddComponent<Light>();
            flash.color = new Color(0.35f, 0.7f, 1f);
            flash.range = 8000f; flash.intensity = 30f; flash.shadows = LightShadows.None;
            EnsureAudio();
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = empAudioClip; source.spatialBlend = 1f;
            source.minDistance = 800f; source.maxDistance = 30000f;
            source.volume = 0.8f; source.Play();
            Destroy(gameObject, lifetime);
        }

        private void Update()
        {
            float age = Time.time - born;
            if (age >= nextCockpit)
            {
                nextCockpit = age + 0.5f;
                CockpitEmpDisruption.CheckLocalDisruption(transform.position, radius);
            }
            float tail = Mathf.Clamp01(1f - age / lifetime);
            flash.intensity = 30f * Mathf.Exp(-age * 5f) + 2.5f * tail *
                (0.5f + Mathf.Sin(age * 9f) * 0.5f);
            if (age < nextArc) return;
            nextArc = age + 0.08f; // 12.5 Hz geometry; fixed arrays, no per-frame mesh allocations.
            float front = Mathf.Min(1f, age / 5.5f) * radius;
            float alpha = Mathf.Clamp01(1f - age / 5.5f);
            for (int i = 0; i < arcs.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / arcs.Length;
                for (int j = 0; j < points.Length; j++)
                {
                    float t = j / (float)(points.Length - 1);
                    float a = angle + t * 0.45f;
                    float r = front * (0.65f + t * 0.35f);
                    points[j] = new Vector3(Mathf.Cos(a) * r,
                        Mathf.Sin(t * 6f + i) * 80f, Mathf.Sin(a) * r)
                        + Random.insideUnitSphere * (j == 0 ? 0f : 28f);
                }
                arcs[i].SetPositions(points);
                arcs[i].startColor = new Color(1.3f, 1.8f, 2f, alpha);
                arcs[i].endColor = new Color(0.15f, 0.4f, 1f, 0);
                arcs[i].enabled = alpha > 0f && Random.value > 0.25f;
            }
            for (int i = 0; i < wavefronts.Length; i++)
            {
                float staggeredAge = age - i * 1.8f;
                LineRenderer wavefront = wavefronts[i];
                if (staggeredAge <= 0f || staggeredAge >= SupportEffectPolicy.EmpDuration)
                {
                    wavefront.enabled = false;
                    continue;
                }
                float pulse = Mathf.Repeat(staggeredAge, 8f) / 8f;

                float waveRadius = radius * Mathf.SmoothStep(0.03f, 1f, pulse);
                float waveAlpha = Mathf.Sin(pulse * Mathf.PI) * 0.8f;
                for (int j = 0; j < ringPoints.Length; j++)
                {
                    float angle = j * Mathf.PI * 2f / ringPoints.Length;
                    float ripple = 1f + Mathf.Sin(angle * 7f + age * 15f + i) * 0.035f;
                    ringPoints[j] = new Vector3(Mathf.Cos(angle) * waveRadius * ripple,
                        Mathf.Sin(angle * 11f - age * 18f) * 90f, Mathf.Sin(angle) * waveRadius * ripple);
                }
                wavefront.SetPositions(ringPoints);
                wavefront.startColor = new Color(0.35f, 1.5f, 2.5f, waveAlpha);
                wavefront.endColor = new Color(0.1f, 0.45f, 1.4f, waveAlpha * 0.25f);
                wavefront.enabled = true;
            }
        }

        public static void Reset()
        {
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i] != null) Destroy(active[i].gameObject);
            active.Clear();
        }

        private void OnDestroy() => active.Remove(this);

        private static void EnsureAudio()
        {
            if (empAudioClip != null) return;

            // Synthesize 5.5 seconds of high-fidelity cinematic EMP sound:
            // supersonic dielectric snap + massive 24Hz sub-bass surge + high-voltage arc crackle + descending inverter whine
            int length = (int)(SampleRate * 5.5f);
            float[] samples = new float[length];

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;

                // 1. Supersonic dielectric ionization snap (0.0s - 0.05s)
                float snapEnvelope = Mathf.Exp(-t * 40f);
                float snap = (UnityEngine.Random.value * 2f - 1f) * snapEnvelope * 0.95f;

                // 2. Sub-bass atmospheric pulse surge (34 Hz dropping to 18 Hz)
                float bassFreq = Mathf.Lerp(34f, 18f, t / 5.5f);
                float bassEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 5.2f)), 1.6f);
                float bass = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * bassEnvelope * 0.85f;
                float subBass = Mathf.Sin(2f * Mathf.PI * (bassFreq * 0.5f) * t) * bassEnvelope * 0.5f;

                // 3. High-voltage arc discharge & electrostatic sizzle
                float arcEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 2.2f)), 2.5f);
                float noise = (UnityEngine.Random.value * 2f - 1f) * arcEnvelope * 0.6f;

                // 4. Power-grid descending inverter whine (sweeping down from 400Hz to 60Hz)
                float whineFreq = Mathf.Lerp(400f, 60f, Mathf.Clamp01(t / 2.5f));
                float whine = Mathf.Sin(2f * Mathf.PI * whineFreq * t) * arcEnvelope * 0.4f;

                // 5. Cavernous thunder reverberation tail
                float echo = Mathf.Sin(2f * Mathf.PI * 48f * t) * Mathf.Pow(Mathf.Clamp01(1f - (t / 5.5f)), 2.0f) * 0.3f;

                samples[i] = Mathf.Clamp(snap + bass + subBass + noise + whine + echo, -1f, 1f);
            }

            empAudioClip = AudioClip.Create("EmpShockSound", length, 1, SampleRate, false);
            empAudioClip.SetData(samples, 0);
        }

    }
}
