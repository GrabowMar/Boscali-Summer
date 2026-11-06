using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Sky optics (2026-11-06): the 22 degree ice halo and two sundogs under thin cloud, a primary and faint secondary rainbow
    /// opposite the sun when rain lies ahead and the sun is out, a glory round the antisolar point when you fly above a cloud
    /// deck, and the odd meteor on a clear night. Each is one additive quad with a texture baked once, placed along its sky
    /// direction <see cref="Range"/> in front of the camera and faded in and out over a few seconds; a hidden quad's renderer
    /// is off. Driven by the Weather module's sky sample and the sun; client-local and cosmetic.
    /// </summary>
    internal sealed class SkyOptics
    {
        private const float Range = 1500f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        private sealed class Glyph
        {
            public GameObject Go;
            public Material Mat;
            public MeshRenderer Renderer;
            public float Level;
        }

        private Glyph halo, dogLeft, dogRight, bow, glory, meteor;
        private float nextMeteor = 20f, meteorAge = -1f;
        private Vector3 meteorDir, meteorMotion;

        internal string Showing =>
            (halo?.Level > .02f ? "halo " : "") + (dogLeft?.Level > .02f ? "sundogs " : "") + (bow?.Level > .02f ? "rainbow " : "") +
            (glory?.Level > .02f ? "glory " : "") + (meteorAge >= 0f ? "meteor" : "");

        /// <param name="sky">Weather at the camera; <paramref name="rainAhead"/> is rain 4 km toward the antisolar point.</param>
        internal void Tick(Camera cam, bool enabled, bool haveSky, WeatherViewSample sky, float rainAhead, float cameraAltitude, float dt)
        {
            Light sun = RenderSettings.sun;
            if (!enabled || cam == null || sun == null || !haveSky) { Fade(0f, dt); return; }
            if (halo == null && !Build()) return;
            Vector3 eye = cam.transform.position, toSun = -sun.transform.forward;
            float elevation = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;
            bool day = elevation > 2f;

            // Halo and sundogs: thin cloud between you and a sun up in the sky (cirrostratus), not a solid deck.
            float thin = day ? Bell(sky.Cover, .15f, .5f, .85f) : 0f;
            Ease(halo, thin * .55f, dt);
            Place(halo, eye, toSun, 25f, 1f);
            float dogs = thin * Mathf.InverseLerp(30f, 8f, elevation);   // sundogs need a low sun
            Ease(dogLeft, dogs * .8f, dt);
            Ease(dogRight, dogs * .8f, dt);
            Vector3 flat = new Vector3(toSun.x, 0f, toSun.z).normalized;
            float spread = 22f / Mathf.Max(.35f, Mathf.Cos(elevation * Mathf.Deg2Rad));
            Vector3 up = Vector3.up * toSun.y;
            Place(dogLeft, eye, (Quaternion.AngleAxis(-spread, Vector3.up) * flat * Mathf.Sqrt(1f - toSun.y * toSun.y) + up).normalized, 6f, 1f);
            Place(dogRight, eye, (Quaternion.AngleAxis(spread, Vector3.up) * flat * Mathf.Sqrt(1f - toSun.y * toSun.y) + up).normalized, 6f, -1f);

            // Rainbow: sun out where you are (below 42 degrees), rain falling on the far side.
            float rainbow = day && elevation < 42f ? Mathf.Clamp01(rainAhead * 4f) * Mathf.InverseLerp(.9f, .55f, sky.Cover) : 0f;
            Ease(bow, rainbow * .7f, dt);
            Place(bow, eye, -toSun, 55f, 1f);

            // Glory: above a cloud deck with the sun behind you; the ring sits round your shadow on the cloud.
            float above = cameraAltitude - sky.CloudTop;
            float glow = day && elevation < 60f && sky.Cover > .45f && above > 30f && above < 3000f ? Mathf.InverseLerp(3000f, 600f, above) : 0f;
            Ease(glory, glow * .8f, dt);
            Place(glory, eye, -toSun, 7f, 1f);

            Meteor(eye, !day && elevation < -8f && sky.Cover < .45f, dt);
        }

        private void Meteor(Vector3 eye, bool clearNight, float dt)
        {
            if (meteorAge >= 0f)
            {
                meteorAge += dt;
                const float Life = .7f;
                float a = meteorAge < .12f ? meteorAge / .12f : Mathf.InverseLerp(Life, .25f, meteorAge);
                Vector3 dir = (meteorDir + meteorMotion * (meteorAge * .35f)).normalized;
                meteor.Go.transform.position = eye + dir * Range;
                meteor.Go.transform.rotation = Quaternion.LookRotation(dir, Vector3.Cross(dir, meteorMotion));
                meteor.Go.transform.localScale = new Vector3(Range * .09f, Range * .004f, 1f);
                SetLevel(meteor, a * .9f);
                if (meteorAge > Life) { meteorAge = -1f; SetLevel(meteor, 0f); }
                return;
            }
            nextMeteor -= dt;
            if (!clearNight || nextMeteor > 0f) return;
            nextMeteor = Random.Range(25f, 70f);
            // Somewhere 25 to 70 degrees up, falling slantwise.
            float az = Random.Range(0f, 360f), el = Random.Range(25f, 70f);
            meteorDir = Quaternion.Euler(-el, az, 0f) * Vector3.forward;
            meteorMotion = Vector3.ProjectOnPlane(Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * Vector3.down, meteorDir).normalized;
            meteorAge = 0f;
        }

        /// <summary>1 inside the plateau, falling to 0 at <paramref name="lo"/> and <paramref name="hi"/>.</summary>
        private static float Bell(float x, float lo, float mid, float hi) =>
            x < mid ? Mathf.InverseLerp(lo, mid, x) : Mathf.InverseLerp(hi, mid, x);

        private static void Ease(Glyph g, float target, float dt)
        {
            g.Level = Mathf.MoveTowards(g.Level, target, dt * .25f);   // about four seconds from nothing to full
            SetLevel(g, g.Level);
        }

        private static void SetLevel(Glyph g, float level)
        {
            bool on = level > .01f;
            if (g.Renderer.enabled != on) g.Renderer.enabled = on;
            if (!on) return;
            var c = new Color(level, level, level, level);
            if (g.Mat.HasProperty(BaseColor)) g.Mat.SetColor(BaseColor, c);
            if (g.Mat.HasProperty(ColorId)) g.Mat.SetColor(ColorId, c);
        }

        /// <param name="halfAngle">The quad's half-width as an angle seen from the eye.</param>
        /// <param name="mirror">-1 flips the texture left-right (the left sundog keeps its red side toward the sun).</param>
        private static void Place(Glyph g, Vector3 eye, Vector3 dir, float halfAngle, float mirror)
        {
            if (!g.Renderer.enabled) return;
            g.Go.transform.position = eye + dir * Range;
            g.Go.transform.rotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > .98f ? Vector3.forward : Vector3.up);
            float size = 2f * Range * Mathf.Tan(halfAngle * Mathf.Deg2Rad);
            g.Go.transform.localScale = new Vector3(size * mirror, size, 1f);
        }

        private void Fade(float target, float dt)
        {
            if (halo == null) return;
            Ease(halo, target, dt); Ease(dogLeft, target, dt); Ease(dogRight, target, dt); Ease(bow, target, dt); Ease(glory, target, dt);
            if (meteorAge >= 0f) { meteorAge = -1f; SetLevel(meteor, 0f); }
        }

        // ---- textures, baked once ----

        private bool Build()
        {
            Material puff = SmokeFx.Puff;
            if (puff == null) return false;
            const float Tan25 = .4663f;
            halo = Make(puff, "Halo", 256, (u, v) => Ring(u, v, Mathf.Tan(22f * Mathf.Deg2Rad) / Tan25, .035f, true, .75f));
            Texture2D dog = Bake("Sundog", 128, Sundog);
            dogLeft = Make(puff, "SundogL", dog);
            dogRight = Make(puff, "SundogR", dog);
            float tan55 = Mathf.Tan(55f * Mathf.Deg2Rad);
            bow = Make(puff, "Rainbow", 512, (u, v) =>
                Add(Ring(u, v, Mathf.Tan(42f * Mathf.Deg2Rad) / tan55, .03f, false, .9f),
                    Ring(u, v, Mathf.Tan(51f * Mathf.Deg2Rad) / tan55, .045f, true, .3f),
                    Inside(u, v, Mathf.Tan(40f * Mathf.Deg2Rad) / tan55, .07f)));
            glory = Make(puff, "Glory", 128, (u, v) =>
                Add(Ring(u, v, .42f, .07f, true, .7f), Ring(u, v, .72f, .07f, true, .45f), Inside(u, v, .2f, .25f)));
            meteor = Make(puff, "Meteor", Bake("Meteor", 64, (u, v) =>
            {
                float across = Mathf.Exp(-v * v * 30f), along = Mathf.Clamp01((u + 1f) * .5f);
                float a = across * along * along * (u > .9f ? Mathf.InverseLerp(1f, .9f, u) : 1f);
                return new Color(1f, .97f, .9f, a);
            }));
            return true;
        }

        private static Glyph Make(Material puff, string name, int size, System.Func<float, float, Color> pixel) =>
            Make(puff, name, Bake(name, size, pixel));

        private static Glyph Make(Material puff, string name, Texture2D tex)
        {
            var mat = new Material(puff) { name = "BoscaliSky" + name, hideFlags = HideFlags.HideAndDontSave };
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.One);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "BoscaliSky" + name;
            go.hideFlags = HideFlags.DontSave;
            Object.Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.enabled = false;
            return new Glyph { Go = go, Mat = mat, Renderer = r };
        }

        private static Texture2D Bake(string name, int size, System.Func<float, float, Color> pixel)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "BoscaliSky" + name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = pixel((x + .5f) / size * 2f - 1f, (y + .5f) / size * 2f - 1f);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Red (0) through yellow and green to violet (1).</summary>
        private static Color Spectrum(float x) => Color.HSVToRGB(Mathf.Lerp(0f, .78f, Mathf.Clamp01(x)), .7f, 1f);

        /// <summary>A coloured ring of radius <paramref name="r0"/> (1 = quad edge); red inside when <paramref name="redInside"/> (halos), outside for a rainbow.</summary>
        private static Color Ring(float u, float v, float r0, float width, bool redInside, float strength)
        {
            float t = (Mathf.Sqrt(u * u + v * v) - r0) / width;
            if (t <= -1f || t >= 1f) return Color.clear;
            Color c = Spectrum(redInside ? (t + 1f) * .5f : (1f - t) * .5f);
            c.a = (1f - t * t) * (1f - t * t) * strength;
            return c;
        }

        /// <summary>The faint brightening inside a bow or under a glory.</summary>
        private static Color Inside(float u, float v, float r0, float strength)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            return r >= r0 ? Color.clear : new Color(1f, 1f, 1f, strength * (1f - r / r0));
        }

        private static Color Add(params Color[] layers)
        {
            Color sum = Color.clear;
            float a = 0f;
            foreach (Color c in layers) { sum += c * c.a; a += c.a; }
            return a <= 0f ? Color.clear : new Color(sum.r / a, sum.g / a, sum.b / a, Mathf.Clamp01(a));
        }

        /// <summary>A sundog: a bright tall blob, red on the side toward the sun (left in texture space), fading blue-white outward.</summary>
        private static Color Sundog(float u, float v)
        {
            float a = Mathf.Exp(-(u * u * 9f + v * v * 3.5f)) + .25f * Mathf.Exp(-(u - .45f) * (u - .45f) * 6f - v * v * 30f);
            Color c = Spectrum(Mathf.InverseLerp(-.5f, .5f, u) * .6f);
            c = Color.Lerp(c, Color.white, Mathf.InverseLerp(.1f, .8f, u));
            c.a = Mathf.Clamp01(a);
            return c;
        }

        internal void Reset()
        {
            nextMeteor = 20f;
            meteorAge = -1f;
            var textures = new System.Collections.Generic.HashSet<Texture>();   // the two sundogs share one
            foreach (Glyph g in new[] { halo, dogLeft, dogRight, bow, glory, meteor })
            {
                if (g == null) continue;
                if (g.Go != null) Object.Destroy(g.Go);
                if (g.Mat == null) continue;
                if (g.Mat.mainTexture != null) textures.Add(g.Mat.mainTexture);
                Object.Destroy(g.Mat);
            }
            foreach (Texture t in textures) Object.Destroy(t);
            halo = dogLeft = dogRight = bow = glory = meteor = null;
        }
    }
}
