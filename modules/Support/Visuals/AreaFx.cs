using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// Believable cues for the OPS perks that have no missile to hang a visual on. No holographic rings or domes: RECON PASS / SAT CAMERA / RECON TEAM and ELINT only
    /// glint briefly on the contacts they pick up (a short wavefront of tiny warm flashes), and RADAR BLIND / SAM NET DOWN make the jammed emitters arc, spark and
    /// flicker their lights for the effect's duration, thinning out towards the end. Local presentation only (no collider, no sensor, no gameplay number); one
    /// pooled object per effect (two particle layers, four arc lines, four lights), no per-frame allocation. Played by the action on the machine that runs it.
    /// </summary>
    internal static class AreaFx
    {
        internal const int MaxActive = 4;
        private static readonly List<AreaFxEffect> active = new List<AreaFxEffect>(MaxActive);
        private static readonly Stack<AreaFxEffect> free = new Stack<AreaFxEffect>(MaxActive);
        private static readonly Vector3[] pingScratch = new Vector3[AreaFxEffect.MaxPings];

        public static readonly Color ReconTint = new Color(0.25f, 0.85f, 1.3f);
        public static readonly Color OpticalTint = new Color(1.3f, 1.1f, 0.65f);
        public static readonly Color ElintGreen = new Color(0.3f, 1.4f, 0.45f);
        public static readonly Color ElintAmber = new Color(1.5f, 0.85f, 0.18f);
        public static readonly Color BlindTint = new Color(1.5f, 0.8f, 0.15f);
        public static readonly Color SamTint = new Color(1.5f, 0.3f, 0.3f);
        public static readonly Color TeamTint = new Color(0.45f, 1.4f, 0.55f);

        /// <summary>A scanning ring with sweeping spokes over the reveal radius; hostile ground contacts get a ping as the spoke passes them.</summary>
        public static void Sweep(Vector3 centre, float radius, float seconds, Color tint, FactionHQ owner, bool pingEmittersOnly = false) =>
            Start(AreaFxKind.Sweep, centre, radius, seconds, tint, owner, pingEmittersOnly);

        /// <summary>An ELINT pulse: three rings running out from the aim, green fading to amber; working radars get an amber ping as the front passes.</summary>
        public static void Pulse(Vector3 centre, float radius, FactionHQ owner) =>
            Start(AreaFxKind.Pulse, centre, radius, 3.8f, ElintGreen, owner, true);

        /// <summary>The jamming dome: an animated noisy edge band, a faint wire dome and glitch sparks on the emitters inside, for the effect's duration.</summary>
        public static void Jam(Vector3 centre, float radius, float seconds, Color tint, FactionHQ owner) =>
            Start(AreaFxKind.Jam, centre, radius, seconds, tint, owner, true);

        /// <summary>The ground point under a global aim (statics and ships), in the local frame.</summary>
        public static Vector3 At(GlobalPosition target) =>
            Runtime.SupportTargeting.TryMapPoint(target, out Vector3 p) ? p : target.ToLocalPosition();

        private static void Start(AreaFxKind kind, Vector3 centre, float radius, float seconds, Color tint, FactionHQ owner, bool emitters)
        {
            if (GameManager.IsHeadless || radius < 50f) return;
            AreaFxEffect e;
            if (active.Count >= MaxActive) return;
            if (free.Count > 0) e = free.Pop();
            else
            {
                var go = new GameObject("BoscaliSummer.AreaFx");
                go.transform.SetParent(Datum.origin, false);
                e = go.AddComponent<AreaFxEffect>();
            }
            e.transform.SetParent(Datum.origin, false);
            e.transform.position = centre;
            int pings = CollectPings(owner, centre, radius, emitters, kind == AreaFxKind.Jam ? 8 : AreaFxEffect.MaxPings);
            active.Add(e);
            e.gameObject.SetActive(true);
            e.Begin(kind, radius, Mathf.Clamp(seconds, 2f, 600f), tint, pingScratch, pings);
        }

        internal static void Release(AreaFxEffect e)
        {
            active.Remove(e);
            e.gameObject.SetActive(false);
            free.Push(e);
        }

        public static void Reset()
        {
            for (int i = 0; i < active.Count; i++) if (active[i] != null) Object.Destroy(active[i].gameObject);
            active.Clear();
            while (free.Count > 0) { AreaFxEffect e = free.Pop(); if (e != null) Object.Destroy(e.gameObject); }
        }

        /// <summary>Hostile ground units inside the radius (offsets from the centre into <see cref="pingScratch"/>); with <paramref name="emitters"/> only those with a working radar.</summary>
        private static int CollectPings(FactionHQ owner, Vector3 centre, float radius, bool emitters, int cap)
        {
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return 0;
            int n = 0;
            float sq = radius * radius;
            for (int i = 0; i < units.Count && n < cap; i++)
            {
                Unit u = units[i];
                if (u == null || u.disabled || u is Aircraft) continue;
                FactionHQ hq = u.NetworkHQ;
                if (hq == null || hq == owner) continue;
                if (emitters && !(u.radar is Radar r && r != null && r.activated)) continue;
                Vector3 d = u.transform.position - centre;
                if (d.x * d.x + d.z * d.z > sq) continue;
                pingScratch[n++] = d;
            }
            return n;
        }
    }

    internal enum AreaFxKind : byte { Sweep, Pulse, Jam }

    internal sealed class AreaFxEffect : MonoBehaviour
    {
        internal const int MaxPings = 16;
        private const int Arcs = 4, ArcPoints = 8;

        private readonly Vector3[] pings = new Vector3[MaxPings];
        private readonly bool[] pinged = new bool[MaxPings];
        private readonly float[] pingAt = new float[MaxPings];
        private readonly Vector3[] arcPoints = new Vector3[ArcPoints];
        private readonly LineRenderer[] arcs = new LineRenderer[Arcs];
        private readonly float[] arcUntil = new float[Arcs];
        private readonly Light[] lights = new Light[Arcs];
        private readonly float[] lightNext = new float[Arcs];
        private ParticleSystem glints, sparks;
        private AreaFxKind kind;
        private float seconds, born, nextBurst, camDist;
        private int pingCount, arcCursor;
        private Color tint;

        public void Begin(AreaFxKind k, float radius, float duration, Color color, Vector3[] offsets, int count)
        {
            kind = k; seconds = k == AreaFxKind.Jam ? duration : 3.5f; tint = color; born = Time.time; nextBurst = 0.2f; arcCursor = 0;
            Build();
            pingCount = Mathf.Min(count, MaxPings);
            for (int i = 0; i < pingCount; i++)
            {
                pings[i] = offsets[i]; pinged[i] = false;
                pingAt[i] = new Vector2(offsets[i].x, offsets[i].z).magnitude / radius * 1.8f; // a wavefront from the aim outwards
            }
            glints.Clear(); sparks.Clear(); glints.Play(); sparks.Play();
            for (int i = 0; i < Arcs; i++) { arcs[i].enabled = false; lights[i].enabled = false; lightNext[i] = 0f; }
        }

        private void Build()
        {
            if (glints != null) return;
            glints = SupportParticles.Layer(transform, "Contact glint", true, 32, 1.1f, 22f, Color.white);
            sparks = SupportParticles.Layer(transform, "Arc sparks", true, 96, 0.7f, 2.2f, Color.white, 1.4f);
            for (int i = 0; i < Arcs; i++)
            {
                var go = new GameObject("Arc");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = SupportParticles.Lightning;
                line.useWorldSpace = false; line.positionCount = ArcPoints;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.alignment = LineAlignment.View;
                line.enabled = false;
                arcs[i] = line;
                var l = new GameObject("Flicker").AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.type = LightType.Point; l.range = 45f; l.shadows = LightShadows.None;
                l.color = new Color(1f, 0.82f, 0.55f); l.enabled = false;
                lights[i] = l;
            }
        }

        private void Update()
        {
            float age = Time.time - born;
            if (age >= seconds) { AreaFx.Release(this); return; }
            var csm = SceneSingleton<CameraStateManager>.i;
            Camera cam = csm != null ? csm.mainCamera : Camera.main;
            camDist = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 1000f;
            for (int i = 0; i < pingCount; i++)
            {
                if (pinged[i] || age < pingAt[i]) continue;
                pinged[i] = true;
                if (kind == AreaFxKind.Jam) continue;
                Color c = kind == AreaFxKind.Pulse ? AreaFx.ElintAmber : tint;
                c.a = 0.55f;
                glints.Emit(new ParticleSystem.EmitParams { position = pings[i] + Vector3.up * 3f, startColor = c, startSize = Mathf.Clamp(camDist * 0.004f, 18f, 90f) }, 1);
            }
            if (kind != AreaFxKind.Jam || pingCount == 0) return;

            float thin = Mathf.Lerp(1f, 2.6f, age / seconds); // arcing dies down as the jammer burns out
            for (int i = 0; i < Arcs; i++)
            {
                if (arcs[i].enabled && age > arcUntil[i]) arcs[i].enabled = false;
                if (i >= pingCount) continue;
                if (age >= lightNext[i])
                {
                    lightNext[i] = age + Random.Range(0.05f, 0.4f) * thin;
                    bool on = Random.value < 0.45f / thin;
                    lights[i].enabled = on;
                    if (on) { lights[i].transform.localPosition = pings[i] + Vector3.up * 6f; lights[i].intensity = Random.Range(1.5f, 4f); lights[i].range = 45f; }
                }
            }
            if (age < nextBurst) return;
            nextBurst = age + Random.Range(0.25f, 0.9f) * thin;
            Vector3 at = pings[Random.Range(0, pingCount)] + Vector3.up * Random.Range(2f, 6f);
            for (int s = 0; s < 7; s++)
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = at, velocity = Random.onUnitSphere * Random.Range(4f, 12f) + Vector3.up * 5f,
                    startColor = new Color(3f, 1.8f, 0.7f, 1f), startSize = Mathf.Clamp(camDist * 0.0007f, 0.6f, 4f)
                }, 1);
            int a = arcCursor++ % Arcs;
            Vector3 end = at + Random.onUnitSphere * Random.Range(4f, 9f);
            float w = Mathf.Clamp(camDist * 0.0008f, 0.25f, 3f);
            for (int j = 0; j < ArcPoints; j++)
                arcPoints[j] = Vector3.Lerp(at, end, j / (float)(ArcPoints - 1)) + (j == 0 || j == ArcPoints - 1 ? Vector3.zero : Random.insideUnitSphere * 1.1f);
            arcs[a].SetPositions(arcPoints);
            arcs[a].startWidth = w; arcs[a].endWidth = w * 0.4f;
            arcs[a].startColor = new Color(2.2f, 2.6f, 3.2f, 1f); arcs[a].endColor = new Color(0.6f, 0.9f, 2f, 0f);
            arcs[a].enabled = true; arcUntil[a] = age + Random.Range(0.06f, 0.16f);
        }
    }
}
