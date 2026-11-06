using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Condensation on the airframe (2026-11-06; neither Kelly's pack nor NoAtmoFX has it): a transonic vapour cone
    /// that blooms around an aircraft between Mach 0.93 and 1.06, and white vapour ribbons off both wingtips above
    /// about 3 G. Both need humid air (<c>humidity</c>, 0..1 from the dewpoint spread) and thin out with altitude.
    /// Client-local and cosmetic, for at most <see cref="MaxTracked"/> aircraft within <see cref="Reach"/>.
    /// </summary>
    internal sealed class Vapour
    {
        private const float Reach = 3500f, ConeLife = .09f, TipLife = 1.1f;
        private const int MaxTracked = 8;

        private sealed class Craft
        {
            public ParticleSystem Cone, Tips;
            public int Seen;
        }

        private readonly Dictionary<Aircraft, Craft> crafts = new Dictionary<Aircraft, Craft>();
        private readonly List<Aircraft> gone = new List<Aircraft>();
        private int frame;

        internal int Tracked => crafts.Count;
        /// <summary>Strongest cone and wingtip vapour this frame, after humidity (diagnostics).</summary>
        internal float PeakCone { get; private set; }
        internal float PeakTip { get; private set; }

        /// <summary>Mach bell for the cone: 0 below 0.93 and above 1.06, full near 0.99.</summary>
        internal static float ConeStrength(float mach) =>
            mach < .93f || mach > 1.06f ? 0f : Mathf.Sin(Mathf.PI * (mach - .93f) / .13f);

        /// <summary>Wingtip vapour from felt G: none under 3 G, full at 6 G.</summary>
        internal static float TipStrength(float g) => Mathf.InverseLerp(3f, 6f, g);

        internal void Tick(Vector3 eye, bool enabled, float humidity, float light)
        {
            frame++;
            PeakCone = PeakTip = 0f;
            if (enabled && humidity > .05f && Datum.origin != null)
            {
                List<Aircraft> all = UnitRegistry.allAircraft;
                for (int i = 0; all != null && i < all.Count; i++)
                {
                    Aircraft a = all[i];
                    if (a == null || a.disabled || (a.transform.position - eye).sqrMagnitude > Reach * Reach) continue;
                    float altitude = a.transform.position.GlobalY();
                    float thin = Mathf.InverseLerp(12000f, 3000f, altitude);
                    float sound = LevelInfo.GetSpeedOfSound(altitude);
                    float cone = sound > 1f ? ConeStrength(a.speed / sound) : 0f;
                    Pilot pilot = a.pilots != null && a.pilots.Length > 0 ? a.pilots[0] : null;
                    // Pilot.gForce is the kinematic acceleration along the jet's up axis (0 in level flight); add gravity's share for felt G.
                    float g = pilot != null ? pilot.gForce + a.transform.up.y : 1f;
                    float tips = TipStrength(g) * Mathf.InverseLerp(60f, 120f, a.speed);
                    float wet = humidity * Mathf.Lerp(.35f, 1f, thin);
                    PeakCone = Mathf.Max(PeakCone, cone * wet);
                    PeakTip = Mathf.Max(PeakTip, tips * wet);
                    if (cone * wet < .03f && tips * wet < .03f) continue;
                    if (!crafts.TryGetValue(a, out Craft c))
                    {
                        if (crafts.Count >= MaxTracked) continue;
                        crafts[a] = c = Make(a);
                        if (c == null) { crafts.Remove(a); continue; }
                    }
                    c.Seen = frame;
                    Feed(a, c, cone * wet, tips * wet, light);
                }
            }
            gone.Clear();
            foreach (var kv in crafts)
            {
                if (kv.Key != null && kv.Value.Seen == frame) continue;
                Stop(kv.Value.Cone);
                if (kv.Key == null || (kv.Value.Cone == null || kv.Value.Cone.particleCount == 0) &&
                    (kv.Value.Tips == null || kv.Value.Tips.particleCount == 0)) gone.Add(kv.Key);
            }
            foreach (Aircraft a in gone) { Free(crafts[a]); crafts.Remove(a); }
        }

        private static void Feed(Aircraft a, Craft c, float cone, float tips, float light)
        {
            var emission = c.Cone.emission;
            emission.rateOverTime = cone > .03f ? 1600f + 2400f * cone : 0f;
            var main = c.Cone.main;
            main.startColor = new Color(light, light, light, .32f * Mathf.Clamp01(cone * 2.2f));
            if (tips <= .03f) return;
            float half = a.definition != null && a.definition.width > 2f ? a.definition.width * .5f : a.maxRadius * .8f;
            Transform t = a.transform, datum = Datum.origin;
            var colour = new Color(light, light, light, .85f * Mathf.Clamp01(tips * 2f));
            for (int side = -1; side <= 1; side += 2)
                c.Tips.Emit(new ParticleSystem.EmitParams
                {
                    position = datum.InverseTransformPoint(t.position + t.right * (side * half * .97f) - t.forward * 1.5f),
                    startColor = colour, startSize = 1.3f, startLifetime = TipLife, applyShapeToPosition = false,
                }, 1);
        }

        private static Craft Make(Aircraft a)
        {
            Material puff = SmokeFx.Puff;
            if (puff == null || Datum.origin == null) return null;
            float r = Mathf.Max(3f, a.maxRadius * .55f);

            // Cone: a shell blown back from a ring around the fuselage, in the aircraft's own space.
            var coneGo = new GameObject("BoscaliVapourCone") { hideFlags = HideFlags.DontSave };
            coneGo.transform.SetParent(a.transform, false);
            coneGo.transform.localRotation = Quaternion.LookRotation(Vector3.back);
            ParticleSystem cone = coneGo.AddComponent<ParticleSystem>();
            cone.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = cone.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = ConeLife;
            main.startSpeed = r * .9f / ConeLife;
            main.startSize = new ParticleSystem.MinMaxCurve(r * .7f, r * 1.1f);
            main.maxParticles = 1200;
            var shape = cone.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 66f;
            shape.radius = r * .45f;
            shape.radiusThickness = 0f;
            var emission = cone.emission;
            emission.rateOverTime = 0f;
            var fade = cone.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .25f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            Quiet(coneGo.GetComponent<ParticleSystemRenderer>(), puff, false);
            cone.Play();

            // Wingtips: two ribbons (particles alternate left/right) in Datum space, so they stay where they were laid.
            var tipGo = new GameObject("BoscaliWingtipVapour") { hideFlags = HideFlags.DontSave };
            ParticleSystem tips = tipGo.AddComponent<ParticleSystem>();
            tips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var tm = tips.main;
            tm.playOnAwake = false;
            tm.loop = true;
            tm.simulationSpace = ParticleSystemSimulationSpace.Custom;
            tm.customSimulationSpace = Datum.origin;
            tm.startSpeed = 0f;
            tm.maxParticles = 400;
            var te = tips.emission;
            te.rateOverTime = 0f;
            var ts = tips.shape;
            ts.enabled = false;
            var grow = tips.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 3.5f)));
            var tf = tips.colorOverLifetime;
            tf.enabled = true;
            var tg = new Gradient();
            tg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.5f, .4f), new GradientAlphaKey(0f, 1f) });
            tf.color = tg;
            var trails = tips.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.Ribbon;
            trails.ribbonCount = 2;
            trails.sizeAffectsWidth = true;
            trails.inheritParticleColor = true;
            trails.dieWithParticles = true;
            trails.worldSpace = false;
            Quiet(tipGo.GetComponent<ParticleSystemRenderer>(), Contrails.Material() ?? puff, true);
            tips.Play();
            return new Craft { Cone = cone, Tips = tips };
        }

        private static void Quiet(ParticleSystemRenderer r, Material m, bool ribbon)
        {
            if (ribbon) { r.renderMode = ParticleSystemRenderMode.None; r.trailMaterial = m; }
            else r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static void Stop(ParticleSystem ps)
        {
            if (ps == null) return;
            var e = ps.emission;
            e.rateOverTime = 0f;
        }

        private static void Free(Craft c)
        {
            if (c.Cone != null) Object.Destroy(c.Cone.gameObject);
            if (c.Tips != null) Object.Destroy(c.Tips.gameObject);
        }

        internal void Reset()
        {
            foreach (Craft c in crafts.Values) Free(c);
            crafts.Clear();
        }
    }
}
