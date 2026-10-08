using System;
using BoscaliSummer.Modules.Cinematography.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Cinematography.Runtime
{
    // Native LateUpdate invokes this before CheckOriginShift and weather rendering. No Harmony camera writer.
    internal sealed class CinematicCameraState : CameraBaseState
    {
        private readonly ShotPlan plan;
        private readonly Func<float> clock;
        private readonly Action<float> frame;
        private readonly Func<bool> permitted;
        private readonly Func<Vector3, bool> clear;
        private readonly Action<string> fault;
        private readonly Action complete;
        private readonly Func<ShotPose?> anchor;
        private readonly GlobalPosition baselinePosition;
        private readonly Quaternion baselineRotation;
        private readonly float baselineFov, baselineDesired;
        private readonly bool baselineInputs;
        private readonly Vector3 baselineVelocity;
        private float previousClock, elapsed;
        private ShotPose lastPose;
        private float lastTime;
        private bool wrote, stopping, terminal, finalFrame;
        private Vector3 writtenVelocity;
        internal bool OwnsCamera { get; private set; }
        internal bool ManualPaused { get; set; }
        internal bool Seek(float seconds)
        {
            if(!OwnsCamera || terminal || !ShotPlan.Finite(seconds) || seconds<0 || seconds>plan.duration) return false;
            float now=clock();if(!ShotPlan.Finite(now)) return false;
            elapsed=seconds;previousClock=now;finalFrame=false;return true;
        }

        internal CinematicCameraState(CameraStateManager cam, ShotPlan shot, Func<float> missionClock,
            Action<float> onFrame, Func<bool> allowed, Func<Vector3, bool> pathClear, Action<string> onFault, Action onComplete,
            Func<ShotPose?> anchorPose=null)
        {
            plan = shot; clock = missionClock; frame = onFrame; permitted = allowed; clear = pathClear;
            fault = onFault; complete = onComplete;
            anchor=anchorPose;
            cam.GetCameraPosition(out baselinePosition, out baselineRotation);
            baselineFov = cam.mainCamera.fieldOfView; baselineDesired = cam.desiredFOV;
            baselineInputs = cam.allowInputs; baselineVelocity = cam.cameraVelocity;
            previousClock = clock();
        }

        public override void EnterState(CameraStateManager cam)
        { OwnsCamera = true; cam.allowInputs = false; }

        public override void LeaveState(CameraStateManager cam)
        {
            OwnsCamera = false;
            if (wrote)
            {
                cam.GetCameraPosition(out GlobalPosition now, out Quaternion rotation);
                var position = new Vector3(lastPose.Position.X, lastPose.Position.Y, lastPose.Position.Z);
                if ((new Vector3(now.x, now.y, now.z) - position).sqrMagnitude < .0001f &&
                    Math.Abs(Quaternion.Dot(rotation, Rotation(lastPose))) > .99999f)
                    cam.transform.SetPositionAndRotation(baselinePosition.ToLocalPosition(), baselineRotation);
                if (cam.mainCamera.fieldOfView == lastPose.Fov) cam.mainCamera.fieldOfView = baselineFov;
                if (cam.desiredFOV == lastPose.Fov) cam.desiredFOV = baselineDesired;
                if ((cam.cameraVelocity - writtenVelocity).sqrMagnitude < .0001f) cam.cameraVelocity = baselineVelocity;
            }
            if (!cam.allowInputs) cam.allowInputs = baselineInputs;
            if (!stopping) Fail("CAMERA OWNERSHIP LOST");
        }

        internal void RequestStop() { stopping = true; }
        internal void ReturnToFree(CameraStateManager cam)
        {
            if (!OwnsCamera || cam.currentState != this) return;
            stopping = true;
            // Native free EnterState looks at this stale history even when already detached.
            Unit previous = cam.previousFollowingUnit;
            cam.previousFollowingUnit = null;
            try
            {
                cam.SwitchState(cam.freeState);
                if (cam.currentState == cam.freeState) cam.desiredFOV = baselineDesired;
            }
            finally
            {
                if ((cam.currentState == cam.freeState || cam.currentState == this) && cam.previousFollowingUnit == null)
                    cam.previousFollowingUnit = previous;
            }
        }

        public override void UpdateState(CameraStateManager cam)
        {
            if (terminal || !OwnsCamera) return;
            try
            {
                if (!permitted()) { Fail("PRODUCTION CONTEXT LOST"); return; }
                float now = clock();
                if (!ShotPlan.Finite(now) || now < previousClock) { Fail("MISSION CLOCK CHANGED"); return; }
                bool paused = Time.timeScale == 0 || ManualPaused;
                if (!paused) elapsed += (now - previousClock)*(plan.options?.playbackRate ?? 1);
                previousClock = now;
                float time = elapsed;
                if (finalFrame && !paused) { terminal = true; complete(); return; }
                ShotPose? actor=anchor?.Invoke();
                if(plan.anchorId!=null && !actor.HasValue) { Fail("ANCHOR LOST");return; }
                ShotPose pose = plan.SamplePresentation(time,actor);
                Vector3 position = new GlobalPosition(pose.Position.X, pose.Position.Y, pose.Position.Z).ToLocalPosition();
                if (!clear(position)) { Fail("CAMERA PATH BLOCKED"); return; }
                if (wrote && time > lastTime)
                {
                    var velocity = (pose.Position - lastPose.Position) / (time - lastTime);
                    writtenVelocity = new Vector3(velocity.X, velocity.Y, velocity.Z);
                }
                else writtenVelocity = Vector3.zero;
                cam.cameraVelocity = writtenVelocity;
                cam.transform.SetPositionAndRotation(position, Rotation(pose));
                cam.desiredFOV = cam.mainCamera.fieldOfView = pose.Fov;
                lastPose = pose; lastTime = time; wrote = true;
                finalFrame = time >= plan.duration;
                frame(Math.Min(time, plan.duration));
            }
            catch (Exception e) { Fail("CAMERA FAULT: " + e.GetType().Name); }
        }

        public override void FixedUpdateState(CameraStateManager cam) { }
        private static Quaternion Rotation(ShotPose pose) => new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
        private void Fail(string reason) { if (terminal) return; terminal = true; fault(reason); }
    }
}
