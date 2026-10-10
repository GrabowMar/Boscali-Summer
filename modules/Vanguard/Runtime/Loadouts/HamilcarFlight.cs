using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// CG-9 HAMILCAR (server). The host flies the glider with a velocity servo over the native airframe (the SENTINEL
    /// pattern; vanilla Steering/ApplyAero still run after this hook). Release falls clear of the hold, Glide follows the
    /// straight line to a landing spot short of the designated target, Final and Flare bring it down, Slide runs it out on
    /// its belly, Unload turns the Hexhound in the pod into a live unit and the empty shell stays as a Husk. No landing
    /// zone, or anything that kills it before the vehicle is out, loses both. The propeller is a picture (HamilcarVisuals):
    /// the airframe has no thrust, the longer reach is HamilcarProfile.GlideRatio.
    /// </summary>
    internal sealed partial class VanguardFlight
    {
        private const string HamilcarCargo = "UGV1_grenade";
        private const float HamilcarSecondLook = 2500f; // the landing zone is searched again this far out, then locked
        private const float HamilcarLookahead = 300f;   // line following: steer at the approach line this far ahead
        private const float HamilcarSettle = 1f;        // seconds to close a height error
        private const float HamilcarMaxSink = 0.6f;     // steepest descent, metres down per metre flown
        private const float HamilcarMaxClimb = 15f;     // m/s, over rising ground
        private const float HamilcarLift = 20f;        // m/s2 of MaxAccel the height channel may take before heading and speed
        private const float HamilcarLevelRate = 60f;    // deg/s onto the ground normal after touchdown

        /// <summary>Sim seams (VanguardAutomation "hamilcar" tune): the husk lifetime, and hold-position for an AI launcher's vehicle.</summary>
        internal static float HamilcarHusk = HamilcarProfile.HuskSeconds;
        internal static bool HamilcarHold;
        /// <summary>The most recent glider, kept after its shell is gone: the sim reads its landing through <see cref="HamilcarReport"/>.</summary>
        internal static VanguardFlight HamilcarLast;
        private static int hamilcarSerial;

        private HamilcarPhase hamilcarPhase;
        private GlobalPosition hamilcarAim, hamilcarSpot, hamilcarRest;
        private Vector3 hamilcarApproach, hamilcarHeading, hamilcarGround, hamilcarNormal = Vector3.up;
        private Quaternion hamilcarRestRot;
        private bool hamilcarNoLz, hamilcarShort, hamilcarLocked, hamilcarDry;
        private float hamilcarPhaseAt, hamilcarDist, hamilcarAgl = 1000f, hamilcarLineDist, hamilcarLineHeight;
        private float hamilcarReleaseSpeed, hamilcarSeat, hamilcarSlid, hamilcarNextProbe;
        private float hamilcarLandDist, hamilcarTouchSink, hamilcarTouchSpeed, hamilcarDrift;
        private Unit hamilcarVehicle;

        /// <summary>From the final approach on the flight handles the ground itself: vanilla Missile.DetectCollisions is skipped.</summary>
        internal bool OwnsGroundContact => role == VanguardRole.Lander && hamilcarPhase >= HamilcarPhase.Final && hamilcarPhase != HamilcarPhase.Lost;

        /// <summary>The exact phase for this peer's visuals and HUD. False for any other loadout. Only the host has a flight; clients estimate.</summary>
        internal bool HamilcarState(out HamilcarPhase phase, out float phaseAge, out float rangeToSpot, out bool isShort, out bool noLz)
        {
            phase = hamilcarPhase;
            phaseAge = missile.timeSinceSpawn - hamilcarPhaseAt;
            rangeToSpot = hamilcarDist;
            isShort = hamilcarShort;
            noLz = hamilcarNoLz;
            return role == VanguardRole.Lander;
        }

        internal static void ResetHamilcar()
        {
            HamilcarHusk = HamilcarProfile.HuskSeconds;
            HamilcarHold = false;
            HamilcarLast = null;
        }

        private void InitHamilcar()
        {
            hamilcarHeading = bearing;
            hamilcarReleaseSpeed = missile.rb.velocity.magnitude;
            // The live unit appears where the visual one stops rolling: RollOff ahead of the cradle seat.
            Transform payload = missile.transform.Find("Payload");
            hamilcarSeat = payload != null ? payload.localPosition.z : 0f;
            // No designation: land straight ahead at the end of the reach.
            hamilcarAim = launchPos + bearing * HamilcarProfile.Reach(launchPos.y);
            hamilcarAim.y = 0f;
            HamilcarPlan();
            HamilcarLast = this;
            missile.onDisableUnit += _ => { if (hamilcarPhase != HamilcarPhase.Husk) VanguardStats.Bump("hamilcar.lost"); };
            VanguardStats.Bump("hamilcar.released");
        }

        /// <summary>Sim: re-aim at a bare map point (no unit). False for any other loadout.</summary>
        internal bool HamilcarAimAt(GlobalPosition point)
        {
            if (role != VanguardRole.Lander) return false;
            target = null;
            hamilcarAim = point;
            hamilcarApproach = Vector3.zero; // a fresh plan, not a second look
            HamilcarPlan();
            return true;
        }

        // Stopped dead in the air is a loss (vanilla collisions zero the velocity on a hillside); on the ground it is the plan.
        private float HamilcarMinSpeed() => hamilcarPhase >= HamilcarPhase.Slide ? 0f : 20f;

        // 900 s to get down. Once down only the husk timer takes it away: the lifetime rule ends in a blast, a landed glider never does.
        private float HamilcarLifetime() => hamilcarPhase >= HamilcarPhase.Slide ? float.MaxValue : 900f;

        // Reach, landing centre and the landing-zone search: at release, once more HamilcarSecondLook out, and from HamilcarAimAt.
        private void HamilcarPlan()
        {
            if (target != null && !target.disabled) hamilcarAim = TargetPosition(out _);
            GlobalPosition pos = missile.GlobalPosition();
            float reach = HamilcarProfile.Reach(launchPos.y - hamilcarAim.y);
            var centre = new GlobalPosition(HamilcarProfile.Centre(launchPos.AsVector3(), hamilcarAim.AsVector3(), reach, out bool isShort));
            bool found = HamilcarSearch(pos, centre, out GlobalPosition spot, out Vector3 approach);
            if (!found && !hamilcarNoLz && hamilcarApproach != Vector3.zero) return; // the second look found nothing: keep the first spot
            if (isShort && !hamilcarShort) VanguardStats.Bump("hamilcar.short");
            if (!found && !hamilcarNoLz) VanguardStats.Bump("hamilcar.noLz");
            hamilcarShort = isShort;
            hamilcarNoLz = !found;
            hamilcarSpot = spot;
            hamilcarApproach = approach;
            HamilcarLine(pos);
        }

        // First candidate around the centre with a dry, level run-out and room for the vehicle off the nose.
        // None: the centre itself and no landing zone (it flies on and is lost at LostHeight).
        private bool HamilcarSearch(GlobalPosition from, GlobalPosition centre, out GlobalPosition spot, out Vector3 approach)
        {
            Vector3 origin = from.ToLocalPosition(), middle = centre.ToLocalPosition();
            Vector3 forward = new Vector3(middle.x - origin.x, 0f, middle.z - origin.z);
            forward = forward.sqrMagnitude > 1f ? forward.normalized : bearing;
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            spot = centre;
            approach = forward;
            if (!Encyclopedia.Lookup.TryGetValue(HamilcarCargo, out UnitDefinition cargo)) return false;
            for (int i = 0; i < HamilcarProfile.CandidateCount; i++)
            {
                Vector2 offset = HamilcarProfile.Candidate(i, seed);
                Vector3 touch = middle + right * offset.x + forward * offset.y;
                touch.y = Datum.LocalSeaY + 1400f; // DryGround probes from +500 m: start above any hill
                Vector3 run = new Vector3(touch.x - origin.x, 0f, touch.z - origin.z);
                if (Vector3.Dot(run, forward) < 1f) continue; // never a spot behind the glider (the back line of a close centre)
                run.Normalize();
                if (!GroundPlacement.DryGround(touch, out Vector3 a) ||
                    !GroundPlacement.DryGround(touch + run * (HamilcarProfile.SlideDistance * 0.5f), out Vector3 b) ||
                    !GroundPlacement.DryGround(touch + run * HamilcarProfile.SlideDistance, out Vector3 c) ||
                    Mathf.Abs(a.y - b.y) > 2f || Mathf.Abs(b.y - c.y) > 2f || Mathf.Abs(a.y - c.y) > 2f ||
                    !GroundPlacement.TryPlace(cargo, c + run * (hamilcarSeat + HamilcarProfile.RollOff), Quaternion.LookRotation(run), out _)) continue;
                spot = a.ToGlobalPosition();
                approach = run;
                return true;
            }
            return false;
        }

        // Metres still to fly along the approach line to the touchdown point (negative once past it).
        private float HamilcarAlong(GlobalPosition pos)
        {
            Vector3 to = hamilcarSpot - pos;
            return to.x * hamilcarApproach.x + to.z * hamilcarApproach.z;
        }

        // The glide line runs straight from here down to the final-approach gate.
        private void HamilcarLine(GlobalPosition pos)
        {
            hamilcarLineDist = Mathf.Max(HamilcarAlong(pos), HamilcarProfile.GateRange + 1f);
            hamilcarLineHeight = pos.y - hamilcarSpot.y;
        }

        // Commanded height above the touchdown point. Flying away from it (a release heading the wrong way) holds the release height.
        private float HamilcarPath(float dist) => HamilcarProfile.PathHeight(hamilcarPhase, dist,
            HamilcarProfile.GateHeight + (hamilcarLineHeight - HamilcarProfile.GateHeight) *
            (Mathf.Min(dist, hamilcarLineDist) - HamilcarProfile.GateRange) / (hamilcarLineDist - HamilcarProfile.GateRange));

        // Ground under a point: terrain or a static, else the sea surface (false).
        private static bool HamilcarGround(Vector3 at, out Vector3 point, out Vector3 normal)
        {
            if (Physics.Raycast(at + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 3000f, (int)PhysicsLayers.StaticsMask) &&
                hit.point.y > Datum.LocalSeaY)
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }
            point = new Vector3(at.x, Datum.LocalSeaY, at.z);
            normal = Vector3.up;
            return false;
        }

        // Acceleration over the airframe. The host keeps the donor's gravity flag, so gravity is cancelled here.
        private static void HamilcarPush(Rigidbody rb, Vector3 accel)
        {
            if (rb.useGravity) accel -= Physics.gravity;
            rb.AddForce(accel, ForceMode.Acceleration);
        }

        private void TickHamilcar()
        {
            Rigidbody rb = missile.rb;
            float age = missile.timeSinceSpawn, dt = Time.fixedDeltaTime;
            Vector3 here = missile.transform.position;
            GlobalPosition pos = here.ToGlobalPosition();
            missile.SetThrottle(0f);
            if (!hamilcarLocked && hamilcarPhase == HamilcarPhase.Glide && HamilcarAlong(pos) <= HamilcarSecondLook)
            {
                hamilcarLocked = true;
                HamilcarPlan();
            }
            hamilcarDist = Mathf.Max(0f, HamilcarAlong(pos));
            // Ground below: every tick once it is low, at the plan rate up high, not at all once it is pinned.
            if (hamilcarPhase < HamilcarPhase.Unload && (hamilcarPhase >= HamilcarPhase.Final || age >= hamilcarNextProbe))
            {
                hamilcarNextProbe = age + PlanInterval;
                hamilcarDry = HamilcarGround(here, out hamilcarGround, out hamilcarNormal);
                hamilcarAgl = here.y - hamilcarGround.y;
            }
            float slide = HamilcarProfile.PhaseSpeed(HamilcarPhase.Slide, hamilcarSlid);
            HamilcarPhase next = HamilcarProfile.Next(hamilcarPhase, age, hamilcarDist, hamilcarAgl, slide, age - hamilcarPhaseAt, !hamilcarNoLz);
            if (next == HamilcarPhase.Slide && hamilcarPhase == HamilcarPhase.Flare && !hamilcarDry) next = HamilcarPhase.Lost; // came down on water
            if (next != hamilcarPhase)
            {
                HamilcarEnter(next, here);
                if (missile.disabled) return;
            }
            switch (hamilcarPhase)
            {
                case HamilcarPhase.Release: // falls clear of the hold on the airframe's own physics while the wing swings out
                    missile.SetAimpoint(pos + hamilcarHeading * 400f, Vector3.zero);
                    break;
                case HamilcarPhase.Glide:
                case HamilcarPhase.Final:
                case HamilcarPhase.Flare:
                    HamilcarFly(rb, pos, dt);
                    break;
                case HamilcarPhase.Slide:
                    HamilcarSlide(rb, here, slide, dt);
                    break;
                default:
                    if (hamilcarPhase == HamilcarPhase.Husk && age - hamilcarPhaseAt >= HamilcarHusk) HamilcarRemove();
                    else HamilcarPin(rb, here);
                    break;
            }
        }

        private void HamilcarEnter(HamilcarPhase next, Vector3 here)
        {
            hamilcarPhase = next;
            hamilcarPhaseAt = missile.timeSinceSpawn;
            GlobalPosition pos = here.ToGlobalPosition();
            switch (next)
            {
                case HamilcarPhase.Glide:
                    HamilcarLine(pos); // the glide starts where the wing is out, not at the hold
                    break;
                case HamilcarPhase.Slide:
                    Vector3 velocity = missile.rb.velocity, off = hamilcarAim - pos;
                    hamilcarTouchSink = -velocity.y;
                    hamilcarTouchSpeed = new Vector2(velocity.x, velocity.z).magnitude;
                    hamilcarLandDist = new Vector2(off.x, off.z).magnitude;
                    missile.SetTorque(0f, 0f); // vanilla Steering/ApplyAero still run after this hook: no steering torque on the ground
                    VanguardStats.Bump("hamilcar.landed");
                    break;
                case HamilcarPhase.Unload:
                    hamilcarRest = new Vector3(here.x, hamilcarGround.y + HamilcarProfile.RestHeight, here.z).ToGlobalPosition();
                    hamilcarRestRot = missile.rb.rotation;
                    break;
                case HamilcarPhase.Husk:
                    HamilcarUnload();
                    break;
                case HamilcarPhase.Lost:
                    Detonate(); // unarmed, yield 1: a puff
                    break;
            }
        }

        // Glide, Final, Flare: heading follows the approach line (turn-rate limited), speed and height follow the profile.
        private void HamilcarFly(Rigidbody rb, GlobalPosition pos, float dt)
        {
            Vector3 toSpot = hamilcarSpot - pos;
            Vector3 want = new Vector3(toSpot.x, 0f, toSpot.z) - hamilcarApproach * (HamilcarAlong(pos) - HamilcarLookahead);
            float turn = HamilcarProfile.MaxTurnRate * dt;
            hamilcarHeading = Quaternion.Euler(0f, Mathf.Clamp(Vector3.SignedAngle(hamilcarHeading, want, Vector3.up), -turn, turn), 0f) * hamilcarHeading;

            float speed = Mathf.Min(HamilcarProfile.PhaseSpeed(hamilcarPhase, hamilcarDist), HamilcarProfile.SpeedCap(hamilcarReleaseSpeed, launchPos.y - pos.y));
            bool flare = hamilcarPhase == HamilcarPhase.Flare;
            // Height over the touchdown point; in the flare over the ground below, so the keel meets whatever is there.
            float height = flare ? hamilcarAgl - HamilcarProfile.RestHeight : pos.y - hamilcarSpot.y;
            float path = HamilcarPath(hamilcarDist), slope = HamilcarPath(hamilcarDist + 1f) - path;
            float floor = hamilcarPhase == HamilcarPhase.Final ? HamilcarProfile.FlareHeight : HamilcarProfile.GateHeight;
            if (!flare) path = Mathf.Max(path, height - hamilcarAgl + floor); // never under the ground below
            float climb = Mathf.Clamp((path - height) / HamilcarSettle - slope * speed, -HamilcarMaxSink * speed, HamilcarMaxClimb);
            // No more sink than the servo can stop: a late flare still touches down softly, and a steep glide line does not carry
            // its sink through the gate into the ground (it rounds out above the floor and makes up the height on final).
            climb = Mathf.Max(climb, flare ? -(HamilcarProfile.TouchSink + 0.6f * hamilcarAgl)
                : -HamilcarProfile.SinkLimit(Mathf.Min(height, hamilcarAgl) - floor));
            Vector3 velocity = hamilcarHeading * speed + Vector3.up * climb;
            // Height first, heading and speed share what is left of MaxAccel: a hard turn or a long brake must not starve the flare.
            Vector3 accel = (velocity - rb.velocity) / dt;
            float lift = Mathf.Clamp(accel.y, -HamilcarLift, HamilcarLift);
            accel.y = 0f;
            HamilcarPush(rb, Vector3.ClampMagnitude(accel, Mathf.Sqrt(HamilcarProfile.MaxAccel * HamilcarProfile.MaxAccel - lift * lift)) + Vector3.up * lift);
            // Vanilla steering keeps the nose on the flight path; level through the flare.
            missile.SetAimpoint(pos + (flare ? hamilcarHeading : velocity.normalized) * 400f, Vector3.zero);
        }

        // On its belly: the speed comes off along the heading, the hull follows the ground and cannot bounce
        // (the velocity is set outright, its vertical part puts the keel back on the ground within the tick).
        private void HamilcarSlide(Rigidbody rb, Vector3 here, float speed, float dt)
        {
            hamilcarSlid += speed * dt;
            Vector3 along = Vector3.ProjectOnPlane(hamilcarHeading, hamilcarNormal).normalized;
            rb.velocity = along * speed + Vector3.up * ((hamilcarGround.y + HamilcarProfile.RestHeight - here.y) / dt);
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, Quaternion.LookRotation(along, hamilcarNormal), HamilcarLevelRate * dt));
            HamilcarPush(rb, Vector3.zero);
        }

        // Stopped: held on its rest pose every tick. The drift is how far physics moved it between two ticks.
        private void HamilcarPin(Rigidbody rb, Vector3 here)
        {
            Vector3 rest = hamilcarRest.ToLocalPosition();
            hamilcarDrift = Mathf.Max(hamilcarDrift, (here - rest).magnitude);
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MovePosition(rest);
            rb.MoveRotation(hamilcarRestRot);
            HamilcarPush(rb, Vector3.zero);
        }

        // End of the unload: the Hexhound becomes a live unit off the nose, hold-position for a player's glider (the vanilla cargo rule).
        private void HamilcarUnload()
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !Encyclopedia.Lookup.TryGetValue(HamilcarCargo, out UnitDefinition cargo)) return;
            Vector3 forward = hamilcarRestRot * Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z));
            Vector3 desired = hamilcarRest.ToLocalPosition() + forward * (hamilcarSeat + HamilcarProfile.RollOff);
            if (!GroundPlacement.TryPlace(cargo, desired, rotation, out Vector3 point))
            {
                // The checked spot got blocked or the glider stopped off it: any dry ground under the nose will do.
                if (!HamilcarGround(desired, out point, out _))
                {
                    Plugin.Logger?.LogWarning("[Vanguard] HAMILCAR stopped with water off the nose - the Hexhound is lost");
                    return;
                }
                point.y += cargo.spawnOffset.y + 0.2f;
            }
            bool hold = HamilcarHold || (launcher != null && launcher is Aircraft aircraft && aircraft.Player != null);
            hamilcarVehicle = spawner.SpawnVehicle(cargo.unitPrefab, point.ToGlobalPosition(), rotation, Vector3.zero, missile.NetworkHQ,
                "HAMILCAR_UGV_" + (++hamilcarSerial), 1f, hold, null);
            VanguardStats.UgvsSpawned++;
            VanguardStats.Bump("hamilcar.unloaded");
        }

        // End of the husk: the shell goes quietly (the CyberAnchorSpawner pattern). Never Detonate a landed glider.
        private void HamilcarRemove()
        {
            VanguardStats.Bump("hamilcar.huskRemoved");
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            GameObject shell = missile.gameObject;
            missile.Networkdisabled = true;
            shell.SetActive(false);
            if (spawner != null && spawner.IsServer && spawner.ServerObjectManager != null) spawner.ServerObjectManager.Destroy(shell);
            Object.Destroy(shell); // the network destroy silently skips an unregistered identity
        }

        /// <summary>Sim telemetry of the most recent glider: numeric only, every key always present.</summary>
        internal static void HamilcarReport(Dictionary<string, object> h, Unit lead)
        {
            foreach (string counter in new[] { "released", "landed", "unloaded", "lost", "noLz", "short", "huskRemoved" })
                h[counter] = VanguardStats.Get("hamilcar." + counter);
            VanguardFlight f = HamilcarLast;
            Missile glider = f != null && f.missile != null && !f.missile.disabled ? f.missile : null;
            Unit vehicle = f != null && f.hamilcarVehicle != null && !f.hamilcarVehicle.disabled ? f.hamilcarVehicle : null;
            Vector3 fromRest = vehicle != null ? vehicle.GlobalPosition() - f.hamilcarRest : Vector3.zero;
            h["phase"] = f != null ? (int)f.hamilcarPhase : -1;
            h["landDist"] = f != null ? f.hamilcarLandDist : 0f;
            h["touchSink"] = f != null ? f.hamilcarTouchSink : 0f;
            h["touchSpeed"] = f != null ? f.hamilcarTouchSpeed : 0f;
            h["restDrift"] = f != null ? f.hamilcarDrift : 0f;
            h["gliderAlive"] = glider != null ? 1 : 0;
            h["vehicleAlive"] = vehicle != null ? 1 : 0;
            h["vehicleDist"] = vehicle != null ? new Vector2(fromRest.x, fromRest.z).magnitude : -1f;
            h["range"] = glider != null ? f.hamilcarDist : -1f;
            h["agl"] = glider != null ? f.hamilcarAgl : -1f;
            h["speed"] = glider != null ? glider.speed : 0f;
            h["fromLead"] = glider != null && lead != null ? FastMath.Distance(glider.GlobalPosition(), lead.GlobalPosition()) : -1f;
            // The cradle's Hexhound, read off the live hierarchy: the renderers under Payload and whether any still draws.
            int renderers = 0, drawn = 0;
            Transform payload = glider != null ? glider.transform.Find("Payload") : null;
            if (payload != null)
                foreach (Renderer renderer in payload.GetComponentsInChildren<Renderer>(true))
                {
                    renderers++;
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy) drawn++;
                }
            h["payloadRenderers"] = renderers;
            h["payloadHidden"] = glider != null && drawn == 0 ? 1 : 0;
        }
    }

    /// <summary>Scene reset for the HAMILCAR statics (the sim seams and the last glider).</summary>
    internal sealed class HamilcarService : MonoBehaviour, ISceneService
    {
        public void ResetForScene() => VanguardFlight.ResetHamilcar();
    }
}
