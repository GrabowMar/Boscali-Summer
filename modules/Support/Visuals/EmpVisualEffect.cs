using System.Collections.Generic;
using BoscaliSummer.Features.Support.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Visuals
{
    // Nuclear-pulse phases: a prompt coherent E1 spike, an intermediate lightning-like E2,
    // and a slow geomagnetic E3 heave that holds for the jam window. Gameplay jamming
    // belongs to EmpAction.
    internal sealed class EmpVisualEffect : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const float E1Window = 0.4f;
        private const float FootprintSnap = 0.12f;
        private const float E2Window = 3.2f;
        private const int RingPoints = 72;
        private static AudioClip avionicsClip;
        private static AudioClip thunderClip;
        private static Gradient auroraFade;
        private static readonly List<EmpVisualEffect> active = new List<EmpVisualEffect>(4);
        private readonly LineRenderer[] arcs = new LineRenderer[12];
        private readonly LineRenderer[] heave = new LineRenderer[3];
        private readonly Vector3[] arcPoints = new Vector3[25];
        private readonly Vector3[] ringPoints = new Vector3[RingPoints];
        private float born, radius, lifetime, nextArc, nextCockpit;
        private float groundOffset;
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
            radius = Mathf.Clamp(range, 1000f, SupportEffectPolicy.MaxEmpRadius);
            lifetime = SupportEffectPolicy.EmpDuration + 4f;
            born = Time.time;
            // Show the affected footprint at ground level while the source pulse stays in the sky.
            float groundY = Datum.LocalSeaY + 120f;
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 50000f, PhysicsLayers.StaticsMask))
                groundY = hit.point.y + 120f;
            groundOffset = groundY - transform.position.y;
            var prompt = SupportParticles.Layer(transform, "Prompt ionization", true, 48, 1.6f, 150f,
                new Color(3f, 4.4f, 6f));
            prompt.Emit(24);
            var intermediate = SupportParticles.Layer(transform, "Scattered-gamma glow", true, 160, 4.5f, 95f,
                new Color(0.15f, 0.85f, 1.9f, 0.5f));
            SupportParticles.Ring(intermediate, 160, 80f, 150f, 6f);
            var footprint = SupportParticles.Layer(transform, "Ionized footprint", true, 320, 5.5f, 120f,
                new Color(0.45f, 1.4f, 2.4f, 0.6f));
            footprint.transform.localPosition = Vector3.up * groundOffset;
            SupportParticles.Ring(footprint, 320, radius * 0.97f, 90f, 0f);
            var aurora = SupportParticles.Layer(transform, "Geomagnetic heave", true, 240,
                SupportEffectPolicy.EmpDuration, 200f, new Color(0.4f, 1.3f, 1.5f, 0.35f), 0f, AuroraFade());
            SupportParticles.Ring(aurora, 240, radius * 0.94f, 30f, 0f);
            for (int i = 0; i < arcs.Length; i++)
            {
                var go = new GameObject("Lightning branch");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = SupportParticles.Lightning;
                line.useWorldSpace = false; line.positionCount = arcPoints.Length;
                line.startWidth = 12f; line.endWidth = 2f;
                arcs[i] = line;
            }
            for (int i = 0; i < heave.Length; i++)
            {
                var go = new GameObject("Ionization wavefront");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = SupportParticles.Lightning;
                line.useWorldSpace = false; line.positionCount = RingPoints;
                line.loop = true; line.startWidth = 22f; line.endWidth = 22f;
                heave[i] = line;
            }
            heave[0].transform.localPosition = Vector3.up * groundOffset;
            flash = gameObject.AddComponent<Light>();
            flash.color = new Color(0.72f, 0.86f, 1f);
            flash.range = 45000f; flash.intensity = 90f; flash.shadows = LightShadows.None;
            EnsureAudio();
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = avionicsClip; source.spatialBlend = 1f;
            source.minDistance = 800f; source.maxDistance = 40000f;
            source.volume = 0.85f; source.Play();
            var thunder = gameObject.AddComponent<AudioSource>();
            thunder.clip = thunderClip; thunder.spatialBlend = 1f;
            thunder.minDistance = 2000f; thunder.maxDistance = 60000f;
            thunder.volume = 0.9f; thunder.PlayDelayed(ThunderDelay());
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
            flash.intensity = 90f * Mathf.Exp(-age * 7f)
                + (age > E1Window ? 9f * Mathf.Exp(-(age - E1Window) * 1.1f) : 0f)
                + (age > E2Window ? 1.5f * Mathf.Clamp01(1f - (age - E2Window) / 10f) : 0f);
            flash.color = age > E2Window
                ? Color.Lerp(new Color(0.72f, 0.86f, 1f), new Color(0.45f, 1f, 0.8f),
                    Mathf.Clamp01((age - E2Window) / 6f))
                : new Color(0.72f, 0.86f, 1f);
            UpdateFootprint(age);
            UpdateArcs(age);
            UpdateHeave(age);
        }

        private void UpdateFootprint(float age)
        {
            LineRenderer ring = heave[0];
            if (age >= 6f)
            {
                ring.enabled = false;
                return;
            }
            float snap = Mathf.SmoothStep(0.02f, 1f, Mathf.Clamp01(age / FootprintSnap));
            float alpha = age <= FootprintSnap
                ? age / FootprintSnap
                : Mathf.Clamp01(1f - (age - FootprintSnap) / 5.5f);
            DrawRing(ring, radius * snap, 0f, 0f);
            ring.startColor = new Color(0.8f, 2f, 3.2f, alpha);
            ring.endColor = new Color(0.25f, 0.8f, 2f, alpha * 0.35f);
            ring.enabled = true;
        }

        private void UpdateArcs(float age)
        {
            if (age < E1Window || age > E2Window + 1.5f)
            {
                for (int i = 0; i < arcs.Length; i++) arcs[i].enabled = false;
                return;
            }
            if (age < nextArc) return;
            nextArc = age + 0.08f;
            float envelope = age < E1Window + 0.35f
                ? (age - E1Window) / 0.35f
                : Mathf.Clamp01(1f - (age - E1Window - 0.35f) / (E2Window - E1Window));
            for (int i = 0; i < arcs.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / arcs.Length;
                for (int j = 0; j < arcPoints.Length; j++)
                {
                    float t = j / (float)(arcPoints.Length - 1);
                    float a = angle + t * 0.45f;
                    float r = radius * (0.6f + t * 0.4f);
                    arcPoints[j] = new Vector3(Mathf.Cos(a) * r,
                        Mathf.Sin(t * 6f + i + age * 3f) * 140f, Mathf.Sin(a) * r)
                        + Random.insideUnitSphere * (j == 0 ? 0f : 34f);
                }
                arcs[i].SetPositions(arcPoints);
                arcs[i].startColor = new Color(1.3f, 1.9f, 2.3f, envelope);
                arcs[i].endColor = new Color(0.15f, 0.4f, 1f, 0f);
                arcs[i].enabled = envelope > 0.02f && Random.value > 0.4f;
            }
        }

        private void UpdateHeave(float age)
        {
            for (int i = 1; i < heave.Length; i++)
            {
                LineRenderer ring = heave[i];
                float start = E2Window + (i - 1) * 7f;
                float elapsed = age - start;
                float window = SupportEffectPolicy.EmpDuration - start;
                if (elapsed <= 0f || elapsed >= window)
                {
                    ring.enabled = false;
                    continue;
                }
                float pulse = Mathf.Repeat(elapsed, 12f) / 12f;
                float waveRadius = radius * Mathf.SmoothStep(0.12f, 1f, pulse);
                float alpha = Mathf.Sin(pulse * Mathf.PI) * 0.7f * Mathf.Clamp01(1f - elapsed / window);
                DrawRing(ring, waveRadius, 90f, 1f);
                ring.startColor = new Color(0.35f, 1.6f, 1.25f, alpha);
                ring.endColor = new Color(0.1f, 0.5f, 0.9f, alpha * 0.25f);
                ring.enabled = true;
            }
        }

        private void DrawRing(LineRenderer line, float waveRadius, float undulation, float equatorBias)
        {
            float phase = Time.time * 15f;
            for (int j = 0; j < RingPoints; j++)
            {
                float angle = j * Mathf.PI * 2f / RingPoints;
                float ripple = 1f + Mathf.Sin(angle * 7f + phase) * 0.03f * equatorBias *
                    (1f + Mathf.Max(0f, Mathf.Sin(angle)));
                ringPoints[j] = new Vector3(Mathf.Cos(angle) * waveRadius * ripple,
                    Mathf.Sin(angle * 11f - phase) * undulation, Mathf.Sin(angle) * waveRadius * ripple);
            }
            line.SetPositions(ringPoints);
        }

        public static void Reset()
        {
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i] != null) Destroy(active[i].gameObject);
            active.Clear();
        }

        private void OnDestroy() => active.Remove(this);

        private float ThunderDelay()
        {
            var csm = SceneSingleton<CameraStateManager>.i;
            Camera cam = csm != null ? csm.mainCamera : Camera.main;
            if (cam == null) return 2.5f;
            return Mathf.Clamp(Vector3.Distance(cam.transform.position, transform.position) / 5000f, 1.2f, 6f);
        }

        private static Gradient AuroraFade()
        {
            if (auroraFade != null) return auroraFade;
            auroraFade = new Gradient();
            auroraFade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0f, 0.07f),
                    new GradientAlphaKey(0.85f, 0.16f), new GradientAlphaKey(0.6f, 0.55f),
                    new GradientAlphaKey(0f, 1f) });
            return auroraFade;
        }

        private static void EnsureAudio()
        {
            if (avionicsClip == null) avionicsClip = BuildAvionicsClip();
            if (thunderClip == null) thunderClip = BuildThunderClip();
        }

        private static AudioClip BuildAvionicsClip()
        {
            int length = (int)(SampleRate * 3.2f);
            float[] samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float prompt = (UnityEngine.Random.value * 2f - 1f) * Mathf.Exp(-t * 55f) * 0.95f;
                float ringFreq = Mathf.Lerp(3200f, 420f, Mathf.Clamp01(t / 0.3f));
                float receiver = Mathf.Sin(2f * Mathf.PI * ringFreq * t) * Mathf.Exp(-t * 9f) * 0.35f;
                float arcEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 2.6f)), 2.2f);
                float arc = (UnityEngine.Random.value * 2f - 1f) * arcEnvelope * 0.5f;
                float pop = UnityEngine.Random.value > 0.988f
                    ? (UnityEngine.Random.value * 2f - 1f) * 0.8f : 0f;
                samples[i] = Mathf.Clamp(prompt + receiver + arc + pop, -1f, 1f);
            }
            var clip = AudioClip.Create("EmpAvionicsSnap", length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip BuildThunderClip()
        {
            int length = (int)(SampleRate * 7f);
            float[] samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float bassFreq = Mathf.Lerp(32f, 17f, t / 7f);
                float envelope = Mathf.Pow(Mathf.Clamp01(1f - t / 6.5f), 1.4f);
                float bass = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * envelope * 0.85f;
                float sub = Mathf.Sin(2f * Mathf.PI * (bassFreq * 0.5f) * t) * envelope * 0.5f;
                float roll = (UnityEngine.Random.value * 2f - 1f) * envelope * 0.35f;
                float echo = (Mathf.Sin(2f * Mathf.PI * 46f * t) + Mathf.Sin(2f * Mathf.PI * 61f * t) * 0.5f)
                    * Mathf.Pow(Mathf.Clamp01(1f - t / 6.8f), 1.8f) * 0.3f;
                samples[i] = Mathf.Clamp(bass + sub + roll + echo, -1f, 1f);
            }
            var clip = AudioClip.Create("EmpThunder", length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
