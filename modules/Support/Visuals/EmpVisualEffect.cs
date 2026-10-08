using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// EMP: a high-altitude airburst seen from below. A star-bright flash that is a point of white light in the sky (sized to a constant angle, not a fixed size), a faint
    /// ionisation shell racing outwards from it, the sky flickering for a few seconds, and then the ground under the shell: as its faint front reaches each unit its lights
    /// and screens die in a spit of sparks and a puff of smoke. No dome, no bolts. One pooled object (two lights per effect plus six flicker lights, ~300 particles); the
    /// cockpit consequences stay in <see cref="CockpitEmpDisruption"/>. Gameplay jamming belongs to EmpAction.
    /// </summary>
    internal sealed class EmpVisualEffect : MonoBehaviour
    {
        private const int MaxVictims = 24, FlickerLights = 6;
        private const float ShellSeconds = 2.4f;
        private static readonly List<EmpVisualEffect> active = new List<EmpVisualEffect>(2);
        private static readonly Stack<EmpVisualEffect> free = new Stack<EmpVisualEffect>(2);
        private static AudioClip snap, rumble;

        private ParticleSystem star, shell, groundRing, airglow, sparks, puffs;
        private Light flash;
        private readonly Light[] flickers = new Light[FlickerLights];
        private readonly float[] flickerUntil = new float[FlickerLights];
        private AudioSource snapSource, rumbleSource;
        private readonly Vector3[] victims = new Vector3[MaxVictims];
        private readonly float[] victimAt = new float[MaxVictims];
        private readonly bool[] victimDone = new bool[MaxVictims];
        private int victimCount, flickerCursor;
        private Vector3 point, ground;
        private float born, radius, nextCockpit, nextSky;

        public static void Trigger(Vector3 point, float radiusMeters)
        {
            if (GameManager.IsHeadless || active.Count >= 2) return;
            EmpVisualEffect e;
            if (free.Count > 0) e = free.Pop();
            else e = new GameObject("BoscaliSummer.EMP").AddComponent<EmpVisualEffect>();
            e.transform.SetParent(Datum.origin, false);
            e.gameObject.SetActive(true);
            active.Add(e);
            e.Begin(point, Mathf.Clamp(radiusMeters, 1000f, SupportEffectPolicy.MaxEmpRadius));
        }

        public static void Reset()
        {
            for (int i = 0; i < active.Count; i++) if (active[i] != null) Destroy(active[i].gameObject);
            active.Clear();
            while (free.Count > 0) { var e = free.Pop(); if (e != null) Destroy(e.gameObject); }
        }

        private void Build()
        {
            if (star != null) return;
            star = CineFx.Layer(transform, "Burst star", CineFx.Glow, 12);
            airglow = CineFx.Layer(transform, "Ionised air", CineFx.Glow, 8);
            CineFx.Tint(airglow, CineFx.Fade(0.05f, 0.3f, 0.5f));
            shell = CineFx.Layer(transform, "Ionisation shell", CineFx.RingAdd, 4, ParticleSystemRenderMode.Billboard);
            groundRing = CineFx.Layer(transform, "Ground front", CineFx.RingAdd, 4, ParticleSystemRenderMode.HorizontalBillboard);
            foreach (var ps in new[] { shell, groundRing })
            {
                var g = ps.sizeOverLifetime; g.enabled = true;
                g.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.02f), new Keyframe(0.12f, 0.35f), new Keyframe(1f, 1f)));
                CineFx.Tint(ps, CineFx.Fade(0.04f, 0.3f));
            }
            sparks = CineFx.Layer(transform, "Dying electronics", CineFx.Glow, 200, ParticleSystemRenderMode.Stretch, false, 2f, 0.05f);
            var gs = sparks.main; gs.gravityModifier = 1.2f;
            CineFx.Tint(sparks, CineFx.Ramp(new Color(1f, 1f, 0.9f, 1f), 0f, new Color(1f, 0.7f, 0.3f, 1f), 0.3f, new Color(1f, 0.4f, 0.1f, 0.6f), 0.7f, new Color(0.3f, 0.1f, 0.05f, 0f), 1f));
            puffs = CineFx.Layer(transform, "Burnt-out smoke", CineFx.Smoke, 60, ParticleSystemRenderMode.Billboard, true);
            CineFx.Grow(puffs, 0.5f, 2.2f); CineFx.Spin(puffs, 20f); CineFx.Turbulence(puffs, 3f, 0.1f);
            CineFx.Tint(puffs, CineFx.Ramp(new Color(1f, 1f, 1f, 0f), 0f, new Color(0.3f, 0.3f, 0.3f, 0.55f), 0.1f, new Color(0.25f, 0.25f, 0.25f, 0.4f), 0.6f, new Color(0.3f, 0.3f, 0.3f, 0f), 1f));
            var fl = new GameObject("Flash light"); fl.transform.SetParent(transform, false);
            flash = fl.AddComponent<Light>();
            flash.type = LightType.Point; flash.color = new Color(0.82f, 0.9f, 1f); flash.range = 90000f; flash.shadows = LightShadows.None;
            for (int i = 0; i < FlickerLights; i++)
            {
                var go = new GameObject("Dying light"); go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point; l.range = 55f; l.color = new Color(1f, 0.8f, 0.55f); l.shadows = LightShadows.None; l.enabled = false;
                flickers[i] = l;
            }
            if (snap == null) snap = CineFx.Clip("EmpSnap", 1.2f, t => CineFx.Noise() * Mathf.Exp(-t * 60f) * 0.95f + Mathf.Sin(6.2832f * Mathf.Lerp(2400f, 300f, Mathf.Clamp01(t / 0.25f)) * t) * Mathf.Exp(-t * 14f) * 0.35f);
            if (rumble == null)
            {
                float lp = 0f;
                rumble = CineFx.Clip("EmpRumble", 7f, t =>
                {
                    lp += (CineFx.Noise() - lp) * 0.03f;
                    return lp * 3.4f * Mathf.Clamp01(t * 3f) * Mathf.Exp(-t * 0.6f) + Mathf.Sin(6.2832f * Mathf.Lerp(40f, 20f, t / 7f) * t) * Mathf.Exp(-t * 0.5f) * 0.5f;
                });
            }
            snapSource = CineFx.Speaker(gameObject, snap, 1500f, 70000f, 0.9f);
            rumbleSource = CineFx.Speaker(gameObject, rumble, 2500f, 90000f, 0.8f);
        }

        private void Begin(Vector3 burst, float range)
        {
            Build();
            point = burst; radius = range; born = Time.time; nextCockpit = 0f; nextSky = 0.7f; flickerCursor = 0;
            ground = point;
            if (Physics.Raycast(point, Vector3.down, out RaycastHit hit, 60000f, PhysicsLayers.StaticsMask)) ground = hit.point; else ground.y = Datum.LocalSeaY;
            foreach (var ps in new[] { star, airglow, shell, groundRing, sparks, puffs }) { ps.Clear(); ps.Play(); }
            for (int i = 0; i < FlickerLights; i++) { flickers[i].enabled = false; flickerUntil[i] = 0f; }
            transform.localPosition = Vector3.zero;

            float dist = Mathf.Max(800f, CineFx.CameraDistance(point));
            // the flash is sized by what the eye sees: a hard white star of about 4 degrees with a wide soft bloom
            star.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(point), startSize = dist * 0.07f, startLifetime = 0.5f, startColor = new Color(1f, 1f, 1f, 1f) }, 1);
            star.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(point), startSize = dist * 0.35f, startLifetime = 0.9f, startColor = new Color(0.75f, 0.85f, 1f, 0.55f) }, 1);
            star.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(point), startSize = dist * 0.9f, startLifetime = 1.6f, startColor = new Color(0.55f, 0.7f, 1f, 0.2f) }, 1);
            airglow.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(point), startSize = dist * 0.8f, startLifetime = 5f, startColor = new Color(0.5f, 0.65f, 1f, 0.5f) }, 1);
            shell.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(point), startSize = dist * 1.7f, startLifetime = ShellSeconds + 1.2f, startColor = new Color(0.75f, 0.88f, 1f, 0.5f) }, 1);
            groundRing.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(ground + Vector3.up * 20f), startSize = radius * 2f, startLifetime = ShellSeconds + 0.8f, startColor = new Color(0.8f, 0.9f, 1f, 0.3f) }, 1);
            flash.transform.position = point;
            flash.enabled = true;

            // the units the front reaches: ground vehicles, ships and buildings in the footprint, each dying when the front arrives
            victimCount = 0;
            List<Unit> units = UnitRegistry.allUnits;
            if (units != null)
            {
                float sq = radius * radius;
                for (int i = 0; i < units.Count && victimCount < MaxVictims; i++)
                {
                    Unit u = units[i];
                    if (u == null || u.disabled || u is Aircraft || u is Missile) continue;
                    Vector3 d = u.transform.position - ground;
                    float dd = d.x * d.x + d.z * d.z;
                    if (dd > sq) continue;
                    victims[victimCount] = u.transform.position;
                    victimAt[victimCount] = 0.35f + Mathf.Sqrt(dd) / radius * ShellSeconds + Random.Range(0f, 0.35f);
                    victimDone[victimCount++] = false;
                }
            }
            snapSource.PlayDelayed(Mathf.Clamp(dist / 4000f, 0f, 3f));
            rumbleSource.PlayDelayed(Mathf.Clamp(dist / 1800f, 0.5f, 7f));
            CockpitEmpDisruption.CheckLocalDisruption(point, radius);
        }

        private void Update()
        {
            float age = Time.time - born;
            if (age >= SupportEffectPolicy.EmpDuration + 4f)
            {
                active.Remove(this);
                gameObject.SetActive(false);
                free.Push(this);
                return;
            }
            if (age >= nextCockpit)
            {
                nextCockpit = age + 0.5f;
                CockpitEmpDisruption.CheckLocalDisruption(point, radius);
            }
            // the flash: a hard peak, then an uneven flicker as the sky recovers
            float f = 90f * Mathf.Exp(-age * 9f);
            if (age > 0.7f && age < 5f) f += Mathf.Max(0f, Mathf.Sin(age * 31f) * Mathf.Sin(age * 17f)) * 10f * (1f - (age - 0.7f) / 4.3f) * (Random.value > 0.4f ? 1f : 0f);
            flash.intensity = f;
            if (age > 5f) flash.enabled = false;

            for (int i = 0; i < victimCount; i++)
            {
                if (victimDone[i] || age < victimAt[i]) continue;
                victimDone[i] = true;
                Kill(victims[i], age);
            }
            for (int i = 0; i < FlickerLights; i++)
            {
                if (!flickers[i].enabled) continue;
                if (age > flickerUntil[i]) flickers[i].enabled = false;
                else flickers[i].intensity = Random.value > 0.35f ? Random.Range(2f, 6f) : 0f;
            }
        }

        private void Kill(Vector3 at, float age)
        {
            Vector3 p = at + Vector3.up * 3f;
            for (int i = 0; i < 12; i++)
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = CineFx.ToSim(p), velocity = Random.onUnitSphere * Random.Range(3f, 14f) + Vector3.up * Random.Range(4f, 10f),
                    startSize = Random.Range(0.3f, 0.9f), startLifetime = Random.Range(0.5f, 1.4f)
                }, 1);
            puffs.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(p), velocity = Vector3.up * 2.5f + CineFx.Wind() * 0.3f, startSize = 5f, startLifetime = Random.Range(5f, 8f), rotation = Random.Range(0f, 360f) }, 1);
            int idx = flickerCursor++ % FlickerLights;
            Light l = flickers[idx];
            l.transform.position = p + Vector3.up * 2f;
            l.enabled = true; flickerUntil[idx] = age + Random.Range(0.5f, 1.1f);
        }

        private void OnDestroy() => active.Remove(this);
    }
}
