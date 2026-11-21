using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// A purely cosmetic launch shared by satellite completion cues and ASAT flights: the vanilla VLS booster's plume, smoke
    /// trail, lights and roar (the same clone the TORCH bottles use) flown up a gravity-turn arc.
    /// It owns no weapon, collision, satellite availability or network authority.
    /// </summary>
    internal static class SatelliteLaunchVisuals
    {
        private static readonly List<GameObject> active = new List<GameObject>();

        public static void Reset()
        {
            foreach (GameObject effect in active) if (effect != null) Object.Destroy(effect);
            active.Clear();
        }

        public static void Play(Vector3 launchPoint, float duration)
        {
            if (GameManager.IsHeadless || Datum.origin == null || !float.IsFinite(duration)) return;
            VLSBooster source = GameAccess.VlsBoosterOrNull();
            if (source == null) return;
            active.RemoveAll(effect => effect == null);

            // A scene root: the floating origin moves every root and syncs physics, so the body rides the shifts.
            var go = new GameObject("BoscaliSatelliteLaunch");
            active.Add(go);
            go.SetActive(false);
            go.transform.position = launchPoint;

            // The booster cloned whole while the root is inactive so its brain never wakes; only the trail emitters stay.
            GameObject plume = Object.Instantiate(source.gameObject, go.transform);
            foreach (MonoBehaviour part in plume.GetComponentsInChildren<MonoBehaviour>(true))
                if (part != null && !(part is TrailEmitter)) Object.DestroyImmediate(part);
            foreach (Collider collider in plume.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            if (plume.TryGetComponent(out MeshRenderer casing)) Object.DestroyImmediate(casing);
            plume.transform.localPosition = Vector3.zero;
            plume.transform.localRotation = Quaternion.Inverse(source.transform.root.rotation) * source.transform.rotation;

            Rigidbody body = go.AddComponent<Rigidbody>(); // TrailEmitter lays smoke by the body's speed
            body.useGravity = false;
            body.detectCollisions = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            SatelliteLaunchEffect effect = go.AddComponent<SatelliteLaunchEffect>();
            go.SetActive(true);
            effect.Begin(body, Mathf.Clamp(duration, 3f, 90f));
        }
    }

    /// <summary>Flies the booster plume up a gravity-turn arc over its lifetime, burns out, lets the smoke hang, then removes itself.</summary>
    internal sealed class SatelliteLaunchEffect : MonoBehaviour
    {
        private const float ClimbHeight = 45000f;
        private const float Downrange = 30000f;
        private const float SmokeLinger = 30f;
        private static readonly System.Reflection.FieldInfo TrailLife = AccessTools.Field(typeof(TrailEmitter), "emitLifetime");

        private Rigidbody body;
        private ParticleSystem[] smoke;
        private TrailEmitter[] trails;
        private Light[] lamps;
        private AudioSource[] sound;
        private float elapsed;
        private float duration;
        private bool burnedOut;
        private Vector3 origin; // global (datum-space) launch point
        private Vector3 heading;

        public void Begin(Rigidbody rb, float durationSeconds)
        {
            body = rb;
            duration = durationSeconds;
            origin = transform.position - Datum.origin.position;
            // Launches head away from the theatre centre, which sits at the global origin.
            var outward = new Vector3(origin.x, 0f, origin.z);
            heading = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(Vector3.up, heading);

            smoke = GetComponentsInChildren<ParticleSystem>(true);
            trails = GetComponentsInChildren<TrailEmitter>(true);
            lamps = GetComponentsInChildren<Light>(true);
            sound = GetComponentsInChildren<AudioSource>(true);
            foreach (ParticleSystem system in smoke) system.Play();
            foreach (Light lamp in lamps) lamp.enabled = true;
            foreach (AudioSource source in sound) source.Play();
            foreach (TrailEmitter trail in trails)
            {
                TrailLife?.SetValue(trail, duration + 1f); // the trail switches itself off after a fixed emit time
                trail.rb = body;
                trail.StartTrail();
            }
        }

        /// <summary>The rocket's global position <paramref name="seconds"/> after lift-off.</summary>
        private Vector3 At(float seconds)
        {
            float t = Mathf.Clamp01(seconds / duration);
            float climb = t * t * (2f - t);
            return origin + Vector3.up * (ClimbHeight * climb) + heading * (Downrange * t * t * t);
        }

        private void FixedUpdate()
        {
            if (burnedOut || body == null) return;
            float dt = Time.fixedDeltaTime;
            elapsed = Mathf.Min(duration, elapsed + dt);
            Vector3 target = Datum.origin.position + At(elapsed);
            Vector3 velocity = (target - body.position) / dt;
            body.velocity = velocity;
            if (velocity.sqrMagnitude > 1f) body.MoveRotation(Quaternion.LookRotation(velocity));
            if (elapsed < duration) return;

            burnedOut = true;
            body.velocity = Vector3.zero;
            foreach (ParticleSystem system in smoke) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (TrailEmitter trail in trails) trail.StopTrail();
            foreach (Light lamp in lamps) lamp.enabled = false;
            foreach (AudioSource source in sound) source.Stop();
            Destroy(gameObject, SmokeLinger); // the trail smoke lives in datum space and hangs until then
        }
    }
}
