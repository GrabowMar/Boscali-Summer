using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer with a screen: drives the LANCE model's contract joints from the pod's LanceLook. Barrel slams back at
    /// release and eases home; Charge0..Charge6 glow strips light aft to fore with the bank, pulse when full, flash on
    /// release and shade toward orange as the rails heat; a muzzle flash light, and a charge whine. Joints are found by
    /// name and any that are missing are skipped. Glow materials are shared assets: only MaterialPropertyBlocks are written.
    /// </summary>
    internal sealed class LancePodView : MonoBehaviour
    {
        private const float ViewRange = 1500f;
        private const float FlashSeconds = 0.08f;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly Color Cyan = new Color(0.25f, 0.85f, 1f);
        private static readonly Color Orange = new Color(1f, 0.45f, 0.1f);
        private static AudioClip whineClip;

        private LancePod pod;
        private Transform mount, barrel, muzzle;
        private Vector3 barrelRest;
        private readonly Renderer[][] strips = new Renderer[LancePose.Strips][];
        private readonly float[] pushed = new float[LancePose.Strips];
        private float pushedBlend = -1f;
        private MaterialPropertyBlock block;
        private bool idle = true;
        private Light flash;
        private float flashAt = -99f, flashPower;
        private AudioSource whine;

        public static LancePodView Attach(LancePod pod)
        {
            Transform visual = null;
            for (Transform t = pod.Gun.transform; t != null && visual == null; t = t.parent) visual = t.Find("VanguardVisual");
            if (visual == null) return null;
            var view = pod.Gun.gameObject.AddComponent<LancePodView>();
            view.pod = pod;
            view.mount = visual.parent;
            view.Find(visual);
            view.BuildFlash(); // a disabled light now, so the first shot does not pay for URP light setup
            return view;
        }

        private void BuildFlash()
        {
            var go = new GameObject("LanceFlash");
            go.transform.SetParent(transform, false);
            flash = go.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(0.7f, 0.85f, 1f);
            flash.range = 30f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
        }

        private void Find(Transform visual)
        {
            var joints = new Transform[LancePose.Strips];
            foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Barrel") barrel = t;
                else if (t.name == "Muzzle") muzzle = t;
                else if (t.name.Length == 7 && t.name.StartsWith("Charge") && t.name[6] >= '0' && t.name[6] < '0' + LancePose.Strips)
                    joints[t.name[6] - '0'] = t;
            }
            var missing = new List<string>();
            if (barrel == null) missing.Add("Barrel");
            else barrelRest = barrel.localPosition;
            for (int i = 0; i < strips.Length; i++)
            {
                strips[i] = joints[i] == null ? new Renderer[0] : GlowRenderers(joints[i]);
                if (strips[i].Length == 0) missing.Add("Charge" + i);
            }
            if (missing.Count > 0) Plugin.Logger?.LogWarning("LANCE view: no " + string.Join(", ", missing) + " on " + visual.parent.name + "; those parts stay static.");
        }

        private static Renderer[] GlowRenderers(Transform joint)
        {
            var found = new List<Renderer>();
            foreach (Renderer r in joint.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Glow") || (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("Vanguard_Glow"))) found.Add(r);
            return found.ToArray();
        }

        /// <summary>Muzzle flash light for a shot of this power (owner at release, others from RemoteSingleFire).</summary>
        public void PlayShot(float power)
        {
            flashAt = Time.time;
            flashPower = power;
            if (flash == null) BuildFlash(); // the view was built without one (never expected)
            Vector3 forward = mount != null ? mount.forward : transform.forward;
            flash.transform.position = (muzzle != null ? muzzle.position : transform.position + forward * 3f) + forward;
        }

        private void LateUpdate()
        {
            if (pod == null || pod.Gun == null)
            {
                Destroy(this);
                return;
            }
            float now = Time.time;
            LanceLook look = pod.Look(now);
            bool active = look.Charging || look.Recharge01 >= 0f || look.Heat > 0.05f || (look.SinceShot >= 0f && look.SinceShot < 6f);
            if (!active && idle) return;
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera != null && (camera.transform.position - transform.position).sqrMagnitude > ViewRange * ViewRange)
            {
                if (!idle) Rest();
                return;
            }
            if (!active)
            {
                Rest();
                return;
            }
            idle = false;
            PushBarrel(look);
            PushStrips(look, now);
            PushFlash(now);
            PushWhine(look);
        }

        private void PushBarrel(LanceLook look)
        {
            if (barrel == null) return;
            float back = LancePose.BarrelOffset(look.SinceShot, look.LastPower);
            Vector3 forward = mount != null ? mount.forward : transform.forward;
            barrel.localPosition = barrelRest + barrel.parent.InverseTransformVector(-forward * back);
        }

        private void PushStrips(LanceLook look, float now)
        {
            block ??= new MaterialPropertyBlock();
            float blend = LancePose.HeatBlend(look.Heat);
            // Compared with the blend LAST PUSHED (not last frame's): the rails cool a hair per frame, so a frame-to-frame test never trips.
            bool blendMoved = Mathf.Abs(blend - pushedBlend) >= 0.02f;
            Color tint = Color.Lerp(Cyan, Orange, blend);
            for (int i = 0; i < strips.Length; i++)
            {
                // A powered but unlit strip keeps a dim floor so the bank never reads as dead hardware.
                float value = Mathf.Max(LancePose.StripIntensity(i, look.Charge, look.Charging, now, look.SinceShot, look.LastPower, look.Recharge01), 0.3f);
                if (Mathf.Abs(value - pushed[i]) < 0.02f && !blendMoved) continue;
                pushed[i] = value;
                block.SetColor(EmissionColor, tint * value);
                foreach (Renderer r in strips[i]) r.SetPropertyBlock(block);
            }
            if (blendMoved) pushedBlend = blend;
        }

        private void PushFlash(float now)
        {
            if (flash == null) return;
            float age = now - flashAt;
            if (age >= FlashSeconds)
            {
                flash.enabled = false;
                return;
            }
            float peak = 2f + 8f * flashPower;
            flash.intensity = age < 0.03f ? peak : peak * (1f - (age - 0.03f) / (FlashSeconds - 0.03f));
            flash.enabled = true;
        }

        private void PushWhine(LanceLook look)
        {
            if (!look.Charging || look.Charge < 0.02f)
            {
                if (whine != null && whine.isPlaying) whine.Stop();
                return;
            }
            if (whine == null)
            {
                whineClip ??= MakeWhine();
                var go = new GameObject("LanceWhine");
                go.transform.SetParent(transform, false);
                whine = go.AddComponent<AudioSource>();
                whine.clip = whineClip;
                whine.loop = true;
                whine.dopplerLevel = 0f;
                whine.minDistance = 20f;
                whine.maxDistance = 800f;
                whine.rolloffMode = AudioRolloffMode.Linear;
                // The game's volume sliders live on its mixer groups: use the group of the gun's own sound source.
                AudioSource vanilla = pod.Gun.GetComponent<AudioSource>();
                AudioMixerGroup group = vanilla != null ? vanilla.outputAudioMixerGroup : null;
                if (group == null && SoundManager.i != null) group = SoundManager.i.EffectsMixer;
                if (group != null) whine.outputAudioMixerGroup = group;
            }
            bool mine = pod.Owner && pod.Aircraft.Player != null && GameManager.GetLocalAircraft(out Aircraft me) && me == pod.Aircraft;
            whine.spatialBlend = mine ? 0f : 1f;
            whine.pitch = 0.55f + 1.65f * look.Charge;
            whine.volume = 0.25f + 0.35f * look.Charge;
            if (!whine.isPlaying) whine.Play();
        }

        // Strips back to the material's own dim glow, barrel home, light and whine off.
        private void Rest()
        {
            idle = true;
            pushedBlend = -1f;
            for (int i = 0; i < strips.Length; i++)
            {
                pushed[i] = 0f;
                foreach (Renderer r in strips[i]) r.SetPropertyBlock(null);
            }
            if (barrel != null) barrel.localPosition = barrelRest;
            if (flash != null) flash.enabled = false;
            if (whine != null && whine.isPlaying) whine.Stop();
        }

        private void OnDisable()
        {
            if (whine != null && whine.isPlaying) whine.Stop();
        }

        // Integer-Hz partials so the loop is seamless: 180 Hz fundamental, 360 Hz with a slow wobble, 1080 Hz whine.
        private static AudioClip MakeWhine()
        {
            const int rate = 22050;
            var samples = new float[rate];
            float peak = 0f;
            for (int i = 0; i < rate; i++)
            {
                float t = i / (float)rate;
                float v = 0.45f * Mathf.Sin(2f * Mathf.PI * 180f * t) +
                          0.25f * Mathf.Sin(2f * Mathf.PI * 360f * t + 1.2f * Mathf.Sin(2f * Mathf.PI * 3f * t)) +
                          0.12f * Mathf.Sin(2f * Mathf.PI * 1080f * t);
                samples[i] = v;
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < rate; i++) samples[i] *= 0.9f / peak;
            AudioClip clip = AudioClip.Create("LanceWhine", rate, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
