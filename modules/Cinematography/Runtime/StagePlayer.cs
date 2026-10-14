using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Cinematography.Domain;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vec3 = System.Numerics.Vector3;

namespace BoscaliSummer.Modules.Cinematography.Runtime
{
    /// <summary>
    /// The cutscene camera mission scripts play (<see cref="ICinematicStage"/>): the director's shot rigs and native camera state, without its private-production
    /// tools. It takes only the detached free camera of a viewer who is not flying, plays on the real-time clock, never slows the simulation, and returns the
    /// camera to free exactly as the director does. A shot whose path is blocked or whose target is gone is skipped, never forced.
    /// </summary>
    internal sealed class StagePlayer : MonoBehaviour, ISceneService, ICinematicStage
    {
        private const int PreflightSamples = 32;

        private readonly CameraPathClearance clearance = new CameraPathClearance();
        private IReadOnlyList<StageShot> shots;
        private Action<int> onShot;
        private Action onEnd;
        private CameraStateManager rig;
        private CinematicCameraState state;
        private int index;
        private bool pauseWas, faulted;

        public bool Playing => shots != null;

        public bool Play(IReadOnlyList<StageShot> edit, Action<int> shotStarted, Action ended)
        {
            if (Playing || edit == null || edit.Count == 0 || !MissionManager.IsRunning) return false;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null || cam.mainCamera == null || cam.currentState != cam.freeState || cam.followingUnit != null) return false;
            if (GameManager.GetLocalAircraft(out Aircraft own) && own != null) return false;
            if (CinematicDirector.Active != null && CinematicDirector.Active.Playing) return false;
            rig = cam;
            shots = edit;
            onShot = shotStarted;
            onEnd = ended;
            index = -1;
            faulted = false;
            pauseWas = GameplayUI.AllowPauseKeybind;
            GameplayUI.AllowPauseKeybind = false;
            Next();
            return true;
        }

        public void Stop()
        {
            if (Playing) End();
        }

        private void Update()
        {
            if (!Playing) return;
            bool owned = state != null && rig != null && rig.currentState == state;
            if (!owned) { End(); return; }   // the game or the player took the camera
            if (!faulted) return;
            faulted = false;
            Release();
            Next();
        }

        /// <summary>Called from inside the finished shot's own update; the director chains its clips the same way.</summary>
        private void Advance()
        {
            Release();
            Next();
        }

        private void Next()
        {
            while (shots != null && ++index < shots.Count)
            {
                if (!TryStart(shots[index])) continue;
                onShot?.Invoke(index);
                return;
            }
            if (shots != null) End();
        }

        private void Release()
        {
            CinematicCameraState ending = state;
            state = null;
            clearance.Reset();
            if (ending == null) return;
            if (rig != null && rig.currentState == ending) ending.ReturnToFree(rig);
            else ending.RequestStop();
        }

        private void End()
        {
            Release();
            if (!GameplayUI.AllowPauseKeybind) GameplayUI.AllowPauseKeybind = pauseWas;
            Action ended = onEnd;
            shots = null;
            onShot = null;
            onEnd = null;
            rig = null;
            ended?.Invoke();
        }

        private bool TryStart(StageShot shot)
        {
            Unit actor = null;
            ShotPlan plan = Build(shot, ref actor);
            if (plan == null || !ShotPlan.Validate(plan, out _)) return false;
            Func<ShotPose?> anchor = () => actor == null ? null : CinematicDirector.ActorPose(actor);
            // The same bounded preflight the director runs: a blocked path is found before the camera moves.
            clearance.Reset();
            for (int i = 0; i <= PreflightSamples; i++)
            {
                ShotPose pose = plan.SamplePresentation(plan.duration * i / PreflightSamples, anchor());
                if (clearance.Clear(new GlobalPosition(pose.Position.X, pose.Position.Y, pose.Position.Z).ToLocalPosition())) continue;
                clearance.Reset();
                return false;
            }
            clearance.Reset();
            var next = new CinematicCameraState(rig, plan, () => Time.unscaledTime, _ => { }, () => MissionManager.IsRunning,
                clearance.Clear, _ => faulted = true, Advance, anchor);
            rig.SwitchState(next);
            if (rig.currentState != next) { next.RequestStop(); return false; }
            state = next;
            next.UpdateState(rig);
            return true;
        }

