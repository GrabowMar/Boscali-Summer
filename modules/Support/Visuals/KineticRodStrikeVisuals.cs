using NuclearOption.Effects;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// ORBITAL ROD: a hypersonic tungsten rod. The descent is an incandescent plasma streak (white-hot head, orange wake, spalling sparks) with a thick vapour contrail that
    /// hangs and drifts behind it; the strike is a blinding flash, a ground-hugging dust and debris shock skirt with a pressure ring running out in front of it, a roiling
    /// fireball, embers, and a dirty column that keeps feeding and blooms into a cap, thinning and shearing off in the wind within about half a minute, leaving a scorched crater. All of it is local
    /// particles (about 1000 live at the peak, two strikes and four descents at most) and two lights; the gameplay (blast damage, the missile) is untouched.
    /// </summary>
    internal static class KineticRodStrikeVisuals
    {
        private static readonly List<RodImpactFx> impacts = new List<RodImpactFx>(2);
        internal static readonly Stack<RodImpactFx> FreeImpacts = new Stack<RodImpactFx>(2);
        internal static readonly Stack<RodTrailFx> FreeTrails = new Stack<RodTrailFx>(4);
        private static readonly List<RodTrailFx> trails = new List<RodTrailFx>(4);

        public static void Reset()
        {
            SupportParticles.DestroyPool(impacts, FreeImpacts);
            SupportParticles.DestroyPool(trails, FreeTrails);
            for (int i = 0; i < craters.Count; i++) if (craters[i] != null) Object.Destroy(craters[i]);
            craters.Clear(); craterHead = 0;
        }

        public static void Track(Missile missile)
        {
            if (missile == null || GameManager.IsHeadless || trails.Count >= 6) return;
            if (missile.GetComponent<KineticRodDescentEffect>() != null) return;
            missile.gameObject.AddComponent<KineticRodDescentEffect>().Initialize();
        }

        /// <summary><paramref name="scale"/> below 1 is a STARFALL rod: a smaller, shorter strike, and up to six of them may be alive at once next to two full ones.</summary>
        public static void TriggerImpact(Vector3 impactPosition, float scale = 1f)
        {
            if (GameManager.IsHeadless) return;
            bool small = scale < 0.95f;
            int big = 0;
            for (int i = 0; i < impacts.Count; i++) if (!impacts[i].Small) big++;
            if (small ? impacts.Count - big >= 6 : big >= 2) return;
            RodImpactFx fx;
            if (FreeImpacts.Count > 0) fx = FreeImpacts.Pop();
            else { var go = new GameObject("BoscaliSummer.RodImpact"); fx = go.AddComponent<RodImpactFx>(); }
            fx.transform.SetParent(Datum.origin, false);
            fx.gameObject.SetActive(true);
            impacts.Add(fx);
            fx.Begin(impactPosition, small ? Mathf.Max(0.3f, scale) : 1f);
        }

        internal static void Release(RodImpactFx fx) => SupportParticles.Recycle(impacts, FreeImpacts, fx);

        // ponytail: a ring of 12 scene-long craters, the oldest reused; the vanilla soot decal stacked twice (scar + darker core) stands in for a real crater mesh
        private const int MaxCraters = 12;
        private static readonly List<GameObject> craters = new List<GameObject>(MaxCraters * 2);
        private static int craterHead;

        /// <summary>The burnt crater a strike leaves for the rest of the scene: a wide scorched scar with a dark core, projected down onto terrain and roofs.</summary>
        internal static void StampCrater(Vector3 ground, Vector3 normal, float scale)
        {
            GameObject prefab = GameAssets.i != null ? GameAssets.i.scorchMarkDecal : null;
            if (prefab == null || Datum.origin == null) return;
            float roll = Random.Range(0f, 360f);
            Place(ground, normal, 120f * scale, roll);
            Place(ground, normal, 48f * scale, roll + 137f);

            void Place(Vector3 at, Vector3 n, float size, float spin)
            {
                GameObject mark;
                if (craters.Count < MaxCraters * 2) { mark = Object.Instantiate(prefab, Datum.origin, false); mark.name = "BoscaliSummer.RodCrater"; craters.Add(mark); }
                else { mark = craters[craterHead]; craterHead = (craterHead + 1) % craters.Count; }
                if (mark == null) return;
                mark.SetActive(true);
                var projector = mark.GetComponent<DecalProjector>();
                if (projector == null) return;
                float depth = Mathf.Max(12f, size * 0.3f);
                mark.transform.rotation = Quaternion.AngleAxis(spin, n) * Quaternion.LookRotation(-n, Vector3.ProjectOnPlane(Vector3.forward, n));
                mark.transform.position = at + n * (depth * 0.45f);
                projector.renderingLayerMask = ~0u;
                projector.size = new Vector3(size, size, depth);
                projector.startAngleFade = 60f; projector.endAngleFade = 85f;
                projector.fadeFactor = 1f;
                projector.drawDistance = 6000f;
            }
        }

        internal static RodTrailFx RentTrail()
        {
            RodTrailFx t;
            if (FreeTrails.Count > 0) t = FreeTrails.Pop();
            else { var go = new GameObject("BoscaliSummer.RodTrail"); t = go.AddComponent<RodTrailFx>(); }
            t.transform.SetParent(Datum.origin, false);
            t.gameObject.SetActive(true);
            trails.Add(t);
            return t;
        }

        internal static void Release(RodTrailFx t) => SupportParticles.Recycle(trails, FreeTrails, t);
    }

    // ------------------------------------------------------------------------------------------------------------------------
    // Descent
    // ------------------------------------------------------------------------------------------------------------------------

    /// <summary>On the rod's missile: hides the stock model, lights the head and feeds the trail from where the rod was to where it is.</summary>
    [DefaultExecutionOrder(200)]
    internal sealed class KineticRodDescentEffect : MonoBehaviour
    {
        private static AudioClip whine;
        private Renderer[] stock;
        private RodTrailFx trail;
        private Light head;
        private AudioSource audioSource;
        private Vector3 last;
        private bool haveLast, detonated;

        public void Initialize()
        {
            stock = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < stock.Length; i++) stock[i].enabled = false;
            trail = KineticRodStrikeVisuals.RentTrail();
            trail.Begin();
            var l = new GameObject("RodHeadLight");
            l.transform.SetParent(transform, false);
            head = l.AddComponent<Light>();
            head.type = LightType.Point; head.color = new Color(1f, 0.93f, 0.8f);
            head.range = 1200f; head.intensity = 60f; head.shadows = LightShadows.None;
            if (whine == null) whine = BuildWhine();
            audioSource = CineFx.Speaker(gameObject, whine, 500f, 60000f, 1f);
            audioSource.loop = true; audioSource.dopplerLevel = 1.5f;
            audioSource.Play();
        }

        public void MarkDetonated()
        {
            detonated = true;
            if (audioSource != null) audioSource.Stop();
            if (head != null) head.enabled = false;
            if (trail != null) { trail.End(); trail = null; }
        }

        private void LateUpdate()
        {
            if (detonated || trail == null) return;
            Vector3 p = transform.position;
            if (haveLast) trail.Feed(last, p, Time.deltaTime);
            last = p; haveLast = true;
            // the last seconds: the ground starts to tremble under a rod you can hear coming
            float agl = p.y - Datum.LocalSeaY;
            if (agl < 2500f) CineFx.Shake(p, 12000f, 0.25f * (1f - agl / 2500f));
        }

        private void OnDestroy()
        {
            if (trail != null) { trail.End(); trail = null; }
            if (head != null) Destroy(head.gameObject);
        }

        private static AudioClip BuildWhine() => CineFx.Clip("RodWhine", 2f, t =>
        {
            float w = 1000f + 140f * Mathf.Sin(t * 6.28f);
            float tone = Mathf.Sin(6.2832f * w * t) * 0.07f + Mathf.Sin(6.2832f * w * 1.51f * t) * 0.04f;
            return CineFx.Noise() * 0.33f + tone;
        });
    }

    /// <summary>The descent trail: plasma head and wake, spall sparks, and the vapour contrail that stays behind after the rod is gone.</summary>
    internal sealed class RodTrailFx : MonoBehaviour
    {
        // 9500 / 11 = 864 contrail puffs, inside the layer's 900; a 2300 m/s rod keeps ~72 glows of 1.4 s alive, inside 120
        private const float ContrailStep = 11f, ContrailTop = 9500f, VapourStep = 45f;
        private ParticleSystem head, plasma, sparks, contrail, vapour;
        private float endAt, contrailDue, vapourDue;
        private bool ending;

        public void Begin()
        {
            if (head == null)
            {
                head = CineFx.Layer(transform, "Rod head", CineFx.Glow, 24);
                plasma = CineFx.Layer(transform, "Rod plasma", CineFx.Glow, 700);
                CineFx.Grow(plasma, 1f, 0.3f, 0.3f);
                CineFx.Tint(plasma, CineFx.Ramp(new Color(1f, 1f, 0.95f, 0f), 0f, new Color(1f, 0.85f, 0.45f, 1f), 0.08f, new Color(1f, 0.4f, 0.08f, 0.55f), 0.45f, new Color(0.5f, 0.1f, 0.04f, 0f), 1f));
                sparks = CineFx.Layer(transform, "Rod spall", CineFx.Glow, 400, ParticleSystemRenderMode.Stretch, false, 2.5f, 0.05f);
                CineFx.Tint(sparks, CineFx.Ramp(new Color(1f, 1f, 0.9f, 1f), 0f, new Color(1f, 0.7f, 0.25f, 1f), 0.3f, new Color(1f, 0.35f, 0.08f, 0.6f), 0.7f, new Color(0.4f, 0.1f, 0.05f, 0f), 1f));
                contrail = CineFx.Layer(transform, "Rod contrail", CineFx.Smoke, 900, ParticleSystemRenderMode.Billboard, true);
                CineFx.Grow(contrail, 0.5f, 3.2f, 0.6f);
                CineFx.Spin(contrail, 14f);
                CineFx.Turbulence(contrail, 5f, 0.05f);
                CineFx.Tint(contrail, CineFx.Ramp(new Color(1f, 0.82f, 0.55f, 0f), 0f, new Color(0.95f, 0.93f, 0.9f, 0.6f), 0.07f, new Color(0.85f, 0.86f, 0.9f, 0.4f), 0.6f, new Color(0.8f, 0.82f, 0.88f, 0f), 1f));
                vapour = CineFx.Layer(transform, "Rod vapour glow", CineFx.Glow, 120);
                CineFx.Tint(vapour, CineFx.Fade(0.1f, 0.4f, 0.35f));
            }
            ending = false; contrailDue = vapourDue = 0f;
            head.Clear(); plasma.Clear(); sparks.Clear(); contrail.Clear(); vapour.Clear();
            head.Play(); plasma.Play(); sparks.Play(); contrail.Play(); vapour.Play();
            CineFx.Drift(contrail, CineFx.Wind() * 0.5f);
        }

        public void End() { if (ending) return; ending = true; endAt = Time.time + 32f; }

        public void Feed(Vector3 from, Vector3 to, float dt)
        {
            if (ending || dt <= 0f) return;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.5f) return;
            Vector3 dir = d / len;
            float day = CineFx.Day01();
            // glare: a bright head that tracks the rod, brighter against a dark sky
            head.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(to), startSize = 90f + Random.Range(0f, 30f), startLifetime = 0.06f, startColor = new Color(1f, 0.97f, 0.9f, 1f) }, 1);
            head.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(to), startSize = 260f, startLifetime = 0.05f, startColor = new Color(1f, 0.6f, 0.25f, 0.35f) }, 1);
            int n = Mathf.Clamp((int)(len / 9f), 1, 26);
            for (int i = 0; i < n; i++)
            {
                float k = (i + Random.value) / n;
                Vector3 pos = Vector3.Lerp(from, to, k);
                plasma.Emit(new ParticleSystem.EmitParams
                {
                    position = CineFx.ToSim(pos), velocity = Random.insideUnitSphere * 6f - dir * 14f,
                    startSize = Random.Range(26f, 40f), startLifetime = Random.Range(0.35f, 0.55f)
                }, 1);
            }
            int s = Mathf.Min(6, 1 + (int)(len / 40f));
            for (int i = 0; i < s; i++)
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = CineFx.ToSim(Vector3.Lerp(from, to, Random.value)),
                    velocity = Random.onUnitSphere * Random.Range(40f, 140f) - dir * Random.Range(40f, 160f),
                    startSize = Random.Range(1.2f, 3.2f), startLifetime = Random.Range(0.4f, 1f)
                }, 1);
            // The contrail forms in the thick air: one puff every ContrailStep metres below ContrailTop, the distance carried from frame to frame, so it is a continuous
            // line at any frame rate (one puff per 28 m frame step left 9-15 m puffs 40+ m apart, a bead chain) and the whole column fits the layer's 900 puffs.
            if (to.y - Datum.LocalSeaY < ContrailTop) contrailDue += len;
            int c = Mathf.Min(48, (int)(contrailDue / ContrailStep));
            contrailDue = Mathf.Min(contrailDue - c * ContrailStep, ContrailStep);
            float shade = Mathf.Lerp(0.45f, 1f, day);
            for (int i = 0; i < c; i++)
            {
                Vector3 pos = Vector3.Lerp(from, to, (i + Random.value) / c) - dir * 6f;
                contrail.Emit(new ParticleSystem.EmitParams
                {
                    position = CineFx.ToSim(pos), velocity = Random.insideUnitSphere * 3f - dir * 6f,
                    startSize = Random.Range(18f, 30f), startLifetime = Random.Range(22f, 30f), rotation = Random.Range(0f, 360f),
                    startColor = new Color(shade, shade, shade, 1f)
                }, 1);
            }
            // the hot wake glow, one every VapourStep metres (a 40 % chance per frame strung 120 m blobs along the line like beads)
            vapourDue += len;
            int v = Mathf.Min(4, (int)(vapourDue / VapourStep));
            vapourDue = Mathf.Min(vapourDue - v * VapourStep, VapourStep);
            for (int i = 0; i < v; i++)
                vapour.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(Vector3.Lerp(from, to, (i + 1f) / v) - dir * 40f), startSize = 120f, startLifetime = 1.4f, startColor = new Color(1f, 0.55f, 0.2f, 0.5f) }, 1);
        }

        private void Update()
        {
            if (ending && Time.time >= endAt) KineticRodStrikeVisuals.Release(this);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------
    // Strike
    // ------------------------------------------------------------------------------------------------------------------------

    internal sealed class RodImpactFx : MonoBehaviour
    {
        private const float Life = 55f, StemSeconds = 15f, CapAt = 9.5f, CraterFire = 34f;
        private static AudioClip boom;
        private ParticleSystem flash, fireGlow, fireBody, skirt, lift, haze, rings, ringsAdd, embers, chunks, stem, cap, crater;
        private Light flashLight, glowLight;
        private AudioSource audioSource;
        private Vector3 ground;
        private float born, nextStem, nextCrater, shakeAt, shakeStrength;
        private bool capDone;
        private float day, scale = 1f, life = Life;

        /// <summary>A STARFALL rod's strike: smaller particles and fewer of them, none of the long smoke column.</summary>
        public bool Small => scale < 0.95f;

        public void Begin(Vector3 point, float s = 1f)
        {
            Build();
            scale = s;
            bool small = Small;
            life = small ? 16f : Life;
            int N(int n) => small ? Mathf.Max(4, Mathf.RoundToInt(n * s)) : n;
            ground = point;
            Vector3 normal = Vector3.up;
            if (Physics.Linecast(point + Vector3.up * 200f, point - Vector3.up * 400f, out RaycastHit hit, PhysicsLayers.StaticsMask)) { ground = hit.point; normal = hit.normal; }
            KineticRodStrikeVisuals.StampCrater(ground, normal, s);
            if (!small) BoscaliSummer.Core.Fx.ParticleFx.Spawn("ops-rod-impact", ground, Quaternion.identity); // authored debris/skirt/embers on top
            transform.localPosition = Vector3.zero;
            born = Time.time; nextStem = small ? float.MaxValue : 0f; nextCrater = small ? float.MaxValue : 0f; capDone = small;
            day = CineFx.Day01();
            Vector3 wind = CineFx.Wind();
            foreach (var ps in new[] { flash, fireGlow, fireBody, skirt, lift, haze, rings, ringsAdd, embers, chunks, stem, cap, crater }) { ps.Clear(); ps.Play(); }
            CineFx.Drift(stem, wind * 1.6f); CineFx.Drift(cap, wind * 1.6f); CineFx.Drift(haze, wind * 0.5f); CineFx.Drift(lift, wind * 0.4f);

            try { SceneSingleton<BlastManager>.i?.AddBlast(ground.ToGlobalPosition(), 70f * s); } catch (System.Exception) { }

            float shade = Mathf.Lerp(0.38f, 1f, day);
            // 1. the flash: a white-out, then a hot after-image
            Burst(flash, ground + Vector3.up * 14f * s, 1400f * s, 0.22f, new Color(1f, 1f, 1f, 1f));
            Burst(flash, ground + Vector3.up * 20f * s, 520f * s, 0.7f, new Color(1f, 0.92f, 0.78f, 1f));
            Burst(flash, ground + Vector3.up * 60f * s, 900f * s, small ? 0.6f : 1.2f, new Color(1f, 0.6f, 0.25f, 0.55f));
            // 2. the dome burst: an additive hot core under a roiling fireball that cools to smoke
            for (int i = 0; i < N(70); i++)
            {
                Vector3 dir = HalfSphere(0.2f);
                float sp = Random.Range(25f, 120f) * s;
                fireGlow.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(ground + dir * 10f * s + Vector3.up * 8f * s), velocity = dir * sp, startSize = Random.Range(60f, 140f) * s, startLifetime = Random.Range(1.4f, 3f), rotation = Random.Range(0f, 360f) }, 1);
            }
            for (int i = 0; i < N(60); i++)
            {
                Vector3 dir = HalfSphere(0.25f);
                fireBody.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(ground + dir * 14f * s + Vector3.up * 8f * s), velocity = dir * Random.Range(20f, 95f) * s, startSize = Random.Range(90f, 190f) * s, startLifetime = Random.Range(4f, small ? 5f : 7.5f), rotation = Random.Range(0f, 360f), startColor = new Color(shade, shade, shade, 1f) }, 1);
            }
            // 3. the shock skirt: a dust wall running along the ground, and the lifted debris behind it
            for (int i = 0; i < N(170); i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float sp = Random.Range(120f, 240f) * s;
                Color dust = Color.Lerp(new Color(0.55f, 0.46f, 0.35f), new Color(0.4f, 0.37f, 0.33f), Random.value) * shade;
                dust.a = 1f;
                skirt.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(ground + dir * Random.Range(6f, 30f) * s + Vector3.up * Random.Range(3f, 25f) * s), velocity = dir * sp + Vector3.up * Random.Range(2f, 14f) * s, startSize = Random.Range(70f, 130f) * s, startLifetime = Random.Range(small ? 4f : 8f, small ? 6f : 13f), rotation = Random.Range(0f, 360f), startColor = dust }, 1);
            }
            for (int i = 0; i < (small ? 0 : 90); i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Color dust = Color.Lerp(new Color(0.5f, 0.42f, 0.32f), new Color(0.3f, 0.28f, 0.26f), Random.value) * shade;
                dust.a = 1f;
                lift.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(ground + dir * Random.Range(10f, 60f) + Vector3.up * 10f), velocity = dir * Random.Range(40f, 110f) + Vector3.up * Random.Range(30f, 75f), startSize = Random.Range(60f, 120f), startLifetime = Random.Range(12f, 20f), rotation = Random.Range(0f, 360f), startColor = dust }, 1);
            }
            for (int i = 0; i < (small ? 0 : 36); i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                haze.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(ground + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(0f, 350f) + Vector3.up * Random.Range(8f, 40f)), velocity = Random.insideUnitSphere * 3f, startSize = Random.Range(260f, 420f), startLifetime = Random.Range(30f, 45f), rotation = Random.Range(0f, 360f), startColor = new Color(shade * 0.62f, shade * 0.56f, shade * 0.5f, 1f) }, 1);
            }
            // 4. the pressure ring and the condensation ring that races it
            Ring(0.0f, 2700f * s, 3.8f, new Color(0.9f, 0.82f, 0.7f, 0.5f), CineFx.RingAlpha);
            if (!small) Ring(0.12f, 3400f, 4.6f, new Color(0.95f, 0.97f, 1f, 0.55f), CineFx.RingAdd);
            // 5. ejecta: stretched embers on ballistic arcs and dark chunks
            for (int i = 0; i < N(130); i++)
            {
                Vector3 dir = HalfSphere(0.35f);
                embers.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(ground + Vector3.up * 8f), velocity = dir * Random.Range(70f, 260f) * s, startSize = Random.Range(1.6f, 4.5f), startLifetime = Random.Range(3f, 6.5f) }, 1);
            }
            for (int i = 0; i < N(50); i++)
            {
                Vector3 dir = HalfSphere(0.5f);
                chunks.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(ground + Vector3.up * 8f), velocity = dir * Random.Range(50f, 190f) * s, startSize = Random.Range(5f, 12f) * s, startLifetime = Random.Range(3f, 6f), rotation = Random.Range(0f, 360f), startColor = new Color(0.1f, 0.09f, 0.08f, 1f) }, 1);
            }
            // 6. lights, sound, ground shake
            flashLight.enabled = true; glowLight.enabled = true;
            flashLight.transform.position = ground + Vector3.up * 30f;
            glowLight.transform.position = ground + Vector3.up * 60f;
            float dist = CineFx.CameraDistance(ground);
            shakeAt = Mathf.Min(dist / 1800f, 4f); shakeStrength = s;
            if (boom == null) boom = BoscaliSummer.Core.Fx.SfxClips.Get("rod-impact") ?? BuildBoom();
            audioSource.clip = boom; audioSource.transform.position = ground;
            audioSource.PlayDelayed(Mathf.Clamp(dist / 700f, 0f, 7f));
        }

        private void Burst(ParticleSystem ps, Vector3 at, float size, float life, Color c) =>
            ps.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(at), startSize = size, startLifetime = life, startColor = c }, 1);

        private void Ring(float delay, float diameter, float life, Color c, Material m)
        {
            // ring particles are horizontal billboards, emitted once: size over life does the expanding
            (m == CineFx.RingAdd ? ringsAdd : rings).Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(ground + Vector3.up * (4f + delay * 40f)), startSize = diameter, startLifetime = life, startColor = c }, 1);
        }

        private static Vector3 HalfSphere(float minY)
        {
            Vector3 v = Random.onUnitSphere;
            v.y = Mathf.Abs(v.y) * (1f - minY) + minY;
            return v.normalized;
        }

        private void Build()
        {
            if (flash != null) return;
            flash = CineFx.Layer(transform, "Flash", CineFx.Glow, 10);
            CineFx.Tint(flash, CineFx.Fade(0.02f, 0.2f));
            fireGlow = CineFx.Layer(transform, "Fire glow", CineFx.Glow, 90, ParticleSystemRenderMode.Billboard);
            CineFx.Drag(fireGlow, 0.025f); CineFx.Spin(fireGlow, 20f); CineFx.Grow(fireGlow, 0.4f, 1.5f, 0.3f);
            CineFx.Tint(fireGlow, CineFx.Ramp(new Color(1f, 0.95f, 0.8f, 0f), 0f, new Color(1f, 0.7f, 0.25f, 0.85f), 0.12f, new Color(1f, 0.32f, 0.05f, 0.5f), 0.55f, new Color(0.4f, 0.08f, 0.02f, 0f), 1f));
            fireBody = CineFx.Layer(transform, "Fireball body", CineFx.Smoke, 80, ParticleSystemRenderMode.Billboard, true);
            CineFx.Drag(fireBody, 0.02f); CineFx.Spin(fireBody, 25f); CineFx.Grow(fireBody, 0.5f, 1.8f, 0.4f); CineFx.Turbulence(fireBody, 10f, 0.06f);
            CineFx.Tint(fireBody, CineFx.Ramp(new Color(1f, 0.7f, 0.35f, 0f), 0f, new Color(1f, 0.45f, 0.12f, 0.9f), 0.1f, new Color(0.3f, 0.2f, 0.15f, 0.8f), 0.4f, new Color(0.12f, 0.11f, 0.1f, 0f), 1f));
            skirt = CineFx.Layer(transform, "Dust skirt", CineFx.Smoke, 200, ParticleSystemRenderMode.Billboard, true);
            CineFx.Drag(skirt, 0.012f, 2f); CineFx.Spin(skirt, 12f); CineFx.Grow(skirt, 0.5f, 1.9f, 0.5f); CineFx.Turbulence(skirt, 8f, 0.04f);
            Gradient dustFade = CineFx.Ramp(new Color(1f, 1f, 1f, 0f), 0f, new Color(1f, 1f, 1f, 0.72f), 0.05f, new Color(1f, 1f, 1f, 0.5f), 0.6f, new Color(1f, 1f, 1f, 0f), 1f);
            lift = CineFx.Layer(transform, "Debris cloud", CineFx.Smoke, 110, ParticleSystemRenderMode.Billboard, true);
            CineFx.Drag(lift, 0.008f, 3f); CineFx.Spin(lift, 10f); CineFx.Grow(lift, 0.6f, 2.4f, 0.5f); CineFx.Turbulence(lift, 12f, 0.05f);
            CineFx.Tint(skirt, dustFade); CineFx.Tint(lift, dustFade);
            haze = CineFx.Layer(transform, "Ground haze", CineFx.Smoke, 40, ParticleSystemRenderMode.Billboard, true);
            CineFx.Tint(haze, CineFx.Fade(0.12f, 0.6f, 0.28f)); CineFx.Spin(haze, 4f);
            rings = CineFx.Layer(transform, "Shock rings", CineFx.RingAlpha, 4, ParticleSystemRenderMode.HorizontalBillboard);
            var rs = rings.sizeOverLifetime; rs.enabled = true;
            rs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.03f), new Keyframe(0.1f, 0.4f), new Keyframe(0.45f, 0.82f), new Keyframe(1f, 1f)));
            CineFx.Tint(rings, CineFx.Fade(0.03f, 0.25f));
            ringsAdd = CineFx.Layer(transform, "Condensation ring", CineFx.RingAdd, 4, ParticleSystemRenderMode.HorizontalBillboard);
            var ra = ringsAdd.sizeOverLifetime; ra.enabled = true; ra.size = rs.size;
            CineFx.Tint(ringsAdd, CineFx.Fade(0.03f, 0.25f));
            embers = CineFx.Layer(transform, "Embers", CineFx.Glow, 150, ParticleSystemRenderMode.Stretch, false, 2f, 0.04f);
            var ge = embers.main; ge.gravityModifier = 1.4f;
            CineFx.Tint(embers, CineFx.Ramp(new Color(1f, 0.95f, 0.75f, 1f), 0f, new Color(1f, 0.55f, 0.15f, 1f), 0.25f, new Color(0.9f, 0.25f, 0.05f, 0.7f), 0.7f, new Color(0.3f, 0.05f, 0.02f, 0f), 1f));
            chunks = CineFx.Layer(transform, "Debris chunks", CineFx.Smoke, 60, ParticleSystemRenderMode.Billboard, true);
            var gc = chunks.main; gc.gravityModifier = 1.2f; CineFx.Spin(chunks, 200f);
            CineFx.Tint(chunks, CineFx.Fade(0.02f, 0.75f, 0.9f));
            // The stem slows to a crawl within ~10 s (strong dampen) and thins out from mid-life: with a weak dampen and a near-black middle the puffs climbed
            // 600-900 m after the feed stopped and hung there as a detached black cloud for most of a minute.
            stem = CineFx.Layer(transform, "Smoke stem", CineFx.Smoke, 300, ParticleSystemRenderMode.Billboard, true);
            CineFx.Drag(stem, 0.012f, 6f); CineFx.Spin(stem, 8f); CineFx.Grow(stem, 0.55f, 2.8f, 0.55f); CineFx.Turbulence(stem, 16f, 0.07f);
            CineFx.Tint(stem, CineFx.Ramp(new Color(1f, 0.55f, 0.25f, 0f), 0f, new Color(0.6f, 0.45f, 0.32f, 0.85f), 0.06f, new Color(0.42f, 0.4f, 0.38f, 0.55f), 0.35f, new Color(0.55f, 0.54f, 0.53f, 0f), 1f));
            cap = CineFx.Layer(transform, "Smoke cap", CineFx.Smoke, 120, ParticleSystemRenderMode.Billboard, true);
            CineFx.Drag(cap, 0.018f, 1f); CineFx.Spin(cap, 6f); CineFx.Grow(cap, 0.7f, 2.4f, 0.5f); CineFx.Turbulence(cap, 10f, 0.05f);
            CineFx.Tint(cap, CineFx.Ramp(new Color(1f, 1f, 1f, 0f), 0f, new Color(0.55f, 0.52f, 0.5f, 0.7f), 0.1f, new Color(0.5f, 0.48f, 0.47f, 0.55f), 0.65f, new Color(0.5f, 0.5f, 0.5f, 0f), 1f));
            crater = CineFx.Layer(transform, "Crater fire", CineFx.Glow, 60);
            CineFx.Grow(crater, 1f, 0.5f, 0.3f);
            CineFx.Tint(crater, CineFx.Ramp(new Color(1f, 0.8f, 0.4f, 0f), 0f, new Color(1f, 0.5f, 0.12f, 0.8f), 0.15f, new Color(0.9f, 0.25f, 0.05f, 0.4f), 0.6f, new Color(0.3f, 0.05f, 0.02f, 0f), 1f));

            flashLight = MakeLight("Flash light", new Color(1f, 0.96f, 0.9f), 6000f);
            glowLight = MakeLight("Fire glow light", new Color(1f, 0.5f, 0.18f), 1800f);
            // its own child: Begin moves the speaker to the strike, and on the root it carried every particle (emitted in origin space) off by the strike's map coordinates
            var boomAt = new GameObject("Boom audio");
            boomAt.transform.SetParent(transform, false);
            audioSource = CineFx.Speaker(boomAt, null, 1500f, 90000f, 1f);
        }

        private Light MakeLight(string name, Color color, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = color; l.range = range; l.shadows = LightShadows.None; l.enabled = false;
            return l;
        }

        private void Update()
        {
            float age = Time.time - born;
            if (age >= life) { KineticRodStrikeVisuals.Release(this); return; }
            flashLight.intensity = 500f * scale * Mathf.Exp(-age * 7f);
            if (flashLight.intensity < 0.5f) flashLight.enabled = false;
            float glowFade = Mathf.Clamp01(1f - age / (Small ? 8f : 42f));
            glowLight.intensity = 55f * scale * glowFade * glowFade * (0.85f + 0.15f * Mathf.Sin(age * 23f) * Random.value);
            if (glowLight.intensity < 0.2f) glowLight.enabled = false;

            if (shakeStrength > 0f && age >= shakeAt)
            {
                float k = (age - shakeAt) / 3f;
                if (k < 1f) CineFx.Shake(ground, 40000f, (1f - k) * (1f - k) * shakeStrength); // a STARFALL rod shakes at its own scale
                else shakeStrength = 0f;
            }
            float shade = Mathf.Lerp(0.38f, 1f, day);
            if (age < StemSeconds && age >= nextStem)
            {
                nextStem = age + 0.07f;
                float fade = 1f - age / (StemSeconds * 1.3f);
                for (int i = 0; i < 2; i++)
                {
                    Vector2 c = Random.insideUnitCircle * 22f;
                    float s = shade * Random.Range(0.85f, 1.1f);
                    stem.Emit(new ParticleSystem.EmitParams
                    {
                        position = CineFx.ToSim(ground + new Vector3(c.x, 14f, c.y)),
                        velocity = new Vector3(Random.Range(-4f, 4f), Random.Range(48f, 80f) * fade, Random.Range(-4f, 4f)),
                        startSize = Random.Range(55f, 90f) * (0.8f + 0.4f * fade), startLifetime = Random.Range(22f, 30f), rotation = Random.Range(0f, 360f),
                        startColor = new Color(s, s, s, 1f)
                    }, 1);
                }
            }
            if (!capDone && age >= CapAt)
            {
                capDone = true;
                float top = 520f;
                for (int i = 0; i < 80; i++)
                {
                    float a = i * Mathf.PI * 2f / 80f + Random.Range(-0.05f, 0.05f);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float s = shade * Random.Range(0.8f, 1.05f);
                    cap.Emit(new ParticleSystem.EmitParams
                    {
                        position = CineFx.ToSim(ground + Vector3.up * (top + Random.Range(-60f, 60f)) + dir * Random.Range(10f, 60f)),
                        velocity = dir * Random.Range(22f, 45f) + Vector3.up * Random.Range(-1f, 9f),
                        startSize = Random.Range(120f, 200f), startLifetime = Random.Range(22f, 30f), rotation = Random.Range(0f, 360f),
                        startColor = new Color(s, s, s, 1f)
                    }, 1);
                }
            }
            if (age < CraterFire && age >= nextCrater)
            {
                nextCrater = age + 0.12f;
                Vector2 c = Random.insideUnitCircle * 38f;
                crater.Emit(new ParticleSystem.EmitParams
                {
                    position = CineFx.ToSim(ground + new Vector3(c.x, 3f, c.y)),
                    velocity = new Vector3(Random.Range(-2f, 2f), Random.Range(6f, 14f), Random.Range(-2f, 2f)),
                    startSize = Random.Range(10f, 24f) * (1f - age / (CraterFire * 1.2f)), startLifetime = Random.Range(1.2f, 2.2f)
                }, 1);
            }
        }

        private static AudioClip BuildBoom()
        {
            float lp = 0f;
            return CineFx.Clip("RodBoom", 10f, t =>
            {
                float crack = CineFx.Noise() * Mathf.Exp(-t * 55f) * 0.95f;
                float f = Mathf.Lerp(52f, 19f, Mathf.Clamp01(t / 5f));
                float thump = Mathf.Sin(6.2832f * f * t) * Mathf.Exp(-t * 0.75f) * 0.95f * Mathf.Clamp01(t * 30f);
                lp += (CineFx.Noise() - lp) * 0.04f;
                float roar = lp * 3.2f * Mathf.Clamp01(t * 8f) * Mathf.Exp(-t * 0.55f);
                float patter = Random.value > 0.9993f ? CineFx.Noise() * Mathf.Exp(-(t - 1f) * 0.3f) * (t > 1f ? 0.7f : 0f) : 0f;
                return crack + thump + roar + patter;
            });
        }
    }
}
