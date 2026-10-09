using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer: HAWC-X booster drop at burnout, the glowing sheath, body-flap trim and the launcher's "vg.hawcx" HUD line.
    /// The server reads its phase from <see cref="HawcxState"/> (which also owns the detection filter); remote clients have
    /// no flight, so they re-derive it from the shared skip clock and the flight path. Each part is optional: a missing
    /// joint is logged once and that part skipped. Velocity comes from position deltas so it holds on clients too.
    /// </summary>
    internal sealed class HawcxVisuals : MonoBehaviour
    {
        private const string Owner = "vanguard.hawcx";
        private const string Channel = "vg.hawcx";
        private const float FlapSlew = 25f;   // deg/s
        private const float ShiftHold = 4f;   // s the SHIFTED line stays up
        private const float DiveSine = -0.6f; // client final estimate: flight path below ~37 deg
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color Plasma = new Color(1f, 0.42f, 0.12f) * 6f;
        private static readonly string[] SkipText = { "GLOW", "SKIP 1 - GLOW", "SKIP 2 - GLOW" };
        private static bool warned;

        private Missile missile;
        private Transform booster;
        private Transform flapL, flapR;
        private Quaternion restL, restR;
        private Vector3 axisL, axisR;
        private float flap;
        private Renderer[] sheath;
        private bool sheathShown;
        private MaterialPropertyBlock block;
        private string lineKey;
        private IHudBoard board;
        private IHudLine line;
        private GlobalPosition lastPos;
        private Vector3 velocity;
        private bool sampled;
        private bool glow, final; // client estimate (the server copies HawcxState in)

        public static void Attach(Missile missile)
        {
            if (VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Glider || missile.GetComponent<HawcxVisuals>() != null) return;
            var v = missile.gameObject.AddComponent<HawcxVisuals>();
            v.missile = missile;
            v.lineKey = "hawcx." + missile.GetInstanceID();
            v.velocity = missile.rb != null ? missile.rb.velocity : Vector3.zero;
            Transform visual = missile.transform.Find("VanguardVisual");
            if (visual != null) v.Rig(visual);
            if ((v.booster == null || v.sheath == null || v.flapL == null) && !warned)
            {
                warned = true;
                Plugin.Logger?.LogWarning("[Vanguard] HAWC-X rig incomplete (booster=" + (v.booster != null) + " sheath=" +
                    (v.sheath != null) + " flaps=" + (v.flapL != null) + "): rebuild Vanguard.nobp from a HawcX model with Booster/Sheath joints");
            }
        }

        private void Rig(Transform visual)
        {
            Transform sheathJoint = null;
            foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
                switch (t.name)
                {
                    case "Booster": booster = t; break;
                    case "Sheath": sheathJoint = t; break;
                    case "BodyFlapL": flapL = t; break;
                    case "BodyFlapR": flapR = t; break;
                }
            if (sheathJoint != null)
            {
                // A joint is always a skin carrier; only its Glow children (all three LODs) are the sheath (VanguardBuilder.hawcx).
                var glows = new List<Renderer>();
                foreach (Renderer r in sheathJoint.GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("Glow", StringComparison.Ordinal)) glows.Add(r);
                if (glows.Count > 0)
                {
                    sheath = glows.ToArray();
                    block = new MaterialPropertyBlock();
                    sheathShown = true;
                    ShowSheath(false);
                }
            }
            if (flapL == null || flapR == null)
            {
                flapL = flapR = null;
                return;
            }
            restL = flapL.localRotation;
            restR = flapR.localRotation;
            // FBX axes are baked and the visual root adds +90X: hinge on the airframe's right in each parent frame.
            axisL = flapL.parent.InverseTransformDirection(missile.transform.right);
            axisR = flapR.parent.InverseTransformDirection(missile.transform.right);
        }

        private void Update()
        {
            if (missile == null || missile.disabled)
            {
                Release();
                enabled = false;
                return;
            }
            float age = missile.timeSinceSpawn;
            GlobalPosition pos = missile.GlobalPosition();
            float dt = Time.deltaTime;
            if (sampled && dt > 1e-4f) velocity = Vector3.Lerp(velocity, (pos - lastPos) / dt, 1f - Mathf.Exp(-dt * 5f));
            lastPos = pos;
            sampled = true;
            float speed = velocity.magnitude;

            GlidePhase phase;
            int skip;
            HawcxState state = null;
            if (HawcxState.TryGet(missile, out state))
            {
                phase = state.Phase;
                skip = state.Skip;
                glow = state.Glow;
                final = state.Final;
            }
            else
            {
                if (!final && age > GlideProfile.BoostTime && velocity.y < DiveSine * speed) final = true;
                phase = final ? GlidePhase.Terminal : age < GlideProfile.BoostTime ? GlidePhase.Boost : GlidePhase.Glide;
                skip = phase == GlidePhase.Glide ? GlideProfile.SkipAt(age) : 0;
                glow = GlideProfile.Glow(phase, age, speed, (float)pos.y, glow);
            }

            if (booster != null && age >= GlideProfile.BoostTime) DropBooster();
            if (sheath != null)
            {
                ShowSheath(glow);
                if (glow)
                {
                    block.SetColor(EmissionId, Plasma * GlideProfile.SheathGlow(speed));
                    for (int i = 0; i < sheath.Length; i++) sheath[i].SetPropertyBlock(block);
                }
            }
            if (flapL != null)
            {
                flap = Mathf.MoveTowards(flap, GlideProfile.FlapAngle(phase, age), FlapSlew * dt);
                // + is trailing edge down: about the right axis a negative angle swings the aft edge down.
                flapL.localRotation = Quaternion.AngleAxis(-flap, axisL) * restL;
                flapR.localRotation = Quaternion.AngleAxis(-flap, axisR) * restR;
            }
            Hud(state, skip, age);
        }

        private void ShowSheath(bool on)
        {
            if (on == sheathShown) return;
            sheathShown = on;
            for (int i = 0; i < sheath.Length; i++) sheath[i].enabled = on;
        }

        // The booster falls away on its own: out of the glider's LOD group (its distance must not cull it, and the group
        // dying with the glider must not light all three levels), low LODs off, a free body under the floating origin.
        private void DropBooster()
        {
            Transform b = booster;
            booster = null;
            LODGroup group = b.parent != null ? b.parent.GetComponentInParent<LODGroup>() : null;
            if (group != null)
            {
                LOD[] lods = group.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                    lods[i].renderers = Array.FindAll(lods[i].renderers, r => r != null && !r.transform.IsChildOf(b));
                group.SetLODs(lods);
            }
            foreach (Renderer r in b.GetComponentsInChildren<Renderer>(true))
                if (r.name.EndsWith("_LOD1", StringComparison.Ordinal) || r.name.EndsWith("_LOD2", StringComparison.Ordinal)) r.enabled = false;
            b.SetParent(Datum.origin, true);
            var body = b.gameObject.AddComponent<Rigidbody>();
            body.mass = 400f;
            body.drag = 0.3f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.velocity = velocity;
            body.angularVelocity = missile.transform.right * 0.4f; // slow tumble
            Destroy(b.gameObject, 20f);
        }

        private void Hud(HawcxState state, int skip, float age)
        {
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null || missile.owner != me)
            {
                Release();
                return;
            }
            HudTone tone;
            string text, detail;
            if (state != null && state.ShiftAge >= 0f && age - state.ShiftAge < ShiftHold)
            {
                tone = HudTone.Info;
                text = state.ShiftText;
                detail = "FINAL ON THE NEW TARGET";
            }
            else if (glow)
            {
                tone = HudTone.Caution;
                text = skip > 0 && skip < SkipText.Length ? SkipText[skip] : SkipText[0];
                detail = "UNSEEN - COURSE LOCKED";
            }
            else if (final)
            {
                tone = HudTone.Info;
                text = "FINAL - DESIGNATE TO SHIFT";
                // The input is the REMORA strike order (shared channel: drones in the air strike it too).
                detail = ModuleServices.TryGet(out IDroneCommand drones) && drones.Striking
                    ? "C: DRONES SCREEN, THEN STRIKE" : "C: DRONES: STRIKE (20 KM)";
            }
            else
            {
                Release();
                return;
            }
            if (board == null && !ModuleServices.TryGet(out board)) return;
            if (line == null)
            {
                board.DeclareChannel(Channel, "HAWC-X");
                line = board.Acquire(Owner, Channel, lineKey);
            }
            line?.Set(tone, text, detail, 0f);
        }

        private void Release()
        {
            line?.Release();
            line = null;
        }

        private void OnDestroy() => Release();
    }
}
