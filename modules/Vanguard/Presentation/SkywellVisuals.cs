using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer, from SkywellBoard: holds the tanker's cargo door open, launches and flies the service kite on its tether
    /// (axis-agnostic world posing re-derived from rest each frame, so floating-origin shifts never smear it), and
    /// docks receivers this peer simulates (AI on the host, a player on their own client; stick input aborts).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    internal sealed partial class SkywellVisuals : MonoBehaviour, ISceneService
    {
        private const float DeploySeconds = 2.5f;
        private const float DeckSlide = 3.2f;  // rear pallet travel out over the ramp

        private static readonly FieldInfo RampPartField = typeof(CargoRamp).GetField("rampPart", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CargoDoorField = typeof(MountedCargo).GetField("cargoDoor", BindingFlags.NonPublic | BindingFlags.Instance);

        private sealed class Joint
        {
            public readonly Transform T;
            public readonly Vector3 Pos;
            public readonly Quaternion Rot;
            public readonly Vector3 Scale; // FBX joints carry a 100x scale: always multiply, never assign 1

            public Joint(Transform t)
            {
                T = t;
                Pos = t != null ? t.localPosition : Vector3.zero;
                Rot = t != null ? t.localRotation : Quaternion.identity;
                Scale = t != null ? t.localScale : Vector3.one;
            }

            public void Reset()
            {
                if (T == null) return;
                T.localPosition = Pos;
                T.localRotation = Rot;
                T.localScale = Scale;
            }
        }

        private sealed class Rig
        {
            public Aircraft Tanker;
            public Joint Deck, Winch, Kite, Tail, Wing, OuterL, OuterR, TipL, TipR, Nozzle, Loader, LoaderFore, Cradle;
            public Joint LatchL, LatchR, RoundJ;   // pit-stop additions: absent in older bundles, always null-safe
            public Joint[] All;
            public Quaternion BodyOffset;    // kite root rotation -> body frame (fwd, up), from the rest joints
            public float Deploy, Launch, NextDoor, NozzleRun, Reach;
            public bool Flying;
            public Vector3 NozzleAim;
            public float HingeBehind;
            public Vector3 KiteLocal;        // tanker space (floating-origin safe)
            public Quaternion KiteRotLocal;
            public LineRenderer Tether;
            // Pit stop: job staging is client-driven from the moment the view's job tuple changes.
            public SkywellPhase LastPhase;
            public uint LastReceiver;
            public PitJobKind LastJob;
            public byte LastStation, LastIndex;
            public float JobStart, RoundVis = 1f, RoundFactor = 1f, CommitFlash = -10f, TetherBlue;
            public PitFrom From;
            public bool RoundHidden;
            public Vector3 ReachTarget;      // tanker space: the rail the loader last served
            public SkywellGlow GlowDeck, GlowKite, GlowCradle, GlowNozzle, GlowTipL, GlowTipR;
            public TrailRenderer TrailL, TrailR;

            public void Dispose()
            {
                if (Tether != null) UnityEngine.Object.Destroy(Tether.gameObject);
                if (TrailL != null) UnityEngine.Object.Destroy(TrailL.gameObject);
                if (TrailR != null) UnityEngine.Object.Destroy(TrailR.gameObject);
            }
        }

        internal static string Probe = "none"; // sim diagnostics

        private readonly Dictionary<uint, Rig> rigs = new Dictionary<uint, Rig>();
        private readonly List<uint> gone = new List<uint>();

        public void ResetForScene()
        {
            foreach (Rig rig in rigs.Values) rig.Dispose();
            rigs.Clear();
            holds.Clear();
        }

        private static Aircraft Resolve(uint id) =>
            id != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit u) ? u as Aircraft : null;

        // ---- docking, FS-41 CATOBAR style (on the peer that simulates the receiver)

        private const float RequiredBrake = 0.8f;
        private const float ResetBrake = 0.5f;      // after a server-side release the brake must drop below this before re-docking
        private const float EngageSpeed = 30f;      // m/s along the scripted path
        private const float MinEngageSeconds = 2.5f;
        private const float GrantGrace = 1.5f;      // seconds a player waits for the server's yes

        internal static string RailProbe = "none"; // sim diagnostics

        private struct Body
        {
            public Rigidbody Rb;
            public Vector3 Pos;     // from the pivot, pivot space
            public Quaternion Rot;
        }

        private sealed class Hold
        {
            public Aircraft Tanker;
            public Vector3 StartPos;     // pivot, tanker space
            public Quaternion StartRot;  // tanker space
            public float Start, Duration;
            public readonly List<Body> Bodies = new List<Body>();
        }

        private readonly Dictionary<Aircraft, Hold> holds = new Dictionary<Aircraft, Hold>();
        private readonly List<Aircraft> released = new List<Aircraft>();

        private void FixedUpdate()
        {
            float now = Time.timeSinceLevelLoad;
            // A player engages by holding the brake near an idle, deployed kit; the script flies the rest. After any release
            // the brake did not cause (hot break, abort), the pilot lets go first, or the script would grab the jet again.
            bool haveMe = GameManager.GetLocalAircraft(out Aircraft me) && me != null && !me.disabled && me.LocalSim;
            if (haveMe && me.GetInputs().brake < ResetBrake) needsBrakeReset = false;
            if (haveMe && !needsBrakeReset && !holds.ContainsKey(me) && me.GetInputs().brake >= RequiredBrake &&
                NearestKit(me, out Aircraft kitTanker))
            {
                Engage(me, kitTanker, now);
                SkywellNet.RequestDock(me, kitTanker);
            }
            // The host flies the AI receivers the server docked.
            if (GameAccess.IsServer())
                foreach (SkywellView v in SkywellBoard.Views.Values)
                {
                    if (v.Receiver == 0 || v.Phase == SkywellPhase.Idle) continue;
                    Aircraft ai = Resolve(v.Receiver);
                    Aircraft tanker = Resolve(v.Tanker);
                    if (ai != null && tanker != null && ai.Player == null && ai.LocalSim && !holds.ContainsKey(ai)) Engage(ai, tanker, now);
                }

            ReinforceRamps();

            released.Clear();
            foreach (KeyValuePair<Aircraft, Hold> pair in holds)
            {
                Aircraft r = pair.Key;
                Hold hold = pair.Value;
                if (r == null || r.disabled || hold.Tanker == null || hold.Tanker.disabled || !r.LocalSim)
                {
                    released.Add(r);
                    continue;
                }
                bool granted = SkywellBoard.Views.TryGetValue(hold.Tanker.persistentID.Id, out SkywellView view) &&
                               view.Receiver == r.persistentID.Id;
                if (r.Player != null && GameManager.IsLocalAircraft(r))
                {
                    if (r.GetInputs().brake < RequiredBrake)
                    {
                        SkywellNet.RequestUndock(r);
                        released.Add(r);
                        continue;
                    }
                    if (!granted && now - hold.Start > GrantGrace)
                    {
                        needsBrakeReset = true;
                        released.Add(r);
                        continue;
                    }
                }
                else if (!granted)
                {
                    released.Add(r);
                    continue;
                }
                Drive(r, hold, now);
            }
            for (int i = 0; i < released.Count; i++) holds.Remove(released[i]);
        }

        /// <summary>
        /// A deployed kit holds the ramp open at cruise; the vanilla ramp hinge is sized for low-speed drops and tears
        /// off into the tail. While deployed, the tanker's physics owner makes its ramp joints unbreakable (vanilla
        /// rebuilds the hinge as it moves, so this runs every step).
        /// </summary>
        private static void ReinforceRamps()
        {
            foreach (SkywellView v in SkywellBoard.Views.Values)
            {
                if (!v.Active) continue;
                Aircraft tanker = Resolve(v.Tanker);
                if (tanker == null || tanker.disabled || !tanker.LocalSim) continue;
                foreach (CargoRamp ramp in tanker.GetComponentsInChildren<CargoRamp>())
                    if (RampPartField?.GetValue(ramp) is Component part && part != null)
                        foreach (UnityEngine.Joint joint in part.GetComponents<UnityEngine.Joint>())
                        {
                            joint.breakForce = float.PositiveInfinity;
                            joint.breakTorque = float.PositiveInfinity;
                        }
            }
        }

        /// <summary>Closest deployed, unoccupied kit whose contact point is within reach astern of the tanker.</summary>
        private static bool NearestKit(Aircraft me, out Aircraft best)
        {
            best = null;
            float bestDist = SkywellService.EngageRadius;
            foreach (SkywellView v in SkywellBoard.Views.Values)
            {
                if (!v.Active || v.Receiver != 0) continue;
                Aircraft tanker = Resolve(v.Tanker);
                if (tanker == null || tanker.disabled || tanker == me || tanker.NetworkHQ != me.NetworkHQ) continue;
                Vector3 contact = SkywellBoard.ContactPoint(tanker);
                Vector3 probe = SkywellBoard.ProbePoint(me);
                float d = Vector3.Distance(probe, contact);
                float astern = tanker.transform.InverseTransformPoint(probe).z - tanker.transform.InverseTransformPoint(contact).z;
                if (d < bestDist && astern < 5f)
                {
                    bestDist = d;
                    best = tanker;
                }
            }
            return best != null;
        }

        private void Engage(Aircraft r, Aircraft tanker, float now)
        {
            Transform t = tanker.transform;
            Vector3 pivot = r.rb.position;
            Quaternion pivotRot = r.rb.rotation;
            var hold = new Hold
            {
                Tanker = tanker,
                StartPos = t.InverseTransformPoint(pivot),
                StartRot = Quaternion.Inverse(t.rotation) * pivotRot,
                Start = now,
            };
            hold.Duration = Mathf.Max(MinEngageSeconds, Vector3.Distance(hold.StartPos, DockPos(r, tanker)) / EngageSpeed);
            // Every part body moves as one (they are jointed: moving only the root tears the airframe apart).
            Quaternion inv = Quaternion.Inverse(pivotRot);
            var seen = new HashSet<Rigidbody>();
            foreach (UnitPart part in r.partLookup)
                if (part != null && !part.IsDetached() && part.rb != null && seen.Add(part.rb))
                    hold.Bodies.Add(new Body { Rb = part.rb, Pos = inv * (part.rb.position - pivot), Rot = inv * part.rb.rotation });
            if (r.rb != null && seen.Add(r.rb))
                hold.Bodies.Add(new Body { Rb = r.rb, Pos = Vector3.zero, Rot = Quaternion.identity });
            holds[r] = hold;
        }

        /// <summary>Receiver pivot (rigidbody origin) that puts its probe on the contact point, tanker space.</summary>
        private static Vector3 DockPos(Aircraft r, Aircraft tanker)
        {
            Vector3 probeFromPivot = Quaternion.Inverse(r.rb.rotation) * (SkywellBoard.ProbePoint(r) - r.rb.position);
            return tanker.transform.InverseTransformPoint(SkywellBoard.ContactPoint(tanker)) - probeFromPivot;
        }

        private static void Drive(Aircraft r, Hold hold, float now)
        {
            Aircraft tanker = hold.Tanker;
            Transform t = tanker.transform;
            float s = Mathf.SmoothStep(0f, 1f, (now - hold.Start) / hold.Duration);
            Vector3 pos = Vector3.Lerp(hold.StartPos, DockPos(r, tanker), s);
            Quaternion rot = Quaternion.Slerp(hold.StartRot, Quaternion.identity, s);
            Vector3 pivot = t.TransformPoint(pos);
            Quaternion pivotRot = t.rotation * rot;
            Vector3 velocity = tanker.rb != null ? tanker.rb.velocity : Vector3.zero;
            Vector3 spin = tanker.rb != null ? tanker.rb.angularVelocity : Vector3.zero;
            foreach (Body b in hold.Bodies)
            {
                if (b.Rb == null) continue;
                b.Rb.MovePosition(pivot + pivotRot * b.Pos);
                b.Rb.MoveRotation(pivotRot * b.Rot);
                b.Rb.velocity = velocity + Vector3.Cross(spin, b.Rb.worldCenterOfMass - pivot);
                b.Rb.angularVelocity = spin;
            }
            // Scripted motion is not flown: keep it out of the pilots' g (as the FS-41 catapult does).
            if (r.pilots != null)
                foreach (Pilot p in r.pilots)
                {
                    if (p == null || p.dead || p.ejected) continue;
                    Rigidbody prb = p.GetRB();
                    if (prb == null) continue;
                    p.velocityPrev = prb.velocity;
                    p.accel = Vector3.zero;
                    p.gForce = 0f;
                }
            RailProbe = $"s={s:F2} dur={hold.Duration:F1} toDock={(t.InverseTransformPoint(r.rb.position) - DockPos(r, tanker)).magnitude:F1} bodies={hold.Bodies.Count}";
        }

        // ---- service kite and door (render peers)

        private const float LaunchSeconds = 8f;
        private const float KiteSpeed = 14f;          // m/s, tanker frame
        private const float NozzleRun = 1.5f;         // refuel boom telescope travel
        private const float BoomLength = 1.95f;       // hinge to nozzle tip (model)
        private const float NozzleSpeed = 1.6f;
        private const float HingeAheadOfProbe = 1.5f; // refuel-boom hinge ahead of the receiver probe when plugged
        private const float TailRun = 2.6f;           // telescoping tail boom travel (model)
        private const int TetherPoints = 16;

        private Material tetherMaterial;

        private void LateUpdate()
        {
            if (Application.isBatchMode) return;
            float dt = Time.deltaTime;
            foreach (SkywellView v in SkywellBoard.Views.Values)
            {
                if (!rigs.TryGetValue(v.Tanker, out Rig rig))
                {
                    if (!v.Active) continue;
                    rig = Build(Resolve(v.Tanker));
                    if (rig == null) continue;
                    rigs[v.Tanker] = rig;
                }
                Pose(rig, v, dt);
            }
            gone.Clear();
            foreach (KeyValuePair<uint, Rig> pair in rigs)
                if (pair.Value.Tanker == null || !SkywellBoard.Views.ContainsKey(pair.Key)) gone.Add(pair.Key);
            for (int i = 0; i < gone.Count; i++)
            {
                rigs[gone[i]].Dispose();
                rigs.Remove(gone[i]);
            }
        }

        private static Rig Build(Aircraft tanker)
        {
            if (tanker == null) return null;
            Transform mount = SkywellBoard.Mount(tanker);
            Transform root = mount.parent != null ? mount.parent : tanker.transform;
            var names = new Dictionary<string, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (!names.ContainsKey(t.name)) names[t.name] = t;
            Joint J(string n) => new Joint(Find(names, n));
            var rig = new Rig
            {
                Tanker = tanker, Deck = J("Deck"), Winch = J("Winch"), Kite = J("Kite"), Wing = J("Wing"),
                Tail = J("Tail"), OuterL = J("WingOuterL"), OuterR = J("WingOuterR"), TipL = J("WingletL"), TipR = J("WingletR"),
                Nozzle = J("Nozzle"),
                Loader = J("Loader"), LoaderFore = J("LoaderFore"), Cradle = J("Cradle"),
                LatchL = J("LatchL"), LatchR = J("LatchR"), RoundJ = J("Round"),
            };
            if (rig.Kite.T == null || rig.Wing.T == null || rig.Nozzle.T == null) return null;
            rig.All = new[] { rig.Kite, rig.Tail, rig.Wing, rig.OuterL, rig.OuterR, rig.Nozzle, rig.Loader, rig.LoaderFore, rig.Cradle,
                rig.Winch, rig.LatchL, rig.LatchR, rig.RoundJ };
            rig.GlowDeck = new SkywellGlow(rig.Deck.T);
            rig.GlowKite = new SkywellGlow(rig.Kite.T);
            rig.GlowCradle = new SkywellGlow(rig.Cradle.T);
            rig.GlowNozzle = new SkywellGlow(rig.Nozzle.T);
            rig.GlowTipL = new SkywellGlow(rig.TipL.T);
            rig.GlowTipR = new SkywellGlow(rig.TipR.T);
            // Body frame from the joints (axis-agnostic): forward = tail nozzle -> kite pivot, up = kite -> wing pivot.
            Vector3 fwd = (rig.Kite.T.position - rig.Nozzle.T.position).normalized;
            Vector3 up = (rig.Wing.T.position - rig.Kite.T.position).normalized;
            rig.BodyOffset = Quaternion.Inverse(rig.Kite.T.rotation) * Quaternion.LookRotation(fwd, up);
            rig.NozzleAim = Vector3.zero;
            Transform tf = tanker.transform;
            Probe = $"root={root.name} kite={tf.InverseTransformPoint(rig.Kite.T.position).ToString("F1")} " +
                    $"outer={rig.OuterL.T != null && rig.OuterR.T != null} loader={rig.Cradle.T != null} winch={rig.Winch.T != null}";
            return rig;
        }

        private static Transform Find(Dictionary<string, Transform> names, string name) =>
            names.TryGetValue(name, out Transform t) ? t : null;

        private void Pose(Rig rig, SkywellView v, float dt)
        {
            Aircraft tanker = rig.Tanker;
            if (tanker == null) return;
            Transform tt = tanker.transform;

            // Sequence: deck out, then the kite launches; stowing runs it backwards (kite home before the deck).
            if (v.Active)
            {
                rig.Deploy = Mathf.MoveTowards(rig.Deploy, 1f, dt / DeploySeconds);
                if (rig.Deploy >= 1f) rig.Launch = Mathf.MoveTowards(rig.Launch, 1f, dt / LaunchSeconds);
            }
            else
            {
                rig.Launch = Mathf.MoveTowards(rig.Launch, 0f, dt / LaunchSeconds);
                if (rig.Launch <= 0f) rig.Deploy = Mathf.MoveTowards(rig.Deploy, 0f, dt / DeploySeconds);
            }
            if (rig.Deploy > 0f && Time.time >= rig.NextDoor) HoldDoor(rig);

            rig.Deck.Reset();
            if (rig.Deck.T != null && rig.Deploy > 0f)
            {
                Transform deckParent = rig.Deck.T.parent;
                // InverseTransformVector, not ...Direction: the FBX joints carry a 100x scale.
                Vector3 slide = -tt.forward * (DeckSlide * Mathf.SmoothStep(0f, 1f, rig.Deploy));
                rig.Deck.T.localPosition = rig.Deck.Pos + (deckParent != null ? deckParent.InverseTransformVector(slide) : slide);
            }
            foreach (Joint j in rig.All) j.Reset();
            Vector3 cradlePos = rig.Kite.T.position;
            Quaternion cradleRot = rig.Kite.T.rotation * rig.BodyOffset;

            // Where the kite wants to be: the pit-stop job decides. Fuel (and docking) hangs it at the probe; a handoff
            // tucks it under the pylon being served; with no receiver it loiters off the ramp.
            PitSync(rig, v);
            Aircraft r = v.Phase != SkywellPhase.Idle ? Resolve(v.Receiver) : null;
            Vector3 contact = SkywellBoard.ContactPoint(tanker);
            bool close = r != null && Vector3.Distance(SkywellBoard.ProbePoint(r), contact) < 30f;
            bool hot = (v.Flags & PitFlags.HotBreak) != 0;
            bool serving = v.Phase == SkywellPhase.Transfer;
            bool stationStage = close && serving && (v.Job == PitJobKind.Handoff || v.Job == PitJobKind.Cassette);
            bool fuelStage = close && !stationStage;
            Vector3 want;
            Quaternion wantRot;
            if (fuelStage)
            {
                Transform rt = r.transform;
                want = SkywellBoard.ProbePoint(r) + rt.forward * (HingeAheadOfProbe + rig.HingeBehind) + rt.up * 2.0f;   // clear of the nose cone
                wantRot = rt.rotation;
            }
            else if (stationStage)
            {
                Transform rt = r.transform;
                want = SkywellStations.StationPoint(r, v.JobStation) - rt.right * 0.3f - rt.up * 2.7f - rt.forward * 1.6f;   // glider tucked under the jet
                wantRot = rt.rotation;
            }
            else
            {
                float bob = Mathf.Sin(Time.time * 0.7f) * 0.6f;
                want = contact + tt.up * (2.5f + bob) + tt.forward * 4f + tt.right * (Mathf.Sin(Time.time * 0.31f) * 1.2f);
                wantRot = tt.rotation;
            }
            if (!rig.Flying)
            {
                rig.KiteLocal = tt.InverseTransformPoint(cradlePos);
                rig.KiteRotLocal = Quaternion.Inverse(tt.rotation) * cradleRot;
                rig.Flying = true;
            }
            float kiteSpeed = hot ? HotKiteSpeed : v.Job != PitJobKind.None ? PitKiteSpeed : KiteSpeed;
            rig.KiteLocal = Vector3.MoveTowards(rig.KiteLocal, tt.InverseTransformPoint(want), kiteSpeed * dt);
            rig.KiteRotLocal = Quaternion.RotateTowards(rig.KiteRotLocal, Quaternion.Inverse(tt.rotation) * wantRot, 60f * dt);
            if (rig.Launch <= 0f) rig.Flying = false;

            float launch = Mathf.SmoothStep(0f, 1f, rig.Launch);
            Vector3 pos = Vector3.Lerp(cradlePos, tt.TransformPoint(rig.KiteLocal), launch);
            Quaternion body = Quaternion.Slerp(cradleRot, tt.rotation * rig.KiteRotLocal, launch);
            rig.Kite.T.rotation = body * Quaternion.Inverse(rig.BodyOffset);
            rig.Kite.T.position = pos;
            Vector3 kFwd = body * Vector3.forward, kUp = body * Vector3.up, kRight = body * Vector3.right;

            // Unfold sequence once clear of the ramp: tail boom telescopes aft, centre wing swivels 90 deg, then the
            // outer panels swing 180 deg outboard about their vertical hinge pins.
            float tail = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rig.Launch - 0.12f) / 0.2f));
            if (rig.Tail.T != null && tail > 0f && rig.Tail.T.parent != null)
                rig.Tail.T.localPosition = rig.Tail.Pos + rig.Tail.T.parent.InverseTransformVector(-kFwd * (TailRun * tail));
            float swivel = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rig.Launch - 0.3f) / 0.2f));
            if (rig.OuterL.T != null && rig.OuterR.T != null && swivel > 0f)
            {
                // span marker = the two outer hinges (centre-section tips), stowed along the body
                Vector3 span = rig.OuterR.T.position - rig.OuterL.T.position;
                Vector3 to = Vector3.Slerp(span.normalized, kRight, swivel);
                rig.Wing.T.rotation = Quaternion.FromToRotation(span, to) * rig.Wing.T.rotation;
            }
            float unfold = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rig.Launch - 0.5f) / 0.3f));
            if (unfold > 0f)
            {
                UnfoldPanel(rig.OuterL, rig.TipL, kUp, kFwd, unfold);
                UnfoldPanel(rig.OuterR, rig.TipR, kUp, kFwd, unfold);
            }
            rig.HingeBehind = Vector3.Distance(pos, rig.Nozzle.T.position);

            // Mini flying boom: stowed pointing forward under the tail boom. A fuel job swings it down onto the probe, plugs
            // (the slide is along the boom, so it reads as a telescope), holds through the flow, then unplugs.
            Vector3 boomWant = kFwd;
            float runWant = 0f, connected = 0f;
            if (fuelStage && rig.Launch >= 1f && serving && v.Job == PitJobKind.Fuel)
            {
                Vector3 toProbe = SkywellBoard.ProbePoint(r) - rig.Nozzle.T.position;
                connected = PitStaging.Connected(Time.time - rig.JobStart, Mathf.Max(0f, v.JobTenths / 10f - PitPlan.FuelPlug - PitPlan.FuelUnplug));
                boomWant = Vector3.Slerp(kFwd, toProbe.normalized, connected);
                runWant = Mathf.Clamp(toProbe.magnitude - BoomLength, 0f, NozzleRun) * connected;
            }
            if (rig.NozzleAim.sqrMagnitude < 0.5f) rig.NozzleAim = kFwd;
            rig.NozzleAim = Vector3.RotateTowards(rig.NozzleAim, boomWant, 70f * Mathf.Deg2Rad * dt, 0f);
            rig.NozzleRun = Mathf.MoveTowards(rig.NozzleRun, Vector3.Angle(rig.NozzleAim, boomWant) < 5f ? runWant : 0f,
                (hot ? NozzleRun / 0.4f : NozzleSpeed) * dt);
            rig.Nozzle.T.rotation = Quaternion.FromToRotation(kFwd, rig.NozzleAim) * rig.Nozzle.T.rotation;
            if (rig.NozzleRun > 0f && rig.Nozzle.T.parent != null)
                rig.Nozzle.T.localPosition = rig.Nozzle.Pos + rig.Nozzle.T.parent.InverseTransformVector(rig.NozzleAim * rig.NozzleRun);

            // The loader hot-swaps: it carries the round up to the exact rail, the real missile appears at the commit, the
            // arm drops back to the belly magazine for the next one (PitStaging curves, seconds since the job began).
            float jobTime = Time.time - rig.JobStart;
            if (stationStage && rig.Launch >= 1f)
            {
                rig.Reach = PitStaging.Reach(jobTime, rig.From);
                rig.ReachTarget = tt.InverseTransformPoint(SkywellStations.StationPoint(r, v.JobStation));
            }
            else rig.Reach = Mathf.MoveTowards(rig.Reach, 0f, dt / (hot ? 0.3f : 0.6f));
            if (rig.Reach > 0f) PoseLoader(rig, tt.TransformPoint(rig.ReachTarget), -kFwd, rig.Reach);
            PoseRound(rig, stationStage, jobTime, dt);

            float tether = Tether(rig, tt, pos, kUp, kFwd, connected);
            PoseLatches(rig);
            if (rig.Winch.T != null && tether > 0f)
                rig.Winch.T.localRotation = rig.Winch.Rot * Quaternion.AngleAxis(tether / 0.2f * Mathf.Rad2Deg, Vector3.right); // drum pays the line out
            PoseGlow(rig, v, hot, connected);
            PoseTrails(rig, rig.Launch > 0.85f, rig.TipL.T != null ? rig.TipL.T.position + kUp * 0.45f : pos,
                rig.TipR.T != null ? rig.TipR.T.position + kUp * 0.45f : pos);
            ProbeReach = rig.Reach;
            ProbeNozzle = rig.NozzleRun;
            ProbeReachMax = Mathf.Max(ProbeReachMax, rig.Reach);
            ProbeNozzleMax = Mathf.Max(ProbeNozzleMax, rig.NozzleRun);
            ProbeRoundMin = Mathf.Min(ProbeRoundMin, rig.RoundVis);
            if (SimRun)
                KiteProbe = $"launch={rig.Launch:F2} stage={(fuelStage ? "fuel" : stationStage ? "ammo" : "loiter")} job={v.Job}/{v.JobIndex}of{v.JobCount} " +
                            $"kite={tt.InverseTransformPoint(pos).ToString("F1")} nozzle={rig.NozzleRun:F1} reach={rig.Reach:F1} round={rig.RoundVis:F1}";
        }

        /// <summary>Swing an outer wing panel 180 deg out through the trailing side (like a carrier wing fold), never through the fuselage.</summary>
        private static void UnfoldPanel(Joint panel, Joint tip, Vector3 kUp, Vector3 kFwd, float unfold)
        {
            if (panel.T == null || tip.T == null) return;
            Vector3 folded = tip.T.position - panel.T.position;
            float sign = Vector3.Dot(Quaternion.AngleAxis(90f, kUp) * folded, kFwd) < 0f ? 1f : -1f;
            panel.T.rotation = Quaternion.AngleAxis(sign * 180f * unfold, kUp) * panel.T.rotation;
        }

        internal static string KiteProbe = "none";
        private static readonly bool SimRun = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));

        /// <summary>Two-link loader aimed at the pylon (world FromTo aiming, re-derived from rest every frame).</summary>
        private static void PoseLoader(Rig rig, Vector3 target, Vector3 pole, float weight)
        {
            if (rig.Loader.T == null || rig.LoaderFore.T == null || rig.Cradle.T == null) return;
            Vector3 s = rig.Loader.T.position, f = rig.LoaderFore.T.position, c0 = rig.Cradle.T.position;
            float upper = Vector3.Distance(s, f), lower = Vector3.Distance(f, c0);
            Vector3 goal = Vector3.Lerp(c0, target, weight);
            float d = Mathf.Min(Vector3.Distance(goal, s), upper + lower - 0.01f);
            ArmIK.Solve(upper, lower, new Vector3(0f, 0f, d), out float lift, out _);
            Vector3 dir = (goal - s).normalized;
            Vector3 perp = pole - dir * Vector3.Dot(pole, dir);
            perp = perp.sqrMagnitude < 1e-4f ? Vector3.up : perp.normalized;
            float rad = lift * Mathf.Deg2Rad;
            Vector3 elbow = s + upper * (Mathf.Cos(rad) * dir + Mathf.Sin(rad) * perp);
            rig.Loader.T.rotation = Quaternion.FromToRotation(f - s, elbow - s) * rig.Loader.T.rotation;
            Vector3 fNow = rig.LoaderFore.T.position;
            rig.LoaderFore.T.rotation = Quaternion.FromToRotation(rig.Cradle.T.position - fNow, s + dir * d - fNow) * rig.LoaderFore.T.rotation;
        }

        /// <summary>Fuel + power tether from the deck winch to the kite's bridle, with a slight catenary sag.</summary>
        private float Tether(Rig rig, Transform tt, Vector3 kite, Vector3 kUp, Vector3 kFwd, float connected)
        {
            bool show = rig.Launch > 0f && rig.Winch.T != null;
            if (rig.Tether == null)
            {
                if (!show) return 0f;
                if (tetherMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null) shader = Shader.Find("Sprites/Default");
                    tetherMaterial = new Material(shader) { color = new Color(0.08f, 0.085f, 0.09f) };
                }
                rig.Tether = new GameObject("SkywellTether").AddComponent<LineRenderer>();
                rig.Tether.sharedMaterial = tetherMaterial;
                rig.Tether.positionCount = TetherPoints;
                rig.Tether.widthMultiplier = 0.07f;
                rig.Tether.numCapVertices = 2;
                rig.Tether.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            rig.Tether.enabled = show;
            if (!show) return 0f;
            TintTether(rig, connected);
            Vector3 a = rig.Winch.T.position + tt.up * 0.15f;
            Vector3 b = kite + kUp * 0.42f + kFwd * 0.25f;
            Vector3 mid = (a + b) * 0.5f - tt.up * (0.035f * Vector3.Distance(a, b));
            for (int i = 0; i < TetherPoints; i++)
            {
                float t = i / (float)(TetherPoints - 1);
                rig.Tether.SetPosition(i, (1 - t) * (1 - t) * a + 2 * (1 - t) * t * mid + t * t * b);
            }
            return Vector3.Distance(a, b);
        }

        private static void HoldDoor(Rig rig)
        {
            rig.NextDoor = Time.time + 1f;
            WeaponStation station = SkywellBoard.KitStation(rig.Tanker);
            if (station != null)
                foreach (Weapon w in station.Weapons)
                    if (w is MountedCargo cargo && CargoDoorField?.GetValue(cargo) is BayDoor door && door != null) door.OpenDoor(2f);
            foreach (CargoRamp ramp in rig.Tanker.GetComponentsInChildren<CargoRamp>())
                ramp.OpenDoor(2f);
        }
    }
}
