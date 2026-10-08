using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// SABOTAGE STRIKE: three charges go off in sequence on the anchor (flash, fireball, debris, a low dust skirt), the last one a secondary that throws burning debris, then the
    /// wreck burns under a column of black smoke for ~25 s. Pooled (3 objects, ~400 particles each),
    /// no allocation after the first play; presentation only (the anchor is already down on the host).
    /// </summary>
    internal static class StagedBlastFx
    {
        private const int Max = 3;
        private static readonly List<StagedBlastEffect> active = new List<StagedBlastEffect>(Max);
        private static readonly Stack<StagedBlastEffect> free = new Stack<StagedBlastEffect>(Max);

        public static void Play(Vector3 point, float footprint)
        {
            if (GameManager.IsHeadless || active.Count >= Max) return;
            StagedBlastEffect e;
            if (free.Count > 0) e = free.Pop();
            else
            {
                var go = new GameObject("BoscaliSummer.StagedBlast");
                e = go.AddComponent<StagedBlastEffect>();
            }
            e.transform.SetParent(Datum.origin, false);
            e.transform.position = point;
            active.Add(e);
            e.gameObject.SetActive(true);
            e.Begin(Mathf.Clamp(footprint, 6f, 40f));
        }

        /// <summary>Enemy ground units within the sabotage reach of the aim that are already down (so the strike's own victim can be told apart afterwards).</summary>
        internal static void DownAnchors(FactionHQ owner, GlobalPosition target, List<Unit> into)
        {
            Each(owner, target, (u, _) => into.Add(u), into, false);
        }

        /// <summary>Plays the staged charges on the nearest unit that went down since <see cref="DownAnchors"/>.</summary>
        internal static void PlayOnNewlyDown(FactionHQ owner, GlobalPosition target, List<Unit> before)
        {
            Unit best = null; float bestSq = float.MaxValue;
            Each(owner, target, (u, sq) => { if (sq < bestSq) { bestSq = sq; best = u; } }, before, true);
            if (best != null) Play(best.transform.position, 14f);
        }

        private static void Each(FactionHQ owner, GlobalPosition target, System.Action<Unit, float> visit, List<Unit> known, bool skipKnown)
        {
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return;
            Vector3 c = target.ToLocalPosition();
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u == null || u is Aircraft || u.NetworkHQ == null || u.NetworkHQ == owner) continue;
                Vector3 d = u.transform.position - c;
                float sq = d.x * d.x + d.z * d.z;
                if (sq > 1100f * 1100f) continue;
                if (skipKnown && known.Contains(u)) continue;
                if (!Runtime.UplinkSpawner.Down(u, u.NetworkHQ)) continue;
                visit(u, sq);
            }
        }

        internal static void Release(StagedBlastEffect e)
        {
            active.Remove(e);
            e.gameObject.SetActive(false);
            free.Push(e);
        }

        public static void Reset()
        {
            for (int i = 0; i < active.Count; i++) if (active[i] != null) Object.Destroy(active[i].gameObject);
            active.Clear();
            while (free.Count > 0) { StagedBlastEffect e = free.Pop(); if (e != null) Object.Destroy(e.gameObject); }
        }
    }

    internal sealed class StagedBlastEffect : MonoBehaviour
    {
        private static readonly float[] Delays = { 0f, 0.85f, 1.8f };
        private const float SmokeSeconds = 26f;
        private ParticleSystem flash, fire, smoke, debris, dust, flames;
        private Light light;
        private float born, footprint, lightBoost;
        private int next;
        private float nextSmoke;
        private readonly Vector3[] spots = new Vector3[3];

        public void Begin(float size)
        {
            footprint = size; born = Time.time; next = 0; nextSmoke = 0f; lightBoost = 0f;
            if (flash == null)
            {
                flash = SupportParticles.Layer(transform, "Charge flash", true, 12, 0.45f, 38f, new Color(4f, 2.6f, 1.3f));
                fire = SupportParticles.Layer(transform, "Charge fireball", true, 60, 1.5f, 16f, new Color(1.6f, 0.55f, 0.1f));
                debris = SupportParticles.Layer(transform, "Charge debris", true, 72, 2.2f, 1.6f, new Color(3f, 1.2f, 0.25f), 2.2f);
                smoke = SupportParticles.Layer(transform, "Charge smoke", false, 160, 14f, 17f, new Color(0.07f, 0.065f, 0.06f, 0.92f), -0.04f);
                dust = SupportParticles.Layer(transform, "Charge dust skirt", false, 96, 5f, 22f, new Color(0.5f, 0.42f, 0.33f, 0.55f));
                flames = SupportParticles.Layer(transform, "Wreck fire", true, 48, 1.3f, 9f, new Color(1.5f, 0.5f, 0.1f));
                var l = new GameObject("Charge light");
                l.transform.SetParent(transform, false);
                l.transform.localPosition = Vector3.up * 8f;
                light = l.AddComponent<Light>();
                light.type = LightType.Point; light.range = 160f; light.shadows = LightShadows.None;
                light.color = new Color(1f, 0.6f, 0.25f);
            }
            flash.Clear(); fire.Clear(); debris.Clear(); smoke.Clear(); dust.Clear(); flames.Clear();
            flash.Play(); fire.Play(); debris.Play(); smoke.Play(); dust.Play(); flames.Play();
            for (int i = 0; i < spots.Length; i++)
            {
                Vector2 c = Random.insideUnitCircle * footprint;
                spots[i] = new Vector3(c.x, 2f, c.y);
            }
            light.intensity = 0f;
        }

        private void Update()
        {
            float age = Time.time - born;
            if (age > SmokeSeconds + 12f) { StagedBlastFx.Release(this); return; }
            while (next < Delays.Length && age >= Delays[next]) Charge(spots[next++], next == Delays.Length);
            if (age < SmokeSeconds && age >= nextSmoke)
            {
                nextSmoke = age + 0.14f;
                float fade = 1f - age / SmokeSeconds;
                Vector3 s = spots[Random.Range(0, spots.Length)];
                if (age > 1f)
                    flames.Emit(new ParticleSystem.EmitParams
                    {
                        position = s + Random.insideUnitSphere * 2.5f + Vector3.up * 1.5f,
                        velocity = new Vector3(Random.Range(-1f, 1f), Random.Range(3f, 7f), Random.Range(-1f, 1f)) * (0.4f + fade * 0.6f),
                        startSize = Random.Range(5f, 10f) * (0.5f + fade * 0.5f)
                    }, 1);
                smoke.Emit(new ParticleSystem.EmitParams
                {
                    position = s + Vector3.up * 3f,
                    velocity = new Vector3(Random.Range(-2f, 2f), Random.Range(9f, 15f) * (0.5f + fade * 0.5f), Random.Range(-2f, 2f)),
                    rotation = Random.Range(0f, 360f)
                }, 1);
            }
            lightBoost = Mathf.Max(0f, lightBoost - Time.deltaTime * 5f);
            light.intensity = lightBoost * 14f;
        }

        private void Charge(Vector3 at, bool last)
        {
            float k = last ? 1.5f : 1f;
            flash.Emit(new ParticleSystem.EmitParams { position = at + Vector3.up * 4f }, 1);
            for (int i = 0; i < 10; i++)
                fire.Emit(new ParticleSystem.EmitParams
                {
                    position = at + Random.insideUnitSphere * 3f + Vector3.up * 2f,
                    velocity = (Random.insideUnitSphere * 9f + Vector3.up * 11f) * k,
                    rotation = Random.Range(0f, 360f)
                }, 1);
            for (int i = 0; i < 14; i++)
                debris.Emit(new ParticleSystem.EmitParams
                {
                    position = at + Vector3.up * 2f,
                    velocity = (Random.insideUnitSphere * 18f + Vector3.up * 24f) * k
                }, 1);
            for (int i = 0; i < 6; i++)
                smoke.Emit(new ParticleSystem.EmitParams
                {
                    position = at + Random.insideUnitSphere * 4f,
                    velocity = Random.insideUnitSphere * 7f + Vector3.up * 8f,
                    rotation = Random.Range(0f, 360f)
                }, 1);
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                dust.Emit(new ParticleSystem.EmitParams { position = at + dir * 4f, velocity = dir * Random.Range(16f, 26f) * k + Vector3.up * 1.5f, rotation = Random.Range(0f, 360f) }, 1);
            }
            lightBoost = 1f;
        }
    }

    /// <summary>
    /// JTAC LASE: a thin pulsing designator beam from a spotter point to the lased unit, a bright spot with sparkle on the target and a small ring, for the lase seconds.
    /// Follows the unit (rebuilt every frame in the late update so a floating-origin shift cannot flash it); ends when the unit dies or the lase is dropped.
    /// </summary>
    internal static class LaseFx
    {
        private const int Max = 4;
        private static readonly List<LaseEffect> active = new List<LaseEffect>(Max);
        private static readonly Stack<LaseEffect> free = new Stack<LaseEffect>(Max);

        public static void Play(Unit target, FactionHQ owner, float seconds)
        {
            if (GameManager.IsHeadless || target == null || active.Count >= Max) return;
            LaseEffect e;
            if (free.Count > 0) e = free.Pop();
            else
            {
                var go = new GameObject("BoscaliSummer.Lase");
                e = go.AddComponent<LaseEffect>();
            }
            e.transform.SetParent(Datum.origin, false);
            active.Add(e);
            e.gameObject.SetActive(true);
            e.Begin(target, owner, Mathf.Clamp(seconds, 2f, 300f));
        }

        internal static void Release(LaseEffect e)
        {
            active.Remove(e);
            e.gameObject.SetActive(false);
            free.Push(e);
        }

        public static void Reset()
        {
            for (int i = 0; i < active.Count; i++) if (active[i] != null) Object.Destroy(active[i].gameObject);
            active.Clear();
            while (free.Count > 0) { LaseEffect e = free.Pop(); if (e != null) Object.Destroy(e.gameObject); }
        }
    }

    [DefaultExecutionOrder(200)]
    internal sealed class LaseEffect : MonoBehaviour
    {
        private static readonly Color Laser = new Color(1.8f, 0.2f, 0.25f);
        private readonly Vector3[] beamPoints = new Vector3[2];
        private readonly Vector3[] ringPoints = new Vector3[24];
        private LineRenderer beam, ring;
        private ParticleSystem spark;
        private Unit target;
        private FactionHQ owner;
        private float born, seconds, nextCheck, nextSpark, bearing;

        public void Begin(Unit unit, FactionHQ faction, float duration)
        {
            target = unit; owner = faction; seconds = duration; born = Time.time; nextCheck = 0.5f; nextSpark = 0f;
            bearing = (unit.persistentID.Id % 360u) * Mathf.Deg2Rad;
            if (beam == null)
            {
                beam = Make("Beam", 2, false);
                ring = Make("Spot ring", ringPoints.Length, true);
                spark = SupportParticles.Layer(transform, "Spot sparkle", true, 24, 0.35f, 5f, Laser);
            }
            spark.Clear(); spark.Play();
            Place();
        }

        private LineRenderer Make(string name, int points, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = SupportParticles.Lightning;
            line.useWorldSpace = true; line.positionCount = points; line.loop = loop;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.alignment = LineAlignment.View;
            return line;
        }

        private void LateUpdate()
        {
            float age = Time.time - born;
            if (target == null || target.disabled || age >= seconds) { LaseFx.Release(this); return; }
            if (age >= nextCheck)
            {
                nextCheck = age + 0.5f;
                if (owner != null && !owner.IsTargetLased(target)) { LaseFx.Release(this); return; }
            }
            Place();
            var csm = SceneSingleton<CameraStateManager>.i;
            Camera cam = csm != null ? csm.mainCamera : Camera.main;
            Vector3 spot = target.transform.position;
            float range = cam != null ? Vector3.Distance(cam.transform.position, spot) : 500f;
            float w = Mathf.Clamp(range * 0.0012f, 0.35f, 8f);
            float flicker = 0.7f + 0.3f * Mathf.Sin(age * 38f) * (Random.value > 0.85f ? 0.5f : 1f);
            float fade = Mathf.Clamp01(age / 0.3f) * Mathf.Clamp01((seconds - age) / 0.6f);
            Color c = Laser; c.a = 0.55f * flicker * fade;
            beam.startColor = c; beam.endColor = c; beam.startWidth = w * 0.6f; beam.endWidth = w;
            Color rc = Laser; rc.a = 0.8f * fade * (0.6f + 0.4f * Mathf.Sin(age * 9f));
            ring.startColor = rc; ring.endColor = rc; ring.startWidth = w * 1.6f; ring.endWidth = w * 1.6f;
            if (age >= nextSpark)
            {
                nextSpark = age + 0.06f;
                spark.Emit(new ParticleSystem.EmitParams
                {
                    position = transform.InverseTransformPoint(spot + Vector3.up * 1.5f + Random.insideUnitSphere * 1.2f),
                    velocity = Random.insideUnitSphere * 3f,
                    startSize = Mathf.Clamp(range * 0.006f, 3f, 24f)
                }, 1);
            }
        }

        private void Place()
        {
            Vector3 spot = target.transform.position + Vector3.up * 1.5f;
            Vector3 source = spot + new Vector3(Mathf.Cos(bearing), 0f, Mathf.Sin(bearing)) * 900f;
            source.y = spot.y + 25f;
            beamPoints[0] = source; beamPoints[1] = spot;
            beam.SetPositions(beamPoints);
            float r = 7f + Mathf.Sin((Time.time - born) * 9f) * 1.5f;
            for (int i = 0; i < ringPoints.Length; i++)
            {
                float a = i * Mathf.PI * 2f / ringPoints.Length;
                ringPoints[i] = spot + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            ring.SetPositions(ringPoints);
        }
    }
}
