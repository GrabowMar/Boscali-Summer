using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// EMP, after the real high-altitude bursts (Starfish Prime 1962, the Soviet K tests; reworked 2026-10-28): an airburst 30 km over the mark. A flash brighter
    /// than the sun lights the whole sky for an instant; the fireball, far larger than at sea level in the thin air, goes white to orange to a dull red ember while
    /// it rises and is drawn out along the geomagnetic field; then the air itself glows, green for the first seconds (oxygen, 557.7 nm) and blood red after (630 nm,
    /// which lingers), with auroral curtains along the field lines leaning south. No blast reaches the ground and nothing is heard but electronics: the pulse travels
    /// at light speed, so every unit under it is struck at the same instant (no racing front), hardest in the arc south of ground zero and least just north of it
    /// (<see cref="SupportEffectPolicy.EmpField"/>). Struck units spit an arc and go dark; a few more fail over the next seconds as their power lines (E3) give out,
    /// and some hardened ones ride it out. The sky is one mesh of glows and rays rebuilt for the camera each frame on a 25 km shell (direction and angular size are
    /// what the eye reads; the burst lies past the 80 km far plane for much of the footprint), unfogged, before the cloud composite. The cockpit upset is
    /// <see cref="CockpitEmpDisruption"/>, triggered once by the detonation; the radar blackout belongs to EmpAction.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the floating-origin shift (CameraStateManager.LateUpdate, order 2), or the sky mesh flashes on a shift
    internal sealed class EmpVisualEffect : MonoBehaviour
    {
        private const int MaxVictims = 24, FlickerLights = 6, Folds = 4, RaysPerFold = 7, Rays = Folds * RaysPerFold;
        private const float Shell = 25000f, Life = SupportEffectPolicy.EmpDuration + 15f;
        // Up along the geomagnetic field at a mid-northern dip of 60 degrees: the field lines climb toward the south (north is +Z).
        private static readonly Vector3 FieldUp = new Vector3(0f, 0.866f, -0.5f);
        private static readonly Color Green = new Color(0.3f, 1f, 0.5f), Crimson = new Color(1f, 0.12f, 0.2f);
        private static readonly List<EmpVisualEffect> active = new List<EmpVisualEffect>(2);
        private static readonly Stack<EmpVisualEffect> free = new Stack<EmpVisualEffect>(2);
        private static AudioClip crack, hum;
        private static Material skyMaterial, skyOverMaterial;

        private ParticleSystem arcs, sparks, wisps;
        private Light flash;
        private readonly Light[] flickers = new Light[FlickerLights];
        private readonly float[] flickerUntil = new float[FlickerLights];
        private AudioSource crackSource, humSource;
        private readonly GlobalPosition[] victims = new GlobalPosition[MaxVictims];
        private readonly float[] victimAt = new float[MaxVictims];
        private int victimCount, flickerCursor;
        private GlobalPosition burst;
        private float born, radius, fireball;
        private readonly GlobalPosition[] rayFoot = new GlobalPosition[Rays];
        private readonly Vector4[] rayShape = new Vector4[Rays]; // x length along the field, y width, z start (s), w shimmer rate
        private readonly float[] rayPhase = new float[Rays];

        private GameObject skyObject;
        private Mesh sky;
        private readonly List<Vector3> verts = new List<Vector3>(256);
        private readonly List<Color> colours = new List<Color>(256);
        private readonly List<Vector2> uvs = new List<Vector2>(256);
        private readonly List<int> tris = new List<int>(512), trisOver = new List<int>(24);
        private Vector3 camPos, camRight, camUp;

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
            if (skyMaterial != null) Destroy(skyMaterial);
            if (skyOverMaterial != null && skyOverMaterial != skyMaterial) Destroy(skyOverMaterial);
            skyMaterial = skyOverMaterial = null; // their texture is CineFx's, reset beside it
        }

        private void Build()
        {
            if (sparks != null) return;
            arcs = CineFx.Layer(transform, "Arc flash", CineFx.Glow, 48);
            CineFx.Tint(arcs, CineFx.Fade(0.02f, 0.25f));
            sparks = CineFx.Layer(transform, "Dying electronics", CineFx.Glow, 160, ParticleSystemRenderMode.Stretch, false, 2f, 0.05f);
            var gs = sparks.main; gs.gravityModifier = 1.2f;
            CineFx.Tint(sparks, CineFx.Ramp(new Color(0.85f, 0.95f, 1f, 1f), 0f, new Color(1f, 0.8f, 0.45f, 1f), 0.25f, new Color(1f, 0.45f, 0.15f, 0.6f), 0.7f, new Color(0.3f, 0.1f, 0.05f, 0f), 1f));
            wisps = CineFx.Layer(transform, "Burnt-out wisp", CineFx.Smoke, 40, ParticleSystemRenderMode.Billboard, true);
            CineFx.Grow(wisps, 0.4f, 1.8f); CineFx.Spin(wisps, 20f); CineFx.Turbulence(wisps, 2f, 0.1f);
            CineFx.Tint(wisps, CineFx.Ramp(new Color(1f, 1f, 1f, 0f), 0f, new Color(0.3f, 0.3f, 0.3f, 0.35f), 0.12f, new Color(0.28f, 0.28f, 0.28f, 0.22f), 0.6f, new Color(0.3f, 0.3f, 0.3f, 0f), 1f));
            // A directional light: a point light 30 km up lights nothing under URP's inverse square, and the flash lights the whole sky anyway.
            var fl = new GameObject("Flash light"); fl.transform.SetParent(transform, false);
            flash = fl.AddComponent<Light>();
            flash.type = LightType.Directional; flash.shadows = LightShadows.None; flash.enabled = false;
            for (int i = 0; i < FlickerLights; i++)
            {
                var go = new GameObject("Dying light"); go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point; l.range = 55f; l.color = new Color(0.75f, 0.88f, 1f); l.shadows = LightShadows.None; l.enabled = false;
                flickers[i] = l;
            }
            // What a listener under the pulse hears is their own electronics: one crack of static, then the hum of the grid dying (no delay, no boom).
            if (crack == null) crack = CineFx.Clip("EmpCrack", 0.9f, t => CineFx.Noise() * (Mathf.Exp(-t * 40f) + 0.3f * Mathf.Exp(-t * 5f) * (Random.value > 0.93f ? 1f : 0.12f)) * 0.95f);
            if (hum == null)
                hum = CineFx.Clip("EmpGridDie", 4.5f, t =>
                {
                    float phase = 6.2832f * (18f * t + 38.4f * (1f - Mathf.Exp(-t / 1.2f)));
                    float buzz = Mathf.Sin(phase) + 0.5f * Mathf.Sin(3f * phase) + 0.3f * Mathf.Sin(5f * phase);
                    return (buzz * 0.4f + CineFx.Noise() * 0.06f) * Mathf.Clamp01(t * 20f) * Mathf.Exp(-t * 0.9f);
                });
            crackSource = Voice(crack);
            humSource = Voice(hum);
            skyObject = new GameObject("BoscaliSummer.EMP sky");
            sky = new Mesh { name = "EmpSky" };
            sky.MarkDynamic();
            skyObject.AddComponent<MeshFilter>().sharedMesh = sky;
            var r = skyObject.AddComponent<MeshRenderer>();
            SkyMaterials();
            r.sharedMaterials = new[] { skyOverMaterial, skyMaterial };
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private AudioSource Voice(AudioClip clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip; s.spatialBlend = 0f; s.playOnAwake = false;
            return s;
        }

        // The weather module's unfogged sky shaders when its bundle is loaded, at their queue: additive (Boscali/SkyGlow, the aurora's) for light, alpha-over
        // (Boscali/SkyGlowOver) for the fireball's core, which must hide the blue day sky behind it. Without the bundle both fall back to the fogged particle glow.
        private static void SkyMaterials()
        {
            if (skyMaterial != null) return;
            Texture glow = CineFx.Glow.GetTexture("_BaseMap");
            skyMaterial = Sky(Loaded("Boscali/SkyGlow"), glow, "BoscaliEmpSky");
            Shader over = Loaded("Boscali/SkyGlowOver");
            skyOverMaterial = over != null ? Sky(over, glow, "BoscaliEmpSkyOver") : skyMaterial;
        }

        // Shader.Find does not see shaders that only live in a loaded asset bundle (live 2026-10-28: both fell back to the fogged particle glow, and the day
        // fog colour added to every glow turned the whole effect white by day), so look through the loaded shaders. Once per scene.
        private static Shader Loaded(string name)
        {
            Shader found = Shader.Find(name);
            if (found != null) return found;
            foreach (Shader s in Resources.FindObjectsOfTypeAll<Shader>())
                if (s != null && s.name == name) return s;
            return null;
        }

        private static Material Sky(Shader shader, Texture glow, string name)
        {
            Material m;
            if (shader != null)
            {
                m = new Material(shader) { name = name };
                m.SetTexture("_MainTex", glow);
                m.SetFloat("_Intensity", 1f);
            }
            else m = new Material(CineFx.Glow) { name = name };
            m.renderQueue = 2996; // after the stars, before the cloud composite, which then hides it behind cloud
            return m;
        }

        private void Begin(Vector3 point, float range)
        {
            Build();
            radius = range; born = Time.time; flickerCursor = 0;
            burst = point.ToGlobalPosition();
            // A megaton-class fireball is about a kilometre across at sea level and grows as the air thins; at 30 km a few km, larger for a bigger burst.
            fireball = Mathf.Clamp(2600f * Mathf.Pow(range / 12000f, 0.33f), 1800f, 5200f);
            Vector3 ground = point;
            if (Physics.Raycast(point, Vector3.down, out RaycastHit hit, 60000f, PhysicsLayers.StaticsMask)) ground = hit.point; else ground.y = Datum.LocalSeaY;
            foreach (var ps in new[] { arcs, sparks, wisps }) { ps.Clear(); ps.Play(); }
            for (int i = 0; i < FlickerLights; i++) { flickers[i].enabled = false; flickerUntil[i] = 0f; }
            transform.localPosition = Vector3.zero;
            flash.enabled = true;
            skyObject.SetActive(true);

            // Auroral curtains: four folds running east-west south of ground zero (where the field lines climb), seven rays each, feet 50-62 km up, tops 95-130 km.
            GlobalPosition groundGlobal = ground.ToGlobalPosition();
            for (int f = 0; f < Folds; f++)
            {
                float z = -range * (0.1f + 0.28f * f) + Random.Range(-0.08f, 0.08f) * range;
                float wiggle = Random.Range(0f, 6.283f);
                for (int j = 0; j < RaysPerFold; j++)
                {
                    int i = f * RaysPerFold + j;
                    float u = (j + Random.Range(-0.3f, 0.3f)) / (RaysPerFold - 1) * 2f - 1f;
                    float x = u * range * Mathf.Lerp(0.9f, 1.4f, f / (Folds - 1f));
                    float foot = Random.Range(50000f, 62000f), top = Random.Range(95000f, 130000f);
                    rayFoot[i] = groundGlobal + new Vector3(x, foot - groundGlobal.y, z + Mathf.Sin(u * 2.4f + wiggle) * range * 0.12f);
                    rayShape[i] = new Vector4((top - foot) / FieldUp.y, Random.Range(1200f, 3200f), Random.Range(2.5f, 9f) + f * 0.8f, Random.Range(0.5f, 1.8f));
                    rayPhase[i] = Random.Range(0f, 6.283f);
                }
            }

            // The units under the pulse: struck at once where the field is strong, a few later as their power fails, hardened ones not at all.
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
                    if (d.x * d.x + d.z * d.z > sq) continue;
                    float field = SupportEffectPolicy.EmpField(d.x, d.z, radius);
                    float at = Random.value < 0.35f + 0.6f * field ? Random.Range(0f, 0.12f) : Random.value < 0.5f ? Random.Range(1.2f, 7f) : -1f;
                    if (at < 0f) continue;
                    victims[victimCount] = u.transform.position.ToGlobalPosition();
                    victimAt[victimCount++] = at;
                }
            }

            Camera cam = CineFx.Cam();
            float heard = 0f;
            if (cam != null)
            {
                Vector3 off = cam.transform.position - ground;
                float across = new Vector2(off.x, off.z).magnitude;
                heard = Mathf.Max(SupportEffectPolicy.EmpField(off.x, off.z, radius), 0.2f * Mathf.Clamp01(1f - (across - radius) / (radius * 0.6f)));
            }
            if (heard > 0.02f)
            {
                crackSource.volume = 0.9f * heard; crackSource.Play();
                humSource.volume = 0.6f * heard; humSource.PlayDelayed(0.15f);
            }
        }

        private void LateUpdate()
        {
            float age = Time.time - born;
            if (age >= Life)
            {
                active.Remove(this);
                flash.enabled = false;
                if (skyObject != null) skyObject.SetActive(false);
                gameObject.SetActive(false);
                free.Push(this);
                return;
            }
            for (int i = 0; i < victimCount; i++)
            {
                if (victimAt[i] < 0f || age < victimAt[i]) continue;
                victimAt[i] = -1f;
                Kill(victims[i].ToLocalPosition(), age);
            }
            for (int i = 0; i < FlickerLights; i++)
            {
                if (!flickers[i].enabled) continue;
                if (age > flickerUntil[i]) flickers[i].enabled = false;
                else flickers[i].intensity = Random.value > 0.35f ? Random.Range(2f, 6f) : 0f;
            }
            Camera cam = CineFx.Cam();
            if (cam == null) return;
            Vector3 b = burst.ToLocalPosition();
            float fade = 1f - Smooth(Life - 12f, Life, age);
            // The flash, then the faint red light of the glowing sky (it only shows at night).
            float white = 5f * Mathf.Exp(-age / 0.09f) + 0.6f * Mathf.Exp(-age / 0.8f), red = 0.12f * Smooth(0.5f, 6f, age) * fade * (1f - Daylight());
            flash.transform.rotation = Quaternion.LookRotation(cam.transform.position - b);
            flash.intensity = white + red;
            flash.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.85f, 0.9f, 1f), white / Mathf.Max(1e-4f, white + red));
            DrawSky(cam, b, age, fade);
        }

        private void DrawSky(Camera cam, Vector3 b, float age, float fade)
        {
            camPos = cam.transform.position; camRight = cam.transform.right; camUp = cam.transform.up;
            verts.Clear(); colours.Clear(); uvs.Clear(); tris.Clear(); trisOver.Clear();
            float dist = Mathf.Max(1000f, Vector3.Distance(camPos, b));
            // The air's own glow is faint against a day sky (Starfish was a night shot), vivid at night.
            float day = Daylight(), airlight = Mathf.Lerp(1f, 0.2f, day);

            // 1. The flash: a hard white star a few degrees wide in a blue-white bloom, and for an instant the whole sky.
            Glow(b, dist * 0.07f, Color.white * (40f * Mathf.Exp(-age / 0.12f)));
            Glow(b, dist * 0.4f, new Color(0.75f, 0.85f, 1f) * (5f * Mathf.Exp(-age / 0.35f)));
            Glow(b, dist * 2.5f, new Color(0.6f, 0.72f, 1f) * (1.2f * Mathf.Exp(-age / 0.25f)));

            // 2. The fireball: full size in about a second, rising, white to orange to a red ember, drawn out along the field. Its core hides the sky behind it
            // (alpha-over, so it stays orange against a day sky; added light washed it white); its glow and the two lobes along the field are added on top.
            float grow = 1f - Mathf.Exp(-age / 0.45f);
            float r = fireball * (0.15f + 0.85f * Mathf.Sqrt(grow)) * (1f + age * 0.012f);
            Vector3 c = b + Vector3.up * (1800f * (1f - Mathf.Exp(-age / 14f)));
            Color heat = Heat(age) * fade;
            float stretch = Smooth(1f, 10f, age);
            Color core = heat * 0.55f; // 0.8 tonemapped to cream by day
            core.a = 0.92f * Smooth(0.1f, 0.6f, age) * fade;
            Glow(c, r * 2.4f, core, true);
            Glow(c, r * 4f, heat * Mathf.Lerp(0.5f, 0.18f, day));
            Glow(c + FieldUp * (r * (0.9f + 1.6f * stretch)), r * (2.6f + 1.4f * stretch), heat * 0.45f);
            Glow(c - FieldUp * (r * (0.9f + 1.6f * stretch)), r * (2.6f + 1.4f * stretch), heat * 0.45f);

            // 3. The air glows: green for the first seconds, then the long blood-red glow over a sky-wide patch.
            float green = Smooth(0.2f, 1.2f, age) * Mathf.Exp(-Mathf.Max(0f, age - 1.2f) / 2.5f);
            Glow(b + Vector3.up * 12000f, radius * 2.2f, Green * (0.55f * green * airlight));
            float glow = Smooth(1.5f, 9f, age) * fade * airlight;
            Glow(b + Vector3.up * 30000f, radius * 3.2f, Crimson * (0.35f * glow));
            Glow(b + Vector3.up * 55000f, radius * 5f, new Color(0.9f, 0.08f, 0.22f) * (0.22f * glow));

            // 4. Auroral rays along the field lines: green at the foot, crimson above, fading out at the top, each shimmering on its own.
            for (int i = 0; i < Rays; i++)
            {
                Vector4 s = rayShape[i];
                float on = Smooth(s.z, s.z + 3f, age) * fade;
                if (on <= 0.001f) continue;
                float shimmer = 0.55f + 0.45f * Mathf.Sin(age * s.w + rayPhase[i]) * Mathf.Sin(age * s.w * 0.37f + rayPhase[i] * 1.7f);
                Vector3 foot = rayFoot[i].ToLocalPosition();
                float k = on * shimmer * airlight;
                Ray(foot, foot + FieldUp * s.x, s.y, Green * (0.38f * k), Color.Lerp(Green, Crimson, 0.85f) * (0.32f * k), Color.clear);
            }

            sky.Clear();
            sky.SetVertices(verts);
            sky.SetColors(colours);
            sky.SetUVs(0, uvs);
            sky.subMeshCount = 2;
            sky.SetTriangles(trisOver, 0, false);
            sky.SetTriangles(tris, 1, false);
            sky.bounds = new Bounds(camPos, Vector3.one * (Shell * 4f));
        }

        // The fireball's light by age: white, yellow by 1.5 s, orange by 5, red by 14, an ember after; very bright at first, then a long dim glow.
        private static Color Heat(float age)
        {
            Color white = new Color(1f, 0.96f, 0.9f), yellow = new Color(1f, 0.82f, 0.45f), orange = new Color(1f, 0.45f, 0.12f),
                red = new Color(0.85f, 0.12f, 0.05f), ember = new Color(0.4f, 0.03f, 0.05f);
            Color tint = age < 1.5f ? Color.Lerp(white, yellow, age / 1.5f)
                : age < 5f ? Color.Lerp(yellow, orange, (age - 1.5f) / 3.5f)
                : age < 14f ? Color.Lerp(orange, red, (age - 5f) / 9f)
                : Color.Lerp(red, ember, (age - 14f) / 16f);
            return tint * (14f * Mathf.Exp(-age / 0.3f) + 3.5f * Mathf.Exp(-age / 3f) + 0.9f * Mathf.Exp(-age / 14f));
        }

        // A point seen from the camera, brought onto the shell if it lies beyond it; k is the factor its sizes take.
        private Vector3 OnShell(Vector3 world, out float k)
        {
            Vector3 d = world - camPos;
            float dist = d.magnitude;
            k = dist > Shell ? Shell / dist : 1f;
            return camPos + d * k;
        }

        // A camera-facing glow of the given true size: light added to the sky, or (over) a glow that hides it by the colour's alpha.
        private void Glow(Vector3 world, float size, Color c, bool over = false)
        {
            if (c.r + c.g + c.b <= 0.003f || size <= 0f || (over && c.a <= 0.001f)) return;
            Vector3 p = OnShell(world, out float k);
            float h = size * k * 0.5f;
            Vector3 x = camRight * h, y = camUp * h;
            int i = verts.Count;
            verts.Add(p - x - y); verts.Add(p + x - y); verts.Add(p + x + y); verts.Add(p - x + y);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
            if (!over) c.a = 1f;
            for (int n = 0; n < 4; n++) colours.Add(c);
            List<int> into = over ? trisOver : tris;
            into.Add(i); into.Add(i + 1); into.Add(i + 2); into.Add(i); into.Add(i + 2); into.Add(i + 3);
        }

        // A camera-facing ribbon from a to b, soft across (the glow texture's middle row): it fades in over its first eighth (a flat end read as a bar
        // up close), then runs ca at the foot, cm at mid length and cb at the top.
        private static readonly float[] RayRows = { 0f, 0.12f, 0.5f, 1f };

        private void Ray(Vector3 a, Vector3 b, float width, Color ca, Color cm, Color cb)
        {
            Vector3 pa = OnShell(a, out float ka), pb = OnShell(b, out float kb);
            Vector3 side = Vector3.Cross(pb - pa, (pa + pb) * 0.5f - camPos).normalized;
            if (side.sqrMagnitude < 0.5f) return;
            int i = verts.Count;
            for (int n = 0; n < RayRows.Length; n++)
            {
                float f = RayRows[n];
                Vector3 p = Vector3.Lerp(pa, pb, f);
                float w = width * Mathf.Lerp(ka, kb, f) * 0.5f;
                Color c = n == 0 ? Color.clear : n == 1 ? ca : n == 2 ? cm : cb;
                c.a = 1f;
                verts.Add(p - side * w); verts.Add(p + side * w);
                uvs.Add(new Vector2(0f, 0.5f)); uvs.Add(new Vector2(1f, 0.5f));
                colours.Add(c); colours.Add(c);
            }
            for (int s = 0; s < RayRows.Length - 1; s++)
            {
                int o = i + s * 2;
                tris.Add(o); tris.Add(o + 1); tris.Add(o + 3); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
            }
        }

        private void Kill(Vector3 at, float age)
        {
            Vector3 p = at + Vector3.up * 3f;
            arcs.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(p), startSize = Random.Range(6f, 12f), startLifetime = Random.Range(0.12f, 0.25f), startColor = new Color(0.7f, 0.85f, 1f, 1f) }, 1);
            for (int i = 0; i < 8; i++)
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = CineFx.ToSim(p), velocity = Random.onUnitSphere * Random.Range(3f, 12f) + Vector3.up * Random.Range(3f, 8f),
                    startSize = Random.Range(0.25f, 0.7f), startLifetime = Random.Range(0.4f, 1.1f)
                }, 1);
            wisps.Emit(new ParticleSystem.EmitParams { position = CineFx.ToSim(p), velocity = Vector3.up * 1.5f + CineFx.Wind() * 0.3f, startSize = 2.5f, startLifetime = Random.Range(3f, 5f), rotation = Random.Range(0f, 360f) }, 1);
            int idx = flickerCursor++ % FlickerLights;
            Light l = flickers[idx];
            l.transform.position = p + Vector3.up * 2f;
            l.enabled = true; flickerUntil[idx] = age + Random.Range(0.3f, 0.8f);
        }

        // 0 at night, 1 by day, from the sun's height: the ambient light (CineFx.Day01) read near zero at noon in the sim, so the day sky got the night's full glow.
        private static float Daylight()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            return Smooth(-0.05f, 0.15f, level != null && level.sun != null ? -level.sun.transform.forward.y : 1f);
        }

        // The shader smoothstep (Mathf.SmoothStep is an eased lerp, not this).
        private static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        private void OnDestroy()
        {
            active.Remove(this);
            if (skyObject != null) Destroy(skyObject);
            if (sky != null) Destroy(sky);
        }
    }
}
