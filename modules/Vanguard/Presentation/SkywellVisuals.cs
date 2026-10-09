using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Every peer, from SkywellBoard: holds the tanker's cargo door open, launches and flies the service glider on its tether
    /// (axis-agnostic world posing re-derived from rest each frame, so floating-origin shifts never smear it) and, when the
    /// server serves a receiver, brings it under that receiver's belly (flying inverted, belly loader up) and works the boom and
    /// loader from there. Nothing here ever touches a receiver's body; the AI receivers steer themselves (skywellFly.cs).
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
            public Vector3 NozzleOff, LoaderOff; // joint offsets from the kite pivot, in the glider's rest body frame
            public Quaternion CradleRel;     // cradle rotation relative to the body at rest: the round stays parallel to the airframe
            public float Deploy, Launch, NextDoor, NozzleRun, Reach;
            public bool Flying;
            public Vector3 NozzleAim;
            public Vector3 KiteLocal;        // tanker space (floating-origin safe)
            public Quaternion KiteRotLocal;
            public Vector3 KiteVel;          // glider velocity, tanker space
            public Vector3 TargetLocal;      // last frame's glider target, tanker space
            public int TargetKey;            // receiver + job + station: a change is a retarget, not the target moving
            public float Gap;                // how far the glider is from where it wants to be
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
            public Vector3 TrailAnchor;      // tanker world position last frame: a jump no flight speed explains is a floating-origin shift

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

        /// <summary>How far this tanker's glider still has to go to its pose (m); 99 when it has no rig yet. The receiver's HUD counts this down.</summary>
        internal static float GliderGap(uint tanker) =>
            Instance != null && Instance.rigs.TryGetValue(tanker, out Rig rig) ? rig.Gap : 99f;

        private static SkywellVisuals Instance;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
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

        // ---- service glider and door (render peers)

        private const float LaunchSeconds = 8f;
        private const float KiteSpeed = 14f;          // m/s, tanker frame: tow and return
        private const float NozzleRun = 2.0f;         // refuel boom telescope travel
        private const float BoomLength = 1.95f;       // hinge to nozzle tip (model)
        private const float NozzleSpeed = 1.6f;
        private const float TailRun = 2.6f;           // telescoping tail boom travel (model)
        private const float GliderLeash = 140f;       // the glider flies out to a receiver no farther than this from the mount (the keep box reaches ~125 m)
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
            Quaternion rest = Quaternion.LookRotation(fwd, up);
            rig.BodyOffset = Quaternion.Inverse(rig.Kite.T.rotation) * rest;
            // Where the working joints sit on the glider, so the pose can put the boom hinge or the loader shoulder under a point
            // on the receiver whatever way up the glider flies.
            Quaternion inv = Quaternion.Inverse(rest);
            rig.NozzleOff = inv * (rig.Nozzle.T.position - rig.Kite.T.position);
            rig.LoaderOff = rig.Loader.T != null ? inv * (rig.Loader.T.position - rig.Kite.T.position) : Vector3.zero;
            rig.CradleRel = rig.Cradle.T != null ? inv * rig.Cradle.T.rotation : Quaternion.identity;
            rig.NozzleAim = Vector3.zero;
            Transform tf = tanker.transform;
            Probe = $"root={root.name} kite={tf.InverseTransformPoint(rig.Kite.T.position).ToString("F1")} " +
                    $"outer={rig.OuterL.T != null && rig.OuterR.T != null} loader={rig.Cradle.T != null} winch={rig.Winch.T != null}";
            return rig;
        }

        private static Transform Find(Dictionary<string, Transform> names, string name) =>
            names.TryGetValue(name, out Transform t) ? t : null;

        /// <summary>
        /// Where the glider's pivot goes for a receiver under service, and how it sits. It flies belly up, aligned with the
        /// receiver, so the loader on its belly faces the pylons and the boom rises at the receptacle; hand-offs slide it under
        /// the rail being served, fuel under the receptacle, anything else under the belly's middle.
        /// </summary>
        private static void GliderPose(Rig rig, Aircraft r, SkywellView v, bool fuelStage, bool stationStage, out Vector3 want, out Quaternion wantRot)
        {
            Transform rt = r.transform;
            Bounds b = SkywellBoard.LocalBounds(r);
            wantRot = Quaternion.LookRotation(rt.forward, -rt.up);
            float bellyY = b.min.y;
            Vector3 local, off;
            if (stationStage)
            {
                Vector3 rail = rt.InverseTransformPoint(SkywellStations.StationPoint(r, v.JobStation));
                local = new Vector3(rail.x, SkywellGlider.ServeHeight(rail.y, bellyY), rail.z);
                off = rig.LoaderOff;
            }
            else if (fuelStage)
            {
                Vector3 fuel = rt.InverseTransformPoint(SkywellBoard.FuelPoint(r));
                local = new Vector3(fuel.x, bellyY - SkywellGlider.Under, fuel.z);
                off = rig.NozzleOff;
            }
            else
            {
                local = new Vector3(b.center.x, bellyY - SkywellGlider.Under, b.center.z);
                off = Vector3.zero;
            }
            want = rt.TransformPoint(local) - wantRot * off;
        }

        private void Pose(Rig rig, SkywellView v, float dt)
        {
            Aircraft tanker = rig.Tanker;
            if (tanker == null) return;
            Transform tt = tanker.transform;

            // Sequence: deck out, then the glider launches; stowing runs it backwards (glider home before the deck).
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

            // Where the glider wants to be: the receiver under service decides (belly pose, then the job's working pose); with
            // nobody to serve it loiters on its tether; after a hot break it snaps home.
            PitSync(rig, v);
            Aircraft r = v.Phase != SkywellPhase.Idle ? Resolve(v.Receiver) : null;
            bool hot = (v.Flags & PitFlags.HotBreak) != 0;
            // A called-in AI still miles astern is not served yet: the glider only leaves its tow for a receiver within the tether's reach.
            bool serving = r != null && !r.disabled && !hot && (r.transform.position - SkywellBoard.Anchor(tanker)).sqrMagnitude < GliderLeash * GliderLeash;
            bool transfer = serving && v.Phase == SkywellPhase.Transfer;
            bool stationStage = transfer && (v.Job == PitJobKind.Handoff || v.Job == PitJobKind.Cassette);
            bool fuelStage = transfer && v.Job == PitJobKind.Fuel;
            Vector3 want;
            Quaternion wantRot;
            int key;
            if (serving)
            {
                GliderPose(rig, r, v, fuelStage, stationStage, out want, out wantRot);
                key = (int)v.Receiver * 131 + (stationStage ? 100 + v.JobStation : fuelStage ? 99 : 1);
            }
            else
            {
                float bob = Mathf.Sin(Time.time * 0.7f) * 0.6f;
                want = SkywellBoard.Anchor(tanker) + tt.rotation * (SkywellGlider.Tow + new Vector3(Mathf.Sin(Time.time * 0.31f) * 1.2f, bob, 0f));
                wantRot = tt.rotation;
                key = 0;
            }
            Vector3 wantLocal = tt.InverseTransformPoint(want);
            if (!rig.Flying)
            {
                rig.KiteLocal = tt.InverseTransformPoint(cradlePos);
                rig.KiteRotLocal = Quaternion.Inverse(tt.rotation) * cradleRot;
                rig.KiteVel = Vector3.zero;
                rig.TargetLocal = wantLocal;
                rig.TargetKey = key;
                rig.Flying = true;
            }
            // Critically damped follow, at most MaxRelSpeed closing on the receiver; a new job or receiver is a retarget (it eases
            // onto the new pose from its own velocity), a moving receiver is a moving target (no lag, no snapping).
            Vector3 from = key == rig.TargetKey ? rig.TargetLocal : wantLocal;
            float maxSpeed = hot ? HotKiteSpeed : serving ? SkywellGlider.MaxRelSpeed : KiteSpeed;
            SkywellGlider.Follow(ref rig.KiteLocal, ref rig.KiteVel, from, wantLocal, dt, SkywellGlider.SmoothSeconds, maxSpeed);
            rig.TargetLocal = wantLocal;
            rig.TargetKey = key;
            rig.Gap = Vector3.Distance(rig.KiteLocal, wantLocal);
            rig.KiteRotLocal = Quaternion.RotateTowards(rig.KiteRotLocal, Quaternion.Inverse(tt.rotation) * wantRot, SkywellGlider.TurnDegPerSecond * dt);
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

            // Mini flying boom: stowed pointing forward along the tail boom. A fuel job swings it onto the receptacle, plugs (the
            // slide is along the boom, so it reads as a telescope), holds through the flow, then unplugs.
            Vector3 boomWant = kFwd;
            float runWant = 0f, connected = 0f;
            if (fuelStage && rig.Launch >= 1f)
            {
                Vector3 toProbe = SkywellBoard.FuelPoint(r) - rig.Nozzle.T.position;
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

            // The loader hot-swaps: it lifts the round straight up from the glider to the rail (0.6 s), latches (0.3 s), the real
            // missile appears at the commit, the arm drops back to the belly magazine (0.5 s) for the next one (PitStaging curves).
            float jobTime = Time.time - rig.JobStart;
            if (stationStage && rig.Launch >= 1f)
            {
                rig.Reach = PitStaging.Reach(jobTime, rig.From);
                // A follow-on lowers the arm from the rail it just loaded; only once it is down does the target move to the next rail.
                if (rig.From != PitFrom.Again || jobTime >= PitStaging.Down)
                    rig.ReachTarget = tt.InverseTransformPoint(SkywellStations.StationPoint(r, v.JobStation));
            }
            else rig.Reach = Mathf.MoveTowards(rig.Reach, 0f, dt / (hot ? 0.3f : 0.6f));
            if (rig.Reach > 0f) PoseLoader(rig, tt.TransformPoint(rig.ReachTarget), -kFwd, rig.Reach, body);
            PoseRound(rig, stationStage, jobTime, dt);

            float tether = Tether(rig, tt, pos, kUp, kFwd, connected);
            PoseLatches(rig);
            if (rig.Winch.T != null && tether > 0f)
                rig.Winch.T.localRotation = rig.Winch.Rot * Quaternion.AngleAxis(tether / 0.2f * Mathf.Rad2Deg, Vector3.right); // drum pays the line out
            PoseGlow(rig, v, hot, connected);
            PoseTrails(rig, rig.Launch > 0.85f, rig.TipL.T != null ? rig.TipL.T.position + kUp * 0.45f : pos,
                rig.TipR.T != null ? rig.TipR.T.position + kUp * 0.45f : pos, dt);
            ProbeReach = rig.Reach;
            ProbeNozzle = rig.NozzleRun;
            ProbeReachMax = Mathf.Max(ProbeReachMax, rig.Reach);
            ProbeNozzleMax = Mathf.Max(ProbeNozzleMax, rig.NozzleRun);
            ProbeRoundMin = Mathf.Min(ProbeRoundMin, rig.RoundVis);
            if (transfer && rig.Launch >= 1f && jobTime > 0.5f) RecordGap(r, pos);
            if (SimRun)
                KiteProbe = $"launch={rig.Launch:F2} stage={(fuelStage ? "fuel" : stationStage ? "ammo" : serving ? "belly" : "tow")} job={v.Job}/{v.JobIndex}of{v.JobCount} " +
                            $"kite={tt.InverseTransformPoint(pos).ToString("F1")} gap={rig.Gap:F1} nozzle={rig.NozzleRun:F1} reach={rig.Reach:F1} round={rig.RoundVis:F1}";
        }

        /// <summary>Sim diagnostics (status: serveGapMax / serveGapMin / serveGapSamples): glider to airframe-bounds distance while a job runs.</summary>
        internal static float ServeGapMax, ServeGapMin = 999f;
        internal static int ServeGapSamples;

        private static void RecordGap(Aircraft r, Vector3 glider)
        {
            Bounds b = SkywellBoard.LocalBounds(r);
            float gap = SkywellGlider.BoxDistance(r.transform.InverseTransformPoint(glider), b.center, b.extents);
            ServeGapMax = Mathf.Max(ServeGapMax, gap);
            ServeGapMin = Mathf.Min(ServeGapMin, gap);
            ServeGapSamples++;
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

        /// <summary>
        /// Two-link loader reaching the pylon. The goal sweeps from the folded cradle to the rail by angle and length from the shoulder
        /// (so the round rises out of the glider rather than swinging round it), the cradle keeps the airframe's attitude so the round
        /// stays parallel to the rail, and the elbow trails aft. World FromTo aiming, re-derived from rest every frame.
        /// </summary>
        private static void PoseLoader(Rig rig, Vector3 target, Vector3 pole, float weight, Quaternion body)
        {
            if (rig.Loader.T == null || rig.LoaderFore.T == null || rig.Cradle.T == null) return;
            Vector3 s = rig.Loader.T.position, f = rig.LoaderFore.T.position, c0 = rig.Cradle.T.position;
            float upper = Vector3.Distance(s, f), lower = Vector3.Distance(f, c0);
            Vector3 home = c0 - s, aim = target - s;
            float homeDist = home.magnitude, aimDist = aim.magnitude;
            Vector3 dir = Vector3.Slerp(homeDist > 1e-3f ? home / homeDist : -pole, aimDist > 1e-3f ? aim / aimDist : -pole, weight);
            float d = Mathf.Min(Mathf.Lerp(homeDist, aimDist, weight), upper + lower - 0.01f);
            ArmIK.Solve(upper, lower, new Vector3(0f, 0f, d), out float lift, out _);
            Vector3 perp = pole - dir * Vector3.Dot(pole, dir);
            perp = perp.sqrMagnitude < 1e-4f ? Vector3.up : perp.normalized;
            float rad = lift * Mathf.Deg2Rad;
            Vector3 elbow = s + upper * (Mathf.Cos(rad) * dir + Mathf.Sin(rad) * perp);
            rig.Loader.T.rotation = Quaternion.FromToRotation(f - s, elbow - s) * rig.Loader.T.rotation;
            Vector3 fNow = rig.LoaderFore.T.position;
            rig.LoaderFore.T.rotation = Quaternion.FromToRotation(rig.Cradle.T.position - fNow, s + dir * d - fNow) * rig.LoaderFore.T.rotation;
            rig.Cradle.T.rotation = body * rig.CradleRel;
        }

        /// <summary>Fuel + power tether from the deck winch to the glider's bridle, with a slight catenary sag.</summary>
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
            // The bridle sits on the glider's back; flying belly up that is underneath, so the line takes the side facing the tanker.
            Vector3 b = kite + (Vector3.Dot(kUp, tt.up) >= 0f ? kUp : -kUp) * 0.42f + kFwd * 0.25f;
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