        private static ShotPlan Build(StageShot shot, ref Unit actor)
        {
            string mission = MissionManager.CurrentMission?.Name;
            if (string.IsNullOrWhiteSpace(mission) || mission.Length > 256) mission = "mission";
            string scene = SceneManager.GetActiveScene().name;
            if (string.IsNullOrWhiteSpace(scene) || scene.Length > 256) scene = "scene";
            float seconds = Mathf.Clamp(shot.Seconds, 0.5f, 120f);
            ShotPlan plan;
            switch (shot.Kind)
            {
                case StageShotKind.OrbitAirbase:
                    if (shot.Target == null || !FactionRegistry.airbaseLookup.TryGetValue(shot.Target, out Airbase airbase) ||
                        airbase == null || airbase.center == null) return null;
                    GlobalPosition centre = airbase.center.position.ToGlobalPosition();
                    plan = ShotRigs.Orbit("stage", mission, scene, new Vec3(centre.x, centre.y, centre.z), shot.Radius, shot.Height, seconds,
                        shot.FromDegrees, shot.ToDegrees);
                    break;
                case StageShotKind.OrbitUnit:
                    actor = shot.Target == null ? null : CinematicDirector.FindActor(shot.Target);
                    if (actor == null) return null;
                    plan = ShotRigs.Orbit("stage", mission, scene, Vec3.Zero, shot.Radius, shot.Height, seconds, shot.FromDegrees, shot.ToDegrees);
                    plan.anchorId = shot.Target;
                    // The orbit keeps its compass bearings while the unit turns under it.
                    plan.options.stabilizeAnchor = true;
                    break;
                case StageShotKind.Chase:
                    actor = shot.Target == null ? null : CinematicDirector.FindActor(shot.Target);
                    if (actor == null) return null;
                    plan = ShotRigs.Follow("stage", mission, scene, shot.Target, new Vec3(shot.X, shot.Y, shot.Z), seconds);
                    break;
                case StageShotKind.OrbitPoint:
                    plan = ShotRigs.Orbit("stage", mission, scene, new Vec3(shot.X, shot.Y, shot.Z), shot.Radius, shot.Height, seconds,
                        shot.FromDegrees, shot.ToDegrees);
                    break;
                default:
                    var look = new Vec3(shot.LookX, shot.LookY, shot.LookZ);
                    plan = new ShotPlan
                    {
                        id = "stage", mission = mission, scene = scene, duration = seconds, smooth = true,
                        options = new ShotOptions { lookAtTarget = true, targetX = look.X, targetY = look.Y, targetZ = look.Z },
                        keys = new[] { Key(new Vec3(shot.X, shot.Y, shot.Z), look, 0f), Key(new Vec3(shot.ToX, shot.ToY, shot.ToZ), look, seconds) }
                    };
                    break;
            }
            float fov = Mathf.Clamp(shot.Fov <= 0f ? 45f : shot.Fov, 15f, 80f);
            foreach (ShotKey key in plan.keys) key.fov = fov;
            plan.options.realtimeClock = true;
            float shake = Mathf.Clamp01(shot.Shake);
            plan.options.shakeMeters = 0.75f * shake;
            plan.options.shakeDegrees = 3f * shake;
            plan.options.rollDegrees = Mathf.Clamp(shot.Roll, -20f, 20f);
            plan.options.seed = (int)(shot.X + shot.Z) & 0x7fff;
            return plan;
        }

        private static ShotKey Key(Vec3 position, Vec3 look, float time)
        {
            System.Numerics.Quaternion q = ShotOptions.Look(look - position);
            return new ShotKey { x = position.X, y = position.Y, z = position.Z, qX = q.X, qY = q.Y, qZ = q.Z, qW = q.W, time = time };
        }

        public void ResetForScene() => Stop();
        private void OnDisable() => Stop();
    }
}
