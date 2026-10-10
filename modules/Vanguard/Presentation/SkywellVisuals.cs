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
    /// Every peer, from SkywellBoard (the compact SkywellState the host publishes): holds the tanker's cargo door open, slides the
    /// deck out and flies the VTOL service tender on its tether (a critically damped follow in the tanker's frame, re-derived from
    /// the deck frame every frame after the floating-origin shift so a shift never smears it). When the host serves a receiver the
    /// tender holds station under its belly, upright with it, and works the dorsal fuel boom and loader arm UP at the receptacle and
    /// the pylon. Joints are found by the contract names in SkywellRig (a joint the model lacks is simply not animated). Nothing
    /// here ever touches a receiver's body; the AI receivers steer themselves (skywellFly.cs).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    internal sealed partial class SkywellVisuals : MonoBehaviour, ISceneService
    {
        private static readonly FieldInfo RampPartField = typeof(CargoRamp).GetField("rampPart", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CargoDoorField = typeof(MountedCargo).GetField("cargoDoor", BindingFlags.NonPublic | BindingFlags.Instance);

        private const int StageTow = -1, StageBelly = 0, StageFuel = 1, StageStation = 2;
        private const float Leash = SkywellZone.MaxTether + 10f; // the tender flies out to a receiver no farther than this from the deck
        private const int TetherPoints = 16;

        /// <summary>A model joint with its rest pose; every frame starts by restoring it and then poses off the rest.</summary>
        private sealed class Joint
        {
            public readonly Transform T;
            public readonly Vector3 Pos;
            public readonly Quaternion Rot;
            public readonly Vector3 Scale;          // FBX joints carry a 100x scale: always multiply, never assign 1
            public readonly Quaternion ParentRot0;  // the parent's world rotation at rest

            public Joint(Transform t)
            {
                T = t;
                Pos = t != null ? t.localPosition : Vector3.zero;
                Rot = t != null ? t.localRotation : Quaternion.identity;
                Scale = t != null ? t.localScale : Vector3.one;
                ParentRot0 = t != null && t.parent != null ? t.parent.rotation : Quaternion.identity;
            }

            public bool Ok => T != null;

            public void Reset()
            {
                if (T == null) return;
                T.localPosition = Pos;
                T.localRotation = Rot;
                T.localScale = Scale;
            }

            /// <summary>A body-frame axis in the parent's own frame (constant, so the joint's local axes do not matter).</summary>
            public Vector3 Axis(Quaternion body0, Vector3 bodyAxis) => Quaternion.Inverse(ParentRot0) * (body0 * bodyAxis);

            /// <summary>Rotate off the rest pose by <paramref name="deg"/> about a body-frame axis.</summary>
            public void Hinge(Quaternion body0, Vector3 bodyAxis, float deg)
            {
                if (T != null) T.localRotation = Quaternion.AngleAxis(deg, Axis(body0, bodyAxis)) * Rot;
            }
        }

        private sealed class Rig
        {
            public Aircraft Tanker;
            public Joint Deck, Winch, Tender, WingL, WingR, Magazine;
            public Joint FuelBase, FuelBoom, FuelTele, Nozzle, LoadBase, LoadUpper, LoadFore, Gripper;
            public readonly Joint[] Nacelles = new Joint[4], Fans = new Joint[4], Rounds = new Joint[SkywellMagazine.Rounds];
            public Transform TetherPoint;
            public Joint[] All;
            public Quaternion Body0;          // the tender's body frame at rest (x right, y up, z forward), world
            public Quaternion K;              // root rotation -> body frame
            public Quaternion TankerRot0;     // the tanker's world rotation when the rig was built (the winch drum turns about its lateral axis)
            public bool UnderDeck;            // the tender root hangs under the deck joint: the deck slide carries it
            public int Colliders;             // kit colliders switched off (see DisarmKit)
            public Vector3 FuelOff, LoadOff;  // body-frame offsets of the working joints from the tender root
            public float Upper = SkywellRig.DefaultUpper, Lower = SkywellRig.DefaultLower, BoomLen = 1.8f, TeleMax = SkywellRig.TeleTravel;
            public float ArmReach => Upper + Lower;
            public float BoomReach => BoomLen + TeleMax;
            public readonly float[] SlotAngle = new float[SkywellMagazine.Rounds + 1];
            // follow state (deck frame, so a floating-origin shift cannot touch it)
            public bool Primed;
            public Vector3 PosLocal, VelLocal, TargetLocal;
            public Quaternion RotLocal = Quaternion.identity;
            public int TargetKey;
            public float Gap;                 // how far the tender is from where it wants to be
            // the pose key: what the tender is positioned for. It changes only once the arm and boom are stowed.
            public Aircraft PoseRx;
            public int PoseStage = StageTow, PoseStation;
            public Vector3 PoseRail;          // the rail the loader serves, receiver space (latched while the arm is down)
            public Vector3 WantSmooth;        // low-passed target for a replicated receiver
            public bool WantSmoothed;
            public float ArmClock;            // seconds the arm and boom have had the tender in place for this job
            public bool ArmStowed = true;
            // animation weights
            public float Tilt = SkywellTender.FoldedTilt, FanAngle, Swing, Reach, Unfold, MagAngle, TetherAvoid, TetherBlue, NextDoor;
            public float Speed;               // tender speed in the deck frame, m/s
            // pit stop: job staging is client-driven from the moment the view's job tuple changes
            public TenderState LastState;
            public uint LastReceiver;
            public PitJobKind LastJob;
            public byte LastStation, LastIndex, LastRounds = SkywellMagazine.Rounds;
            public float JobStart, CommitFlash = -10f;
            public int CarrySlot = -1;
            public readonly bool[] RoundShown = new bool[SkywellMagazine.Rounds];
            public SkywellGlow GlowDeck, GlowTender, GlowNozzle, GlowGripper;
            public readonly SkywellGlow[] GlowFans = new SkywellGlow[4];
            public LineRenderer Tether;

            public void Dispose()
            {
                if (Tether != null) UnityEngine.Object.Destroy(Tether.gameObject);
            }
        }

        internal static string Probe = "none"; // sim diagnostics

        private readonly Dictionary<uint, Rig> rigs = new Dictionary<uint, Rig>();
        private readonly Dictionary<uint, float> unbuilt = new Dictionary<uint, float>(); // tanker -> retry time (a model without a tender root)
        private readonly List<uint> gone = new List<uint>();

        public void ResetForScene()
        {
            foreach (Rig rig in rigs.Values) rig.Dispose();
            rigs.Clear();
            unbuilt.Clear();
            holds.Clear();
            Steered.Clear();
        }

        private static Aircraft Resolve(uint id) =>
            id != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit u) ? u as Aircraft : null;

        /// <summary>How far this tanker's tender still has to go to its pose (m); 99 when it has no rig yet. The receiver's HUD counts this down.</summary>
        internal static float TenderGap(uint tanker) =>
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

        // ---- tender and door (render peers)

        private void LateUpdate()
        {
            if (Application.isBatchMode) return;
            float dt = Time.deltaTime;
            foreach (SkywellView v in SkywellBoard.Views.Values)
            {
                if (!rigs.TryGetValue(v.Tanker, out Rig rig))
                {
                    if (!v.Active) continue;
                    if (unbuilt.TryGetValue(v.Tanker, out float retry) && Time.time < retry) continue;
                    rig = Build(Resolve(v.Tanker));
                    if (rig == null)
                    {
                        unbuilt[v.Tanker] = Time.time + 5f;
                        continue;
                    }
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
            if (SimRun) SampleSeparation();
        }

        private static Transform Find(Dictionary<string, Transform> names, string name) =>
            names.TryGetValue(name, out Transform t) ? t : null;

        private static Rig Build(Aircraft tanker)
        {
            if (tanker == null) return null;
            Transform mount = SkywellBoard.Mount(tanker);
            // The kit's own subtree first; the names it lacks come from its parent (the model root sits one level up on some mounts).
            var names = new Dictionary<string, Transform>();
            foreach (Transform t in mount.GetComponentsInChildren<Transform>(true))
                if (!names.ContainsKey(t.name)) names[t.name] = t;
            if (mount.parent != null)
                foreach (Transform t in mount.parent.GetComponentsInChildren<Transform>(true))
                    if (!names.ContainsKey(t.name)) names[t.name] = t;
            Joint J(string n) => new Joint(Find(names, n));

            Joint tender = J("Tender");
            if (!tender.Ok)
            {
                BoscaliSummer.Plugin.Logger?.LogWarning("[SKYWELL] the kit model has no Tender joint: nothing to fly");
                return null;
            }
            var rig = new Rig
            {
                Tanker = tanker, Deck = J("Deck"), Winch = J("Winch"), Tender = tender,
                WingL = J("WingL"), WingR = J("WingR"), Magazine = J("Magazine"),
                FuelBase = J("FuelBase"), FuelBoom = J("FuelBoom"), FuelTele = J("FuelTele"), Nozzle = J("Nozzle"),
                LoadBase = J("LoadBase"), LoadUpper = J("LoadUpper"), LoadFore = J("LoadFore"), Gripper = J("Gripper"),
                TetherPoint = Find(names, "TetherPoint"),
            };
            for (int i = 0; i < 4; i++)
            {
                rig.Nacelles[i] = J("Nacelle" + i);
                rig.Fans[i] = J("Fan" + i);
            }
            for (int i = 0; i < SkywellMagazine.Rounds; i++)
            {
                rig.Rounds[i] = J("Round" + i);
                rig.RoundShown[i] = !rig.Rounds[i].Ok || rig.Rounds[i].T.gameObject.activeSelf;
            }
            rig.Colliders = DisarmKit(mount, rig);

            var all = new List<Joint> { rig.Winch, rig.Tender, rig.WingL, rig.WingR, rig.Magazine, rig.FuelBase, rig.FuelBoom, rig.FuelTele, rig.Nozzle,
                rig.LoadBase, rig.LoadUpper, rig.LoadFore, rig.Gripper };
            all.AddRange(rig.Nacelles);
            all.AddRange(rig.Fans);
            all.AddRange(rig.Rounds);
            all.RemoveAll(j => !j.Ok);
            rig.All = all.ToArray();
            ProbeJoints = rig.All.Length;
            rig.UnderDeck = rig.Deck.Ok && rig.Tender.T.IsChildOf(rig.Deck.T);

            // Body frame, from the model's own geometry (axis-agnostic): forward = tender pivot -> TetherPoint (the nose), up = pivot ->
            // the dorsal joints.
            Vector3 pivot = rig.Tender.T.position;
            Quaternion body0 = tanker.transform.rotation;
            Vector3 fwd = Vector3.zero, upHint = Vector3.zero;
            if (rig.TetherPoint != null && (rig.FuelBase.Ok || rig.LoadBase.Ok || rig.Magazine.Ok))
            {
                fwd = rig.TetherPoint.position - pivot;
                int n = 0;
                foreach (Joint dorsal in new[] { rig.FuelBase, rig.LoadBase, rig.Magazine })
                    if (dorsal.Ok)
                    {
                        upHint += dorsal.T.position - pivot;
                        n++;
                    }
                upHint /= Mathf.Max(1, n);
            }
            Vector3 up = Vector3.ProjectOnPlane(upHint, fwd);
            if (fwd.sqrMagnitude > 0.25f && up.sqrMagnitude > 1e-4f) body0 = Quaternion.LookRotation(fwd.normalized, up.normalized);
            rig.Body0 = body0;
            rig.TankerRot0 = tanker.transform.rotation;
            rig.K = Quaternion.Inverse(rig.Tender.T.rotation) * body0;

            // Where the working joints sit and how far they reach, measured from the stowed pose.
            Quaternion inv = Quaternion.Inverse(body0);
            Joint boomPivot = rig.FuelBoom.Ok ? rig.FuelBoom : rig.FuelBase;
            Joint shoulder = rig.LoadUpper.Ok ? rig.LoadUpper : rig.LoadBase;
            rig.FuelOff = boomPivot.Ok ? inv * (boomPivot.T.position - pivot) : Vector3.zero;
            rig.LoadOff = shoulder.Ok ? inv * (shoulder.T.position - pivot) : Vector3.zero;
            if (rig.FuelBoom.Ok && rig.Nozzle.Ok) rig.BoomLen = Mathf.Max(0.8f, Vector3.Distance(rig.FuelBoom.T.position, rig.Nozzle.T.position));
            if (rig.LoadUpper.Ok && rig.LoadFore.Ok && rig.Gripper.Ok)
            {
                float upper = Vector3.Distance(rig.LoadUpper.T.position, rig.LoadFore.T.position);
                float lower = Vector3.Distance(rig.LoadFore.T.position, rig.Gripper.T.position);
                if (upper > 0.3f && lower > 0.3f)
                {
                    rig.Upper = upper;
                    rig.Lower = lower;
                }
            }
            MeasureMagazine(rig);

            rig.GlowDeck = new SkywellGlow(rig.Deck.T);
            rig.GlowTender = new SkywellGlow(rig.Tender.T);
            rig.GlowNozzle = new SkywellGlow(rig.Nozzle.T);
            rig.GlowGripper = new SkywellGlow(rig.Gripper.T);
            for (int i = 0; i < 4; i++) rig.GlowFans[i] = new SkywellGlow(rig.Fans[i].T);

            int wings = (rig.WingL.Ok ? 1 : 0) + (rig.WingR.Ok ? 1 : 0), nacelles = 0, fans = 0, rounds = 0;
            for (int i = 0; i < 4; i++)
            {
                if (rig.Nacelles[i].Ok) nacelles++;
                if (rig.Fans[i].Ok) fans++;
            }
            foreach (Joint round in rig.Rounds)
                if (round.Ok) rounds++;
            Probe = $"tender wings={wings} nacelles={nacelles} fans={fans} fuel={(rig.FuelBoom.Ok ? 1 : 0)} loader={(rig.LoadUpper.Ok && rig.Gripper.Ok ? 1 : 0)} " +
                    $"mag={(rig.Magazine.Ok ? 1 : 0)} rounds={rounds} tether={(rig.TetherPoint != null ? 1 : 0)} winch={(rig.Winch.Ok ? 1 : 0)} " +
                    $"boom={rig.BoomLen:F2}+{rig.TeleMax:F1} arm={rig.Upper:F2}+{rig.Lower:F2} colliders={rig.Colliders}";
            return rig;
        }

        /// <summary>
        /// The tender, its arms and its boom are visuals: any collider on them rides the tanker's rigidbody, so a tender under a receiver's
        /// belly would shove the receiver (pitch kicks, torn-off parts) and the receiver the tanker. They are switched off here.
        /// </summary>
        private static int DisarmKit(Transform mount, Rig rig)
        {
            int off = 0;
            var seen = new HashSet<Collider>();
            foreach (Transform start in new[] { mount, rig.Deck.T, rig.Tender.T, rig.Winch.T })
            {
                if (start == null) continue;
                foreach (Collider c in start.GetComponentsInChildren<Collider>(true))
                    if (seen.Add(c) && c.enabled)
                    {
                        c.enabled = false;
                        off++;
                    }
            }
            return off;
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

        // ---- the pose

        /// <summary>
        /// Where the tender's body goes for a receiver under service, and how it sits: upright with the receiver, so the dorsal loader and
        /// boom face the pylons and the receptacle. Hand-offs slide it under the rail being served, fuel under the receptacle, anything
        /// else under the middle of the belly. The working joint (shoulder or boom hinge) goes under the point it must reach.
        /// </summary>
        private static void StationTarget(Rig rig, Aircraft r, int stage, Vector3 rail, out Vector3 want, out Quaternion wantBody)
        {
            Transform rt = r.transform;
            Bounds b = SkywellBoard.LocalBounds(r);
            wantBody = rt.rotation;
            float bellyY = b.min.y;
            Vector3 local, off;
            if (stage == StageStation)
            {
                local = new Vector3(rail.x, SkywellTender.WorkHeight(rail.y, bellyY, rig.ArmReach, SkywellTender.ArmClearance), rail.z);
                off = rig.LoadOff;
            }
            else if (stage == StageFuel)
            {
                Vector3 fuel = rt.InverseTransformPoint(SkywellBoard.FuelPoint(r));
                local = new Vector3(fuel.x, SkywellTender.WorkHeight(fuel.y, bellyY, rig.BoomReach, SkywellTender.FuelClearance), fuel.z);
                off = rig.FuelOff;
            }
            else
            {
                local = new Vector3(b.center.x, bellyY - SkywellTender.Under, b.center.z);
                off = Vector3.zero;
            }
            want = rt.TransformPoint(local) - wantBody * off;
        }

        private void Pose(Rig rig, SkywellView v, float dt)
        {
            Aircraft tanker = rig.Tanker;
            if (tanker == null) return;
            Transform tt = tanker.transform;
            TenderState state = v.State;
            float p = SkywellTender.Progress(state, v.StateAge);
            bool hot = (v.Flags & PitFlags.HotBreak) != 0;
            bool free = SkywellTender.Flying(state);
            if (state != TenderState.Stowed && !(state == TenderState.Stowing && p < 0.05f) && Time.time >= rig.NextDoor) HoldDoor(rig);

            // The rest pose, then the deck (slides aft with the deploy timeline).
            rig.Deck.Reset();
            foreach (Joint j in rig.All) j.Reset();
            if (rig.Deck.Ok && p > 0f)
            {
                Transform deckParent = rig.Deck.T.parent;
                // InverseTransformVector, not ...Direction: the FBX joints carry a 100x scale.
                Vector3 slide = -tt.forward * (SkywellTender.DeckSlide * SkywellTender.DeckOut(p));
                rig.Deck.T.localPosition = rig.Deck.Pos + (deckParent != null ? deckParent.InverseTransformVector(slide) : slide);
            }

            PitSync(rig, v);

            // ---- what the server is serving, and what the tender is positioned for. It moves on to the next job's pose only once the arm
            // and boom are stowed (or at once on a hot break, or when its receiver is gone): never dragged across a wing.
            Aircraft served = SkywellTender.Serving(state) ? Resolve(v.Receiver) : null;
            bool serving = served != null && !served.disabled && !hot &&
                           (served.transform.position - SkywellBoard.Anchor(tanker)).sqrMagnitude < Leash * Leash;
            bool jobKnown = serving && v.Job != PitJobKind.None;
            bool working = jobKnown && state == TenderState.Service;
            int wantStage = !serving ? StageTow : !jobKnown ? StageBelly : v.Job == PitJobKind.Fuel ? StageFuel : StageStation;
            int wantStation = wantStage == StageStation ? v.JobStation : 0;
            Aircraft wantRx = serving ? served : null;

            bool heldGone = rig.PoseRx == null || rig.PoseRx.disabled;
            bool matches = wantRx == rig.PoseRx && wantStage == rig.PoseStage && wantStation == rig.PoseStation;
            if (!matches && (rig.ArmStowed || hot || heldGone || rig.PoseStage == StageTow || !free))
            {
                rig.PoseRx = wantRx;
                rig.PoseStage = wantStage;
                rig.PoseStation = wantStation;
                rig.WantSmoothed = false;
                rig.ArmClock = 0f;
                matches = true;
            }
            Aircraft px = rig.PoseRx;
            bool poseOn = px != null && !px.disabled && rig.PoseStage != StageTow;
            bool stationPose = poseOn && rig.PoseStage == StageStation;
            // The rail follows the receiver (receiver space) and is only re-read while the arm is down, so the tender never shifts under a raised arm.
            if (stationPose && matches && rig.Reach < 0.05f)
                rig.PoseRail = px.transform.InverseTransformPoint(SkywellStations.StationPoint(px, rig.PoseStation));

            // ---- the tender root: held by the deck at first, free on its tether after
            Vector3 restPos = rig.Tender.T.position;
            Quaternion restBody = rig.Tender.T.rotation * rig.K;
            float roll = SkywellTender.RollAft(p);
            Vector3 deckPos = restPos - tt.forward * ((rig.UnderDeck ? 0f : SkywellTender.DeckSlide * SkywellTender.DeckOut(p)) + SkywellTender.RollOff * roll);
            Vector3 deckLocal = tt.InverseTransformPoint(deckPos);
            Quaternion deckBodyLocal = Quaternion.Inverse(tt.rotation) * restBody;

            Vector3 wantLocal;
            Quaternion wantBodyLocal;
            int key;
            float attach = free ? 0f : SkywellTender.Attach(p);
            if (poseOn && free)
            {
                StationTarget(rig, px, rig.PoseStage, rig.PoseRail, out Vector3 want, out Quaternion wantBody);
                wantLocal = tt.InverseTransformPoint(want);
                wantBodyLocal = Quaternion.Inverse(tt.rotation) * wantBody;
                key = px.GetInstanceID() * 131 + (rig.PoseStage + 1) * 37 + rig.PoseStation;
                if (!px.LocalSim)
                {
                    // A replicated receiver steps with its network updates: low-pass the pose so the tender does not shimmer with it.
                    rig.WantSmooth = rig.WantSmoothed ? SkywellTender.LowPass(rig.WantSmooth, wantLocal, dt, SkywellTender.TargetSmoothSeconds) : wantLocal;
                    rig.WantSmoothed = true;
                    wantLocal = rig.WantSmooth;
                }
                else rig.WantSmoothed = false;
            }
            else
            {
                float bob = Mathf.Sin(Time.time * 0.7f) * 0.6f;
                Vector3 tow = SkywellZone.Tow + new Vector3(Mathf.Sin(Time.time * 0.31f) * 1.2f, bob, 0f);
                wantLocal = free ? tow : Vector3.Lerp(deckLocal, tow, SkywellTender.TowBlend(p));
                wantBodyLocal = Quaternion.identity;
                key = 0;
                rig.WantSmoothed = false;
            }

            if (!rig.Primed)
            {
                rig.PosLocal = free ? wantLocal : deckLocal;
                rig.VelLocal = Vector3.zero;
                rig.RotLocal = free ? wantBodyLocal : deckBodyLocal;
                rig.TargetLocal = wantLocal;
                rig.TargetKey = key;
                rig.Primed = true;
            }
            if (attach >= 1f || state == TenderState.Stowed)
            {
                // Still on the cradle: the follow starts from here when the tender is let go.
                rig.PosLocal = deckLocal;
                rig.VelLocal = Vector3.zero;
                rig.RotLocal = deckBodyLocal;
                rig.TargetLocal = wantLocal;
                rig.TargetKey = key;
            }
            else
            {
                // Critically damped follow, at most MaxRelSpeed closing on a receiver; a new job or receiver is a retarget (it eases onto the
                // new pose from its own velocity), a moving receiver is a moving target (no lag, no snapping).
                Vector3 from = key == rig.TargetKey ? rig.TargetLocal : wantLocal;
                float maxSpeed = hot ? SkywellTender.HotSpeed : poseOn ? SkywellTender.MaxRelSpeed : SkywellTender.ReturnSpeed;
                SkywellTender.Follow(ref rig.PosLocal, ref rig.VelLocal, from, wantLocal, dt, SkywellTender.SmoothSeconds, maxSpeed);
                rig.TargetLocal = wantLocal;
                rig.TargetKey = key;
                rig.RotLocal = Quaternion.RotateTowards(rig.RotLocal, wantBodyLocal, SkywellTender.TurnDegPerSecond * dt);
            }
            rig.Gap = Vector3.Distance(rig.PosLocal, wantLocal);
            rig.Speed = rig.VelLocal.magnitude;
            // Never beyond the end of the tether.
            Vector3 winchLocal = tt.InverseTransformPoint(rig.Winch.Ok ? rig.Winch.T.position : restPos);
            rig.PosLocal = SkywellTender.ClampTether(winchLocal, rig.PosLocal, SkywellZone.MaxTether);

            Vector3 posLocal = attach > 0f ? Vector3.Lerp(rig.PosLocal, deckLocal, attach) : rig.PosLocal;
            Quaternion bodyLocal = attach > 0f ? Quaternion.Slerp(rig.RotLocal, deckBodyLocal, attach) : rig.RotLocal;
            Vector3 pos = tt.TransformPoint(posLocal);
            Quaternion body = tt.rotation * bodyLocal;
            if (state != TenderState.Stowed) rig.Tender.T.SetPositionAndRotation(pos, body * Quaternion.Inverse(rig.K));

            // The arm and boom work only once the tender is in place for this job (the clock waits for it).
            bool armWork = working && matches && poseOn && p >= 1f;
            if (armWork && rig.Gap < SkywellTender.ReachGap && Time.time - rig.JobStart >= NominalStart(v)) rig.ArmClock += dt;

            PoseAirframe(rig, state, p, dt, body);
            PoseArms(rig, v, px, poseOn, armWork, hot, body, dt);
            float length = PoseTether(rig, tt, state, p, poseOn ? px : null, attach, dt);
            if (SimRun) ProbeTetherMax = Mathf.Max(ProbeTetherMax, length);
            PoseWinch(rig, tt, length);
            PoseGlow(rig, v, hot);
            if (SimRun) Measure(rig, v, px, poseOn, working, pos);
        }

        /// <summary>When, in the job's own clock, the hand-off or boom work is meant to begin: after the flight to another station that the job leaves time for.</summary>
        private static float NominalStart(SkywellView v)
        {
            if (v.Job == PitJobKind.Fuel) return v.JobIndex > 0 ? PitPlan.TravelSeconds : 0f;
            return Mathf.Max(0f, v.JobTenths / 10f - SkywellHandoff.Total);
        }

        /// <summary>Wings, nacelles and fans: the deploy timeline and the flight state. All rotations are about body-frame axes off the stowed pose.</summary>
        private static void PoseAirframe(Rig rig, TenderState state, float p, float dt, Quaternion body)
        {
            float spread = SkywellTender.Spread(p);
            if (SimRun) ProbeSpreadMax = Mathf.Max(ProbeSpreadMax, spread);
            rig.WingR.Hinge(rig.Body0, Vector3.up, -SkywellRig.WingFoldDeg * spread);
            rig.WingL.Hinge(rig.Body0, Vector3.up, SkywellRig.WingFoldDeg * spread);

            float tiltWant = SkywellTender.Tilt(state, p, rig.Speed);
            rig.Tilt = Mathf.MoveTowards(rig.Tilt, tiltWant, 90f * dt);
            float power = SkywellTender.FanPower(state, p);
            if (SimRun)
            {
                ProbeTiltMin = Mathf.Min(ProbeTiltMin, rig.Tilt);
                ProbeFanTurns += SkywellRig.FanDegPerSecond * power * dt / 360f;
            }
            rig.FanAngle = Mathf.Repeat(rig.FanAngle + SkywellRig.FanDegPerSecond * power * dt, 360f);
            for (int i = 0; i < 4; i++)
            {
                rig.Nacelles[i].Hinge(rig.Body0, Vector3.right, -(SkywellRig.NacelleStowedTilt - rig.Tilt));
                rig.Fans[i].Hinge(rig.Body0, Vector3.forward, ((i & 1) == 0 ? 1f : -1f) * rig.FanAngle); // counter-rotating pairs, about the fan axis (the nacelle's stowed forward axis)
            }
        }

        private void PoseWinch(Rig rig, Transform tt, float length)
        {
            if (!rig.Winch.Ok || length <= 0f) return;
            // The drum turns about the tanker's lateral axis: it pays the line out as the tender goes.
            Vector3 axis = rig.Winch.Axis(rig.TankerRot0, Vector3.right);
            rig.Winch.T.localRotation = Quaternion.AngleAxis(length / SkywellTender.DrumRadius * Mathf.Rad2Deg, axis) * rig.Winch.Rot;
        }
    }
}
