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
    /// </summary>
    internal sealed class SeaSpray
    {
        private const int MaxParticles = 1400, MaxWash = 4;
        private const float Reach = 5000f, RatePerSecond = 600f;

        private GameObject root;
        private ParticleSystem system;
        private Material material;
        private Texture2D texture;
        private bool failed;
        private readonly Dictionary<int, float> carry = new Dictionary<int, float>();
        private uint noise = 0x9E3779B9u;

        /// <summary>Global xz centre, radius, strength of the strongest washes this frame, and their heading (xy) and stretch along it (z).</summary>
        internal readonly Vector4[] Wash = new Vector4[MaxWash];
        internal readonly Vector4[] WashDir = new Vector4[MaxWash];
        internal int WashCount { get; private set; }
        internal float Strongest { get; private set; }
        internal int Particles => system != null ? system.particleCount : 0;

        internal void Tick(Camera camera, float seaY, bool emit, float dt)
        {
            WashCount = 0;
            Strongest = 0f;
            List<Aircraft> aircraft = UnitRegistry.allAircraft;
            if (camera == null || aircraft == null) return;
            Vector3 eye = camera.transform.position;
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
            }
            if (!emit && root != null && root.activeSelf && system.particleCount == 0) root.SetActive(false);
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
            return true;
        }

        /// <summary>A copy of the game's own water spray material (lit, soft-particle), if any loaded prefab brought it into memory.</summary>
        private static Material GameSprayMaterial()
        {
            Material[] all = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == "WaterSpray") return new Material(all[i]) { name = "BoscaliSeaSpray", hideFlags = HideFlags.HideAndDontSave };
            return null;
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
            if (root != null) Object.Destroy(root);
            if (material != null) Object.Destroy(material);
            if (texture != null) Object.Destroy(texture);
            root = null; system = null; material = null; texture = null;
            failed = false;
        }
    }
}
