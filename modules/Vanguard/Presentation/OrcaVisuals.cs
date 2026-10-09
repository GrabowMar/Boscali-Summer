using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer, from the synced transform only: at the splash (global y &lt;= 0) the WingKit is thrown off and the
    /// motor effects quench; under water the Rotor spins with the run speed and a running torpedo draws a foam
    /// trail on the surface. A sleeper hangs still, so it shows neither. Runs after the floating-origin shift.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    internal sealed class OrcaVisuals : MonoBehaviour
    {
        private const float SampleSeconds = 0.25f; // network positions arrive in steps: measure speed over a window
        private const float KitLife = 6f;
        private const float FoamLife = 5f;
        private static Material foamMaterial;
        private static bool noFoamShader;

        private Missile missile;
        private readonly List<Transform> kit = new List<Transform>();
        private Transform rotor;
        private Quaternion rotorRest;
        private Vector3 rotorAxis;
        private float rotorAngle;
        private GlobalPosition samplePos;
        private float sampleAt = -1f;
        private Vector3 velocity;
        private bool splashed, spun, foamed;
        private ParticleSystem foam;

        public static void Attach(Missile missile)
        {
            if (VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Torpedo || missile.GetComponent<OrcaVisuals>() != null) return;
            Transform visual = missile.transform.Find("VanguardVisual");
            if (visual == null) return;
            var view = missile.gameObject.AddComponent<OrcaVisuals>();
            view.missile = missile;
            Transform wingKit = null, left = null, right = null;
            foreach (Transform joint in visual.GetComponentsInChildren<Transform>(true))
                switch (joint.name)
                {
                    case "WingKit": wingKit = joint; break;
                    case "WingL": left = joint; break;
                    case "WingR": right = joint; break;
                    case "Rotor": view.rotor = joint; break;
                }
            // Fail soft: models without the clip-on kit joint shed the two wings instead.
            if (wingKit != null) view.kit.Add(wingKit);
            else
            {
                if (left != null) view.kit.Add(left);
                if (right != null) view.kit.Add(right);
            }
            if (view.rotor != null)
            {
                view.rotorRest = view.rotor.localRotation;
                // Spin about the body axis in the joint's parent frame, whatever axes the FBX import baked.
                view.rotorAxis = view.rotor.parent.InverseTransformDirection(missile.transform.forward);
            }
        }

        private void LateUpdate()
        {
            if (missile == null || missile.disabled)
            {
                StopFoam();
                enabled = false;
                return;
            }
            GlobalPosition here = missile.GlobalPosition();
            float now = Time.time;
            if (sampleAt < 0f || now - sampleAt >= SampleSeconds)
            {
                if (sampleAt >= 0f) velocity = (here - samplePos) / (now - sampleAt);
                samplePos = here;
                sampleAt = now;
            }
            float speed = velocity.magnitude;
            if (!splashed && here.y <= 0f && missile.timeSinceSpawn > 1f) Splash();
            if (rotor != null && here.y < 0f) Spin(OrcaSleeper.RotorDegPerSec(speed));
            Foam(OrcaSleeper.Bubbles(here.y, speed));
        }

        private void OnDestroy() => StopFoam();

        private void Splash()
        {
            splashed = true;
            if (kit.Count > 0) VanguardStats.Bump("orca.kitDetached");
            foreach (Transform part in kit) ThrowOff(part);
            kit.Clear();
            // Vanilla quenches a missile's motor in water inside DetectCollisions, which the torpedo skips.
            foreach (TrailEmitter trail in missile.GetComponentsInChildren<TrailEmitter>(true)) trail.StopTrail();
            foreach (ParticleSystem ps in missile.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (Light light in missile.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (AudioSource source in missile.GetComponentsInChildren<AudioSource>(true)) source.Stop();
        }

        // The kit pops up off the splash, tumbles and drops back into the sea. Parented to the floating origin so
        // shifts carry it; its LOD1/2 meshes are hidden because the torpedo's LODGroup no longer follows it.
        private void ThrowOff(Transform part)
        {
            if (part == null) return;
            part.SetParent(Datum.origin, true);
            foreach (Renderer r in part.GetComponentsInChildren<Renderer>(true))
                if (r.name.EndsWith("_LOD1") || r.name.EndsWith("_LOD2")) r.enabled = false;
            var body = part.gameObject.AddComponent<Rigidbody>();
            body.mass = 20f;
            body.drag = 0.6f;
            body.angularDrag = 0.4f;
            Vector3 side = part.position - missile.transform.position;
            body.velocity = velocity * 0.3f + Vector3.up * 9f + Vector3.ProjectOnPlane(side, Vector3.up).normalized * 3f;
            body.angularVelocity = Random.insideUnitSphere * 6f;
            Destroy(part.gameObject, KitLife);
        }

        private void Spin(float degPerSec)
        {
            if (degPerSec <= 0f) return;
            if (!spun)
            {
                spun = true;
                VanguardStats.Bump("orca.rotor");
            }
            rotorAngle = Mathf.Repeat(rotorAngle + degPerSec * Time.deltaTime, 360f);
            rotor.localRotation = Quaternion.AngleAxis(rotorAngle, rotorAxis) * rotorRest;
        }

        private void Foam(bool on)
        {
            if (!on)
            {
                if (foam != null && foam.isEmitting) foam.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            if (foam == null && (foam = MakeFoam()) == null) return;
            Vector3 p = missile.transform.position;
            foam.transform.position = new Vector3(p.x, Datum.LocalSeaY + 0.3f, p.z);
            if (foam.isEmitting) return;
            foam.Play();
            if (foamed) return;
            foamed = true;
            VanguardStats.Bump("orca.bubbles");
        }

        private void StopFoam()
        {
            if (foam == null) return;
            foam.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Destroy(foam.gameObject, FoamLife + 0.5f);
            foam = null;
        }

        // Flat foam patches on the surface, simulated in the floating-origin frame so shifts never smear them.
        private static ParticleSystem MakeFoam()
        {
            if (Datum.origin == null || noFoamShader) return null;
            if (foamMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default"); // alpha-blended, vertex colour; null on a headless server
                noFoamShader = shader == null;
                if (noFoamShader) return null;
                foamMaterial = new Material(shader) { name = "ORCA foam", mainTexture = SoftDot() };
            }
            var go = new GameObject("ORCA bubble trail");
            go.transform.SetParent(Datum.origin, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = FoamLife;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.283185f);
            main.startColor = new Color(0.86f, 0.93f, 0.95f, 1f);
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0.6f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.8f;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2.4f)));
            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.08f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.sharedMaterial = foamMaterial;
            return ps;
        }

        private static Texture2D SoftDot()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "ORCA foam dot", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
