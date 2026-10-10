using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// The tender's dorsal hardware: the fuel boom (FuelBase yaw, FuelBoom pitch, FuelTele telescope, Nozzle tip), the loader arm
    /// (LoadBase yaw, LoadUpper and LoadFore pitch, Gripper) and the rotary magazine with its rounds. Everything reaches UP at the
    /// receiver. The arms are aimed with world-space FromTo rotations re-derived from the stowed pose every frame, so they work
    /// whatever the model's local axes are, and every joint a model lacks is simply skipped.
    /// </summary>
    internal sealed partial class SkywellVisuals
    {
        /// <summary>
        /// The launcher angle that brings each slot into the pickup position. The launcher is a rotary rack about the tender's long axis
        /// and the stowed model holds Round0 in the pickup position (beside the loader), so slot s needs minus s steps; the step is the
        /// model's own Round0 to Round1 spacing about that axis.
        /// </summary>
        private static void MeasureMagazine(Rig rig)
        {
            float step = SkywellRig.MagazineStepDeg;
            if (rig.Magazine.Ok && rig.Rounds[0].Ok && rig.Rounds[1].Ok)
            {
                Vector3 axis = rig.Body0 * Vector3.forward, centre = rig.Magazine.T.position;
                Vector3 r0 = Vector3.ProjectOnPlane(rig.Rounds[0].T.position - centre, axis);
                Vector3 r1 = Vector3.ProjectOnPlane(rig.Rounds[1].T.position - centre, axis);
                float measured = r0.sqrMagnitude > 1e-4f && r1.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(r0, r1, axis) : 0f;
                if (Mathf.Abs(measured) > 5f && Mathf.Abs(measured) < 120f) step = measured;
            }
            for (int i = 0; i < rig.SlotAngle.Length; i++) rig.SlotAngle[i] = -i * step;
        }

        private static float Smooth(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

        private void PoseArms(Rig rig, SkywellView v, Aircraft px, bool poseOn, bool armWork, bool hot, Quaternion body, float dt)
        {
            bool fuelWork = armWork && rig.PoseStage == StageFuel;
            bool stationWork = armWork && rig.PoseStage == StageStation;
            float t = rig.ArmClock;

            // ---- fuel boom: swings up onto the receptacle (the slide is along the boom, so it reads as a telescope), holds through the
            // flow, then unplugs: it comes back faster than it went out, so it is clear before the tender moves on.
            float flow = Mathf.Max(0f, v.JobTenths / 10f - PitPlan.FuelPlug - PitPlan.FuelUnplug - (v.JobIndex > 0 ? PitPlan.TravelSeconds : 0f));
            float swingTarget = fuelWork ? SkywellHandoff.Boom(t, flow) : 0f;
            bool unplugging = swingTarget < rig.Swing;
            rig.Swing = unplugging ? Mathf.MoveTowards(rig.Swing, swingTarget, dt / (hot ? 0.15f : 0.4f)) : swingTarget;
            if (rig.Swing > 0.001f && poseOn) PoseBoom(rig, body, SkywellBoard.FuelPoint(px), rig.Swing);

            // ---- loader: unfolds off the tender's back once it is on a rail's station, then lifts each round straight up to the pylon
            // (0.8 s), latches (0.4 s, the host commits at the end of it), lowers (0.8 s) while the carousel indexes (0.5 s).
            float unfoldTarget = poseOn && rig.PoseStage == StageStation && rig.Gap < 8f ? 1f : 0f;
            rig.Unfold = Mathf.MoveTowards(rig.Unfold, unfoldTarget, dt / (hot ? 0.3f : 0.7f));
            if (stationWork) rig.Reach = SkywellHandoff.Reach(t);
            else rig.Reach = Mathf.MoveTowards(rig.Reach, 0f, dt / (hot ? 0.3f : 0.6f));
            PoseMagazine(rig, v, stationWork, t, dt);
            int slot = rig.CarrySlot >= 0 ? rig.CarrySlot : SkywellMagazine.NextSlot(v.Rounds);
            Vector3 pickup = slot < SkywellMagazine.Rounds && rig.Rounds[slot].Ok ? rig.Rounds[slot].T.position : rig.Gripper.Ok ? rig.Gripper.T.position : Vector3.zero;
            bool carrying = stationWork && rig.CarrySlot >= 0 && SkywellHandoff.Carrying(t);
            if (rig.Unfold > 0.001f && poseOn && rig.PoseStage == StageStation || rig.Reach > 0.001f && poseOn)
            {
                Vector3 pylon = px.transform.TransformPoint(rig.PoseRail);
                PoseLoader(rig, body, Vector3.Lerp(pickup, pylon, rig.Reach), Smooth(rig.Unfold));
            }
            PoseRounds(rig, v, px, carrying, t, body);
            rig.ArmStowed = rig.Reach < 0.05f && rig.Swing < 0.05f;
        }

        /// <summary>
        /// Fuel boom aimed at the receptacle: the yaw turns the base so the boom's bearing matches, the pitch tips the boom onto the
        /// point, the telescope runs out along it. The boom blends from its stowed direction by <paramref name="weight"/>.
        /// </summary>
        private static void PoseBoom(Rig rig, Quaternion body, Vector3 point, float weight)
        {
            if (!rig.FuelBoom.Ok || !rig.Nozzle.Ok) return;
            Vector3 up = body * Vector3.up;
            Vector3 pivot = rig.FuelBoom.T.position;
            Vector3 stowed = rig.Nozzle.T.position - pivot;
            if (stowed.sqrMagnitude < 1e-4f) return;
            stowed.Normalize();
            Vector3 toPoint = point - pivot;
            float dist = toPoint.magnitude;
            if (dist < 1e-3f) return;
            Vector3 dir = Vector3.Slerp(stowed, toPoint / dist, weight);
            if (rig.FuelBase.Ok)
            {
                Vector3 from = Vector3.ProjectOnPlane(stowed, up), to = Vector3.ProjectOnPlane(dir, up);
                if (from.sqrMagnitude > 0.01f && to.sqrMagnitude > 0.01f)
                    rig.FuelBase.T.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(from, to, up), up) * rig.FuelBase.T.rotation;
            }
            pivot = rig.FuelBoom.T.position; // the yaw may have moved it a little
            Vector3 now = rig.Nozzle.T.position - pivot;
            if (now.sqrMagnitude > 1e-4f) rig.FuelBoom.T.rotation = Quaternion.FromToRotation(now, dir) * rig.FuelBoom.T.rotation;
            if (rig.FuelTele.Ok && rig.FuelTele.T.parent != null)
            {
                float extension = Mathf.Clamp(dist - rig.BoomLen, 0f, rig.TeleMax) * weight;
                rig.FuelTele.T.localPosition = rig.FuelTele.Pos + rig.FuelTele.T.parent.InverseTransformVector(dir * extension);
            }
        }

        /// <summary>
        /// Loader arm reaching <paramref name="target"/> with its gripper, the elbow trailing aft and up (never into the receiver). The yaw at the
        /// base and the pitch at the shoulder aim the upper arm at the elbow, the forearm at the target; two passes settle the shoulder's
        /// small offset from the yaw axis. The result blends from the stowed (folded) pose by <paramref name="weight"/>, so it unfolds
        /// without a pop.
        /// </summary>
        private static void PoseLoader(Rig rig, Quaternion body, Vector3 target, float weight)
        {
            if (!rig.LoadUpper.Ok || !rig.LoadFore.Ok || !rig.Gripper.Ok) return;
            Vector3 up = body * Vector3.up;
            Vector3 pole = (body * Vector3.back + up * 0.5f).normalized;
            Vector3 elbow = Vector3.zero, end = Vector3.zero;
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 shoulder = rig.LoadUpper.T.position;
                elbow = ArmIK.Elbow(shoulder, target, pole, rig.Upper, rig.Lower, out end);
                Vector3 now = rig.LoadFore.T.position - shoulder, want = elbow - shoulder;
                if (pass == 0 && rig.LoadBase.Ok)
                {
                    Vector3 from = Vector3.ProjectOnPlane(now, up), to = Vector3.ProjectOnPlane(want, up);
                    if (from.sqrMagnitude > 0.01f && to.sqrMagnitude > 0.01f)
                        rig.LoadBase.T.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(from, to, up), up) * rig.LoadBase.T.rotation;
                    shoulder = rig.LoadUpper.T.position;
                    now = rig.LoadFore.T.position - shoulder;
                    want = elbow - shoulder;
                }
                if (now.sqrMagnitude > 1e-4f && want.sqrMagnitude > 1e-4f)
                    rig.LoadUpper.T.rotation = Quaternion.FromToRotation(now, want) * rig.LoadUpper.T.rotation;
            }
            Vector3 foreNow = rig.Gripper.T.position - rig.LoadFore.T.position, foreWant = end - rig.LoadFore.T.position;
            if (foreNow.sqrMagnitude > 1e-4f && foreWant.sqrMagnitude > 1e-4f)
                rig.LoadFore.T.rotation = Quaternion.FromToRotation(foreNow, foreWant) * rig.LoadFore.T.rotation;
            if (weight >= 0.999f) return;
            Blend(rig.LoadBase, weight);
            Blend(rig.LoadUpper, weight);
            Blend(rig.LoadFore, weight);
        }

        private static void Blend(Joint j, float weight)
        {
            if (j.Ok) j.T.localRotation = Quaternion.Slerp(j.Rot, j.T.localRotation, weight);
        }

        /// <summary>
        /// The carousel presents the slot being served to the loader and indexes to the next one in the last 0.5 s of a hand-off; between
        /// hand-offs it sits on the next full slot (and follows a restock smoothly).
        /// </summary>
        private static void PoseMagazine(Rig rig, SkywellView v, bool stationWork, float t, float dt)
        {
            if (!rig.Magazine.Ok) return;
            float want;
            if (rig.CarrySlot >= 0 && v.Job != PitJobKind.None && v.Job != PitJobKind.Fuel)
            {
                int s = Mathf.Clamp(rig.CarrySlot, 0, SkywellMagazine.Rounds - 1);
                want = Mathf.Lerp(rig.SlotAngle[s], rig.SlotAngle[s + 1], stationWork ? SkywellHandoff.IndexStep(t) : 0f);
                rig.MagAngle = want;
            }
            else
            {
                want = rig.SlotAngle[Mathf.Clamp(SkywellMagazine.NextSlot(v.Rounds), 0, SkywellMagazine.Rounds)];
                rig.MagAngle = Mathf.MoveTowards(rig.MagAngle, want, 240f * dt);
            }
            rig.Magazine.Hinge(rig.Body0, Vector3.forward, rig.MagAngle);
        }

        /// <summary>Spent slots are empty; the round being served rides the gripper from the carousel up to the pylon and is gone when the latch ends.</summary>
        private static void PoseRounds(Rig rig, SkywellView v, Aircraft px, bool carrying, float t, Quaternion body)
        {
            bool committed = rig.CarrySlot >= 0 && !SkywellHandoff.Carrying(t) && rig.ArmClock > 0f;
            for (int i = 0; i < SkywellMagazine.Rounds; i++)
            {
                Joint round = rig.Rounds[i];
                if (!round.Ok) continue;
                bool shown = SkywellMagazine.Full(i, v.Rounds) && !(i == rig.CarrySlot && committed);
                if (shown != rig.RoundShown[i])
                {
                    rig.RoundShown[i] = shown;
                    round.T.gameObject.SetActive(shown);
                }
            }
            if (!carrying || !rig.Gripper.Ok || px == null || rig.CarrySlot < 0 || rig.CarrySlot >= SkywellMagazine.Rounds) return;
            Joint carried = rig.Rounds[rig.CarrySlot];
            if (!carried.Ok) return;
            // The round keeps its stowed attitude relative to the tender, and turns to the receiver's as it rises to the rail.
            Quaternion rel = Quaternion.Inverse(body) * carried.T.rotation;
            float lift = SkywellHandoff.Reach(t);
            carried.T.SetPositionAndRotation(rig.Gripper.T.position, Quaternion.Slerp(body * rel, px.transform.rotation * rel, lift));
        }
    }
}
