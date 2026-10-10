using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// HAWC-X on every peer, from synced state only (age, speed, position, targetID): the Booster drops off at burnout
    /// and tumbles away, the Sheath glows through each glow phase, the body flaps trim with the skips. The launching
    /// pilot also gets the vg.hawcx line and a SHIFTED notice when the final dive moves onto a new designation.
    /// Runs after the floating-origin shift.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    internal sealed class HawcxVisuals : MonoBehaviour
    {
        private const string Owner = "vanguard.hawcx";
        private const string Channel = "vg.hawcx";
        private const float BoosterLife = 20f;
        private const float FlapSlewDegPerSec = 25f;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly Color Hot = new Color(1f, 0.42f, 0.12f) * 6f;
        private static readonly string[] SkipText = { "GLOW", "SKIP 1 - GLOW", "SKIP 2 - GLOW" };

        private Missile missile;
        private Transform booster, flapL, flapR;
        private Quaternion flapLRest, flapRRest;
        private Vector3 flapLAxis, flapRAxis;
        private Renderer[] sheath = new Renderer[0];
        private MaterialPropertyBlock block;
        private bool separated, glow, terminal, sheathOn;
        private float flap;
        private uint targetId;
        private GlobalPosition lastAim;
        private IHudLine line;

        public static void Attach(Missile missile)
        {
            if (VanguardKeys.RoleOf(missile.definition.jsonKey) != VanguardRole.Glider || missile.GetComponent<HawcxVisuals>() != null) return;
            Transform visual = missile.transform.Find("VanguardVisual");
            if (visual == null) return;
            var view = missile.gameObject.AddComponent<HawcxVisuals>();
            view.missile = missile;
            foreach (Transform joint in visual.GetComponentsInChildren<Transform>(true))
                switch (joint.name)
                {
                    case "Booster": view.booster = joint; break;
                    case "Sheath": view.sheath = joint.GetComponentsInChildren<Renderer>(true); break;
                    case "BodyFlapL": view.flapL = joint; break;
                    case "BodyFlapR": view.flapR = joint; break;
                }
            foreach (Renderer r in view.sheath) r.enabled = false;
            view.flapLAxis = view.Hinge(view.flapL, ref view.flapLRest);
            view.flapRAxis = view.Hinge(view.flapR, ref view.flapRRest);
            if (view.booster == null || view.sheath.Length == 0)
                Debug.Log("[Vanguard] HAWC-X model has no Booster/Sheath rig - separation/glow visuals off");
        }

        // Flaps hinge about the body's right axis expressed in the joint's parent frame, whatever axes the FBX baked.
        private Vector3 Hinge(Transform flapJoint, ref Quaternion rest)
        {
            if (flapJoint == null) return Vector3.right;
            rest = flapJoint.localRotation;
            return flapJoint.parent.InverseTransformDirection(missile.transform.right);
        }

        private void LateUpdate()
        {
            if (missile == null || missile.disabled)
            {
                line?.Release();
                line = null;
                enabled = false;
                return;
            }
            float age = missile.timeSinceSpawn;
            GlobalPosition here = missile.GlobalPosition();
            GlobalPosition aim = here + missile.transform.forward * 50000f;
            if (UnitRegistry.TryGetUnit(missile.targetID, out Unit target) && target != null) aim = target.GlobalPosition();
            Vector3 to = aim - here;
            GlidePhase phase = GlideProfile.Plan(age, new Vector2(to.x, to.z).magnitude, here.y, 0f, terminal).Phase;
            terminal |= phase == GlidePhase.Terminal;
            glow = GlideProfile.Glow(phase, age, missile.speed, here.y, glow);

            if (!separated && age >= GlideProfile.BoostTime) Separate();
            Sheath(glow ? GlideProfile.SheathGlow(missile.speed) : 0f);
            Flaps(GlideProfile.FlapAngle(phase, age));
            Hud(phase, age, aim);
        }

        private void Separate()
        {
            separated = true;
            if (booster == null) return;
            Vector3 velocity = missile.rb != null ? missile.rb.velocity : missile.transform.forward * missile.speed;
            booster.SetParent(Datum.origin, true);
            foreach (Renderer r in booster.GetComponentsInChildren<Renderer>(true))
                if (r.name.EndsWith("_LOD1") || r.name.EndsWith("_LOD2")) r.enabled = false; // the glider's LODGroup no longer covers it
            var body = booster.gameObject.AddComponent<Rigidbody>();
            body.mass = 400f;
            body.drag = 0.3f;
            body.angularDrag = 0.2f;
            body.velocity = velocity * 0.97f - missile.transform.forward * 4f;
            body.angularVelocity = missile.transform.right * 0.6f;
            Destroy(booster.gameObject, BoosterLife);
            booster = null;
        }

        private void Sheath(float heat)
        {
            bool on = heat > 0.01f;
            if (on != sheathOn)
            {
                sheathOn = on;
                foreach (Renderer r in sheath) if (r != null) r.enabled = on;
            }
            if (!on) return;
            block ??= new MaterialPropertyBlock();
            block.SetColor(EmissionColor, Hot * heat);
            foreach (Renderer r in sheath) if (r != null) r.SetPropertyBlock(block);
        }

        private void Flaps(float wanted)
        {
            if (flapL == null && flapR == null) return;
            flap = Mathf.MoveTowards(flap, wanted, FlapSlewDegPerSec * Time.deltaTime);
            // + = trailing edge down.
            if (flapL != null) flapL.localRotation = Quaternion.AngleAxis(-flap, flapLAxis) * flapLRest;
            if (flapR != null) flapR.localRotation = Quaternion.AngleAxis(-flap, flapRAxis) * flapRRest;
        }

        private void Hud(GlidePhase phase, float age, GlobalPosition aim)
        {
            if (!UnitRegistry.TryGetUnit(missile.ownerID, out Unit owner) || !GameManager.GetLocalAircraft(out Aircraft me) || me != owner) return;
            if (!ModuleServices.TryGet(out IHudBoard board)) return;
            board.DeclareChannel(Channel, "HAWC-X");
            uint id = missile.targetID.Id;
            if (terminal && targetId != 0 && id != targetId)
                board.Notice(Channel, HudTone.Info, GlideProfile.ShiftText(FastMath.Distance(lastAim, aim)), "FINAL DIVE");
            targetId = id;
            lastAim = aim;
            line ??= board.Acquire(Owner, Channel, missile.persistentID.Id.ToString());
            if (glow) line?.Set(HudTone.Caution, SkipText[GlideProfile.SkipAt(age)], "NO UPDATES", GlideProfile.SheathGlow(missile.speed));
            else if (phase == GlidePhase.Terminal) line?.Set(HudTone.Info, "FINAL - DESIGNATE TO SHIFT", "REACH 20 km", 1f);
            else if (phase == GlidePhase.Boost) line?.Set(HudTone.Info, "HAWC-X BOOST", null, age / GlideProfile.BoostTime);
            else line?.Set(HudTone.Info, "HAWC-X GLIDE", null, 0f);
        }

        private void OnDestroy() => line?.Release();
    }
}
