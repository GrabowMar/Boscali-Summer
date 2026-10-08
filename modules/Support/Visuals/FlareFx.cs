using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// DECOY BARRAGE visuals: the barrage's vanilla flares keep their gameplay (IR sources, missile spoofing) and gain a believable pyrotechnic look: a mortar pop with
    /// a sparks fan at the burst, every flare a white-hot sputtering head dropping on its ballistic arc, a thick white smoke trail that hangs and drifts on the wind, and a
    /// warm light under the cluster that lights the ground at night. One pooled object per barrage (two at most): about 3200 trail particles plus 200 heads and sparks,
    /// at most 96 flares followed.
    /// </summary>
    internal static class FlareFx
    {
        private static readonly List<FlareTrailFx> active = new List<FlareTrailFx>(2);
        internal static readonly Stack<FlareTrailFx> Free = new Stack<FlareTrailFx>(2);

        public static FlareTrailFx Begin(Vector3 point)
        {
            if (GameManager.IsHeadless || active.Count >= 2) return null;
            FlareTrailFx fx = Free.Count > 0 ? Free.Pop() : new GameObject("BoscaliSummer.FlareFx").AddComponent<FlareTrailFx>();
            fx.transform.SetParent(Datum.origin, false);
            fx.gameObject.SetActive(true);
            active.Add(fx);
            fx.Start(point);
            return fx;
        }

        internal static void Release(FlareTrailFx fx)
        {
            active.Remove(fx);
            fx.gameObject.SetActive(false);
            Free.Push(fx);
        }

        public static void Reset()
        {
            foreach (var f in active) if (f != null) Object.Destroy(f.gameObject);
            active.Clear();
            while (Free.Count > 0) { var f = Free.Pop(); if (f != null) Object.Destroy(f.gameObject); }
        }
    }

    [DefaultExecutionOrder(200)]
    internal sealed class FlareTrailFx : MonoBehaviour
    {
        private const int MaxFlares = 96, TrailCap = 3200;
        private readonly Transform[] flares = new Transform[MaxFlares];
        private readonly float[] until = new float[MaxFlares];
        private readonly Vector3[] last = new Vector3[MaxFlares];
        private readonly float[] nextTrail = new float[MaxFlares];
        private int count;
        private ParticleSystem pop, heads, sparks, trail, popSmoke;
        private Light glow;
        private float finishedAt = -1f;

        public void Start(Vector3 at)
        {
            if (heads == null)
            {
                pop = CineFx.Layer(transform, "Mortar pop", CineFx.Glow, 16);
                CineFx.Tint(pop, CineFx.Fade(0.03f, 0.25f));
                popSmoke = CineFx.Layer(transform, "Mortar smoke", CineFx.Smoke, 60, ParticleSystemRenderMode.Billboard, true);
                CineFx.Drag(popSmoke, 0.03f); CineFx.Grow(popSmoke, 0.5f, 2.6f); CineFx.Spin(popSmoke, 20f);
                CineFx.Tint(popSmoke, CineFx.Ramp(new Color(1f, 0.9f, 0.7f, 0f), 0f, new Color(0.95f, 0.95f, 0.95f, 0.7f), 0.08f, new Color(0.9f, 0.9f, 0.92f, 0.45f), 0.6f, new Color(0.9f, 0.9f, 0.92f, 0f), 1f));
                heads = CineFx.Layer(transform, "Flare heads", CineFx.Glow, 220);
                CineFx.Tint(heads, CineFx.Fade(0.15f, 0.6f));
                sparks = CineFx.Layer(transform, "Sputter", CineFx.Glow, 320, ParticleSystemRenderMode.Stretch, false, 1.5f, 0.04f);
                var gs = sparks.main; gs.gravityModifier = 0.8f;
                CineFx.Tint(sparks, CineFx.Ramp(new Color(1f, 1f, 0.95f, 1f), 0f, new Color(1f, 0.85f, 0.5f, 1f), 0.3f, new Color(1f, 0.5f, 0.15f, 0.6f), 0.7f, new Color(0.4f, 0.1f, 0.05f, 0f), 1f));
                trail = CineFx.Layer(transform, "Flare smoke", CineFx.Smoke, TrailCap, ParticleSystemRenderMode.Billboard, true);
                CineFx.Grow(trail, 0.4f, 3.4f, 0.6f); CineFx.Spin(trail, 18f); CineFx.Turbulence(trail, 1.8f, 0.12f);
                CineFx.Tint(trail, CineFx.Ramp(new Color(1f, 0.85f, 0.6f, 0f), 0f, new Color(0.96f, 0.96f, 0.96f, 0.62f), 0.04f, new Color(0.9f, 0.91f, 0.93f, 0.4f), 0.55f, new Color(0.88f, 0.9f, 0.93f, 0f), 1f));
                var g = new GameObject("Cluster glow"); g.transform.SetParent(transform, false);
                glow = g.AddComponent<Light>();
                glow.type = LightType.Point; glow.color = new Color(1f, 0.9f, 0.7f); glow.range = 800f; glow.shadows = LightShadows.None;
            }
            foreach (var ps in new[] { pop, popSmoke, heads, sparks, trail }) { ps.Clear(); ps.Play(); }
            count = 0; finishedAt = -1f; glow.enabled = false;
            CineFx.Drift(trail, CineFx.Wind() * 0.5f); CineFx.Drift(popSmoke, CineFx.Wind() * 0.3f);

            float day = Mathf.Lerp(0.55f, 1f, CineFx.Day01());
            pop.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(at), startSize = 140f, startLifetime = 0.35f, startColor = new Color(1f, 0.95f, 0.8f, 1f) }, 1);
            for (int i = 0; i < 8; i++)
                popSmoke.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(at), velocity = Random.onUnitSphere * Random.Range(10f, 30f), startSize = Random.Range(14f, 26f), startLifetime = Random.Range(5f, 9f), rotation = Random.Range(0f, 360f), startColor = new Color(day, day, day, 1f) }, 1);
            for (int i = 0; i < 40; i++)
                sparks.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(at), velocity = Random.onUnitSphere * Random.Range(20f, 90f), startSize = Random.Range(0.6f, 1.6f), startLifetime = Random.Range(0.6f, 1.6f) }, 1);
        }

        public void Track(Transform flare, float burnSeconds)
        {
            if (flare == null || count >= MaxFlares) return;
            flares[count] = flare; until[count] = Time.time + burnSeconds; last[count] = flare.position; nextTrail[count] = 0f;
            count++;
        }

        public void Finish() { if (finishedAt < 0f) finishedAt = Time.time; }

        private void LateUpdate()
        {
            float now = Time.time, shade = Mathf.Lerp(0.55f, 1f, CineFx.Day01());
            Vector3 centroid = Vector3.zero;
            int live = 0;
            for (int i = count - 1; i >= 0; i--)
            {
                Transform t = flares[i];
                if (t == null || now > until[i])
                {
                    count--; flares[i] = flares[count]; until[i] = until[count]; last[i] = last[count]; nextTrail[i] = nextTrail[count]; flares[count] = null;
                    continue;
                }
                Vector3 p = t.position;
                centroid += p; live++;
                if (Random.value > 0.12f)
                    heads.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(p), startSize = Random.Range(5f, 10f), startLifetime = 0.05f, startColor = new Color(1f, Random.Range(0.85f, 1f), Random.Range(0.6f, 0.9f), 1f) }, 1);
                if (Random.value < 0.45f)
                    sparks.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(p), velocity = Random.onUnitSphere * Random.Range(4f, 13f), startSize = Random.Range(0.3f, 0.8f), startLifetime = Random.Range(0.25f, 0.6f) }, 1);
                if (now >= nextTrail[i] && trail.particleCount < TrailCap - 8)
                {
                    nextTrail[i] = now + 0.11f;
                    Vector3 a = last[i];
                    for (int k = 0; k < 2; k++)
                        trail.Emit(new ParticleSystem.EmitParams
                        {
                            position = CineFx.ToSim(Vector3.Lerp(a, p, (k + 1) * 0.5f)), velocity = Random.insideUnitSphere * 1.2f,
                            startSize = Random.Range(2.4f, 4f), startLifetime = Random.Range(9f, 13f), rotation = Random.Range(0f, 360f),
                            startColor = new Color(shade, shade, shade, 1f)
                        }, 1);
                    last[i] = p;
                }
            }
            if (live > 0)
            {
                glow.enabled = true;
                glow.transform.position = centroid / live;
                glow.intensity = Mathf.Min(34f, live * 0.9f) * Random.Range(0.85f, 1f);
            }
            else glow.enabled = false;
            if (finishedAt >= 0f && live == 0 && now - finishedAt > 14f) FlareFx.Release(this);
        }
    }
}
