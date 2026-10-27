using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// Aircraft on the sea (2026-10-19): a jet low and fast over water throws a sheet of spray up behind it, and any low aircraft
    /// leaves a ruffled, foaming wash on the surface (<see cref="Wash"/>, drawn by the ocean shader). Vanilla only sprays for hovering
    /// rotorcraft and VTOLs (<c>Downwash</c>, whose range shrinks to nothing with speed); this covers the fast pass. One shared particle
    /// system in Datum space (floating-origin safe), emitted by hand per aircraft, at most <see cref="MaxParticles"/> alive, no collisions,
    /// lights or shadows. Uses the game's own <c>WaterSpray</c> material when it is loaded, else a procedural puff. Client-local and cosmetic.
    /// 2026-10-27: the wash used to vanish the moment the jet passed; a low aircraft now also lays a foam wake on the water, flat lace
    /// patches dropped by distance flown (so a faster jet drops fewer, larger ones and the count stays under <see cref="MaxWake"/>), the
    /// two outer rows drifting apart like a boat's V and fading over ~12 s. Fades in a heavy swell, where flat patches would float.
    /// </summary>
    internal sealed class SeaSpray
    {
        private const int MaxParticles = 1400, MaxWash = 4, MaxWake = 700;
        private const float Reach = 5000f, RatePerSecond = 600f;

        private GameObject root;
        private ParticleSystem system, wake;
        private Material material, wakeMaterial;
        private Texture2D texture, wakeTexture;
        private bool failed;
        private readonly Dictionary<int, float> carry = new Dictionary<int, float>();
        private readonly Dictionary<int, float> wakeRun = new Dictionary<int, float>();
        private uint noise = 0x9E3779B9u;

        /// <summary>Global xz centre, radius, strength of the strongest washes this frame, and their heading (xy) and stretch along it (z).</summary>
        internal readonly Vector4[] Wash = new Vector4[MaxWash];
        internal readonly Vector4[] WashDir = new Vector4[MaxWash];
        internal int WashCount { get; private set; }
        internal float Strongest { get; private set; }
        internal int Particles => system != null ? system.particleCount : 0;
        internal int WakeParticles => wake != null ? wake.particleCount : 0;

        internal void Tick(Camera camera, float seaY, float waveHeight, bool emit, float dt)
        {
            WashCount = 0;
            Strongest = 0f;
            List<Aircraft> aircraft = UnitRegistry.allAircraft;
            if (camera == null || aircraft == null) return;
            Vector3 eye = camera.transform.position;
            Color foam = WakeTint(waveHeight);
            for (int i = 0; i < aircraft.Count; i++)
            {
                Aircraft plane = aircraft[i];
                if (plane == null || plane.rb == null) continue;
                Vector3 at = plane.transform.position;
                float height = at.y - seaY;
                if (height < -1f || height > 150f) continue;
                if ((at - eye).sqrMagnitude > Reach * Reach) continue;
                // Radar altitude well under the height above the sea means land below: no spray off a beach or a carrier deck.
                if (plane.radarAlt < height - 3f) continue;
                float radius = plane.maxRadius, speed = plane.speed;
                Vector3 velocity = plane.rb.velocity;
                Vector2 flat = new Vector2(velocity.x, velocity.z);
                float wash = SeaState.WashIntensity(height, radius);
                if (wash > 0.01f) AddWash(plane, at, flat, radius, height, speed, wash);
                float spray = SeaState.SprayIntensity(height, speed, radius);
                Strongest = Mathf.Max(Strongest, spray);
                if (emit && spray > 0.01f) Emit(plane, at, flat, velocity, radius, seaY, spray, dt);
                if (emit && wash > 0.05f && speed > 15f && foam.a > 0.02f) DropWake(plane, at, flat, radius, seaY, wash, speed, foam, dt);
            }
            if (!emit && root != null && root.activeSelf && system.particleCount == 0 && (wake == null || wake.particleCount == 0))
                root.SetActive(false);
        }

        private void AddWash(Aircraft plane, Vector3 at, Vector2 flat, float radius, float height, float speed, float strength)
        {
            int slot;
            if (WashCount < MaxWash) slot = WashCount++;
            else
            {
                slot = 0;
                for (int i = 1; i < MaxWash; i++) if (Wash[i].w < Wash[slot].w) slot = i;
                if (Wash[slot].w >= strength) return;
            }
            GlobalPosition g = at.ToGlobalPosition();
            Vector3 forward = plane.transform.forward;
            Vector2 heading = flat.sqrMagnitude > 1f ? flat.normalized : new Vector2(forward.x, forward.z).normalized;
            // A fast jet's blast lands behind it and drags into a long ellipse whose front edge sits under the jet (centred on the jet, the
            // stretched ellipse ran ahead of the nose; live check 2026-10-19); a hover is a round ring under the rotor.
            float reach = radius * 1.4f + height * 0.5f, stretch = 1f + speed / 50f, back = reach * (stretch - 1f);
            Wash[slot] = new Vector4(g.x - heading.x * back, g.z - heading.y * back, reach, strength);
            WashDir[slot] = new Vector4(heading.x, heading.y, stretch, 0f);
        }

        private void Emit(Aircraft plane, Vector3 at, Vector2 flat, Vector3 velocity, float radius, float seaY, float spray, float dt)
        {
            if (!Ensure()) return;
            if (!root.activeSelf) root.SetActive(true);
            int id = plane.GetInstanceID();
            carry.TryGetValue(id, out float owed);
            owed += RatePerSecond * spray * dt;
            int count = Mathf.Min((int)owed, 40);
            carry[id] = owed - (int)owed;
            if (count <= 0) return;
            Vector3 right = Vector3.Cross(Vector3.up, flat.sqrMagnitude > 1f ? new Vector3(flat.x, 0f, flat.y).normalized : plane.transform.forward);
            Transform datum = Datum.origin;
            for (int n = 0; n < count; n++)
            {
                // The sheet rises just behind the jet across its span, kicked up and dragged along with it, then hangs and falls.
                float back = Next(0.05f, 0.3f);
                Vector3 point = new Vector3(at.x - flat.x * back, seaY + 0.4f, at.z - flat.y * back) + right * Next(-0.75f, 0.75f) * radius;
                Vector3 kick = Vector3.up * Next(4f, 10f) * (0.5f + spray) + new Vector3(velocity.x, 0f, velocity.z) * Next(0.04f, 0.14f)
                    + right * Next(-5f, 5f);
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = datum.InverseTransformPoint(point),
                    velocity = datum.InverseTransformDirection(kick),
                    startSize = Next(4f, 10f) * (0.7f + 0.6f * spray),
                    startLifetime = Next(1.4f, 2.8f),
                    rotation = Next(0f, 360f),
                    startColor = new Color(1f, 1f, 1f, 0.6f * spray + 0.15f),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        /// <summary>Unlit foam tint: dimmed with the sun so it does not glow at night, alpha faded out as the swell builds.</summary>
        private static Color WakeTint(float waveHeight)
        {
            Light sun = RenderSettings.sun;
            float light = sun == null ? 1f : Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(Vector3.Dot(-sun.transform.forward, Vector3.up) * 5f + 0.3f));
            Color c = new Color(0.9f, 0.95f, 1f) * light;
            c.a = 1f - 0.8f * Mathf.Clamp01((waveHeight - 1.2f) / 2.5f);
            return c;
        }

        private void DropWake(Aircraft plane, Vector3 at, Vector2 flat, float radius, float seaY, float strength, float speed, Color foam, float dt)
        {
            if (!Ensure() || wake == null) return;
            if (!root.activeSelf) root.SetActive(true);
            int id = plane.GetInstanceID();
            float spacing = Mathf.Max(radius * 1.2f, speed * 0.06f, 4f);
            wakeRun.TryGetValue(id, out float run);
            run += speed * dt;
            if (run < spacing) { wakeRun[id] = run; return; }
            wakeRun[id] = Mathf.Min(run - spacing, spacing); // one drop per frame at most: no burst after a hitch
            Vector2 heading = flat.sqrMagnitude > 1f ? flat.normalized : new Vector2(plane.transform.forward.x, plane.transform.forward.z).normalized;
            Vector3 right = new Vector3(heading.y, 0f, -heading.x);
            float open = Mathf.Min(speed * 0.04f, 8f), fade = foam.a;
            Transform datum = Datum.origin;
            for (int side = -1; side <= 1; side++)
            {
                Vector3 point = new Vector3(at.x, seaY + 0.3f, at.z) + right * (side * radius * 0.35f + Next(-1f, 1f));
                foam.a = strength * fade * (side == 0 ? 0.85f : 0.55f) * Next(0.7f, 1f);
                wake.Emit(new ParticleSystem.EmitParams
                {
                    position = datum.InverseTransformPoint(point),
                    velocity = datum.InverseTransformDirection(right * (side * open * Next(0.6f, 1.2f))),
                    startSize = spacing * Next(1.6f, 2.2f) * (side == 0 ? 1f : 0.8f) + radius * 0.5f,
                    startLifetime = Next(9f, 14f),
                    rotation = Next(0f, 360f),
                    startColor = foam,
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        private float Next(float min, float max)
        {
            noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
            return min + (max - min) * (noise & 0xFFFFFF) / 16777216f;
        }

        private bool Ensure()
        {
            if (system != null) return true;
            if (failed) return false;
            material = GameSprayMaterial();
            if (material == null)
            {
                texture = PuffTexture();
                material = RainStreakMaterial.CreateMaterial(texture, additive: false, cameraFade: true);
            }
            if (material == null) { failed = true; return false; }
            root = new GameObject("BoscaliSeaSpray") { hideFlags = HideFlags.DontSave };
            system = root.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = MaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.startSpeed = 0f;
            // Gravity only: a limit-velocity damp killed the kick within a few frames and sank the sheet under the sea (live check 2026-10-19).
            main.gravityModifier = 0.4f;
            var emission = system.emission;
            emission.rateOverTime = 0f;
            var shape = system.shape;
            shape.enabled = false;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.4f));
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.88f, 0.9f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0.55f, 0.45f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = 0.6f;
            system.Play();
            CreateWake();
            return true;
        }

        /// <summary>The flat foam layer, a child of the spray root: horizontal billboards lying on the sea, drawn under the spray.</summary>
        private void CreateWake()
        {
            wakeTexture = LaceTexture();
            wakeMaterial = RainStreakMaterial.CreateMaterial(wakeTexture, additive: false, cameraFade: false);
            if (wakeMaterial == null) return;
            var child = new GameObject("BoscaliSeaWake") { hideFlags = HideFlags.DontSave };
            child.transform.SetParent(root.transform, false);
            wake = child.AddComponent<ParticleSystem>();
            wake.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = wake.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = MaxWake;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            var emission = wake.emission;
            emission.rateOverTime = 0f;
            var shape = wake.shape;
            shape.enabled = false;
            var drag = wake.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 0.35f;
            drag.multiplyDragByParticleSize = false;
            var size = wake.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.8f, 1f, 2.6f));
            var colour = wake.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.86f, 0.9f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.04f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;
            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.sharedMaterial = wakeMaterial;
            renderer.sortingFudge = 40f; // under the spray sheet
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = 4f; // flat patches under a low camera fill the view; the default cap shrank them
            wake.Play();
        }

        /// <summary>A copy of the game's own water spray material (lit, soft-particle), if any loaded prefab brought it into memory.</summary>
        private static Material GameSprayMaterial()
        {
            Material[] all = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == "WaterSpray") return new Material(all[i]) { name = "BoscaliSeaSpray", hideFlags = HideFlags.HideAndDontSave };
            return null;
        }

        /// <summary>Broken foam lace: a soft disc cut into veins by thresholded noise, denser at the centre.</summary>
        private static Texture2D LaceTexture()
        {
            const int s = 128;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, true) { name = "BoscaliWakeLace", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s * 2f - 1f, v = (y + 0.5f) / s * 2f - 1f;
                    float disc = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                    float n = Mathf.PerlinNoise(x * 0.06f + 7f, y * 0.06f + 1f) * 0.65f + Mathf.PerlinNoise(x * 0.19f + 2f, y * 0.19f + 5f) * 0.35f;
                    float lace = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((n - 0.62f + disc * 0.35f) * 5f));
                    float a = Mathf.Clamp01(disc * 2.2f) * lace;
                    pixels[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(pixels);
            t.Apply(true, true);
            return t;
        }

        /// <summary>A soft, lumpy white puff with transparent edges.</summary>
        private static Texture2D PuffTexture()
        {
            const int s = 64;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, true) { name = "BoscaliSprayPuff", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s * 2f - 1f, v = (y + 0.5f) / s * 2f - 1f;
                    float lump = Mathf.PerlinNoise(x * 0.11f + 3f, y * 0.11f + 9f);
                    float a = Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) * 1.6f) * (0.55f + 0.45f * lump);
                    pixels[y * s + x] = new Color32(250, 252, 255, (byte)(a * a * 255f));
                }
            t.SetPixels32(pixels);
            t.Apply(true, true);
            return t;
        }

        internal void Reset()
        {
            WashCount = 0;
            Strongest = 0f;
            carry.Clear();
            wakeRun.Clear();
            if (root != null) Object.Destroy(root);
            if (material != null) Object.Destroy(material);
            if (texture != null) Object.Destroy(texture);
            if (wakeMaterial != null) Object.Destroy(wakeMaterial);
            if (wakeTexture != null) Object.Destroy(wakeTexture);
            root = null; system = null; material = null; texture = null;
            wake = null; wakeMaterial = null; wakeTexture = null;
            failed = false;
        }
    }
}
