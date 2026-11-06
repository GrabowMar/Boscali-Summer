using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    /// <summary>
    /// Missile sonic boom (2026-11-06, idea from Kelly's Client Effects, written from scratch): a supersonic missile
    /// whose Mach cone sweeps over the camera cracks once, with the game's own boom clip pitched up. The listener is
    /// inside the cone when the angle from the missile's tail axis to it is under the Mach angle asin(1/M).
    /// Missiles are found every half second within <see cref="Reach"/>; each is heard at most once.
    /// </summary>
    internal sealed class MissileBoom
    {
        private const float Reach = 1500f, ScanEvery = .5f;
        private const int MaxTracked = 48;

        private readonly Dictionary<Missile, bool> inside = new Dictionary<Missile, bool>();
        private readonly HashSet<Missile> heard = new HashSet<Missile>();
        private readonly List<Missile> drop = new List<Missile>();
        private float nextScan, lastBoom = -10f;

        internal int Played { get; private set; }

        /// <summary>True when <paramref name="toListener"/> (missile to listener) lies inside the Mach cone behind a missile flying along <paramref name="velocity"/>.</summary>
        internal static bool InsideCone(Vector3 toListener, Vector3 velocity, float soundSpeed)
        {
            float speed = velocity.magnitude, distance = toListener.magnitude;
            if (soundSpeed <= 0f || speed <= soundSpeed || distance < 1f) return false;
            float mach = speed / soundSpeed;
            return Vector3.Dot(toListener / distance, -velocity / speed) > Mathf.Sqrt(1f - 1f / (mach * mach));
        }

        internal void Tick(Vector3 listener, bool enabled, Transform host)
        {
            if (!enabled) { Reset(); return; }
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + ScanEvery;
                drop.Clear();
                foreach (Missile m in inside.Keys)
                    if (m == null || (m.transform.position - listener).sqrMagnitude > Reach * Reach) drop.Add(m);
                foreach (Missile m in drop) { inside.Remove(m); heard.Remove(m); }
                heard.RemoveWhere(m => m == null);
                List<Unit> units = UnitRegistry.allUnits;
                for (int i = 0; units != null && i < units.Count && inside.Count < MaxTracked; i++)
                    if (units[i] is Missile m && m != null && !inside.ContainsKey(m) && !heard.Contains(m) &&
                        (m.transform.position - listener).sqrMagnitude < Reach * Reach)
                        inside[m] = false;
            }
            drop.Clear();
            foreach (var kv in inside)
            {
                Missile m = kv.Key;
                if (m == null || m.rb == null || heard.Contains(m)) continue;
                Vector3 at = m.transform.position;
                bool now = InsideCone(listener - at, m.rb.velocity, LevelInfo.GetSpeedOfSound(at.GlobalY()));
                if (now && !kv.Value && Time.unscaledTime - lastBoom > .2f && Play(listener, at, host))
                {
                    heard.Add(m);
                    lastBoom = Time.unscaledTime;
                }
                if (now != kv.Value) drop.Add(m);
            }
            foreach (Missile m in drop) if (inside.ContainsKey(m)) inside[m] = !inside[m];
        }

        private bool Play(Vector3 listener, Vector3 missile, Transform host)
        {
            AudioClip clip = GameAssets.i != null ? GameAssets.i.sonicBoom : null;
            if (clip == null || host == null) return false;
            var go = new GameObject("BoscaliMissileBoom") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(host, false);
            float distance = Vector3.Distance(listener, missile);
            // Placed a short way toward the missile: the right side of the head, at a volume set by the true distance.
            go.transform.position = listener + (missile - listener) * Mathf.Min(1f, 60f / Mathf.Max(1f, distance));
            AudioSource s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.outputAudioMixerGroup = SoundManager.i != null ? SoundManager.i.EffectsMixer : null;
            s.spatialBlend = 1f;
            s.dopplerLevel = 0f;
            s.minDistance = 40f;
            s.maxDistance = 400f;
            s.volume = Mathf.Lerp(.7f, .25f, distance / Reach);
            s.pitch = 1.3f;
            s.Play();
            Object.Destroy(go, clip.length / s.pitch + .2f);
            Played++;
            return true;
        }

        internal void Reset()
        {
            inside.Clear();
            heard.Clear();
        }
    }
}
