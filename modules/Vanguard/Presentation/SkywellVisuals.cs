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
    /// Every peer, from SkywellBoard: holds the tanker's cargo door open, poses both arms (two-bone IK plus telescope,
    /// axis-agnostic world aiming re-derived from rest each frame, so floating-origin shifts never smear it), and
    /// docks receivers this peer simulates (AI on the host, a player on their own client; stick input aborts).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    internal sealed class SkywellVisuals : MonoBehaviour, ISceneService
    {
        private const float DeploySeconds = 2.5f;
        private const float TipSpeed = 6f;
        private const float StageRun = 2.7f;   // per telescope stage (model: 3 m stages)
        private const float DeckSlide = 3.2f;  // rear pallet travel out over the ramp

        private static readonly FieldInfo RampPartField = typeof(CargoRamp).GetField("rampPart", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CargoDoorField = typeof(MountedCargo).GetField("cargoDoor", BindingFlags.NonPublic | BindingFlags.Instance);

        private sealed class Joint
        {
            public readonly Transform T;
            public readonly Vector3 Pos;
            public readonly Quaternion Rot;

            public Joint(Transform t)
            {
                T = t;
                Pos = t != null ? t.localPosition : Vector3.zero;
                Rot = t != null ? t.localRotation : Quaternion.identity;
            }

            public void Reset()
            {
                if (T == null) return;
                T.localPosition = Pos;
                T.localRotation = Rot;
            }
        }

        private sealed class Arm
        {
            public Joint Shoulder, Forearm, Wrist;
            public readonly List<Joint> Tele = new List<Joint>();
            public Vector3 TipLocal; // tanker space
            public bool HasTip;
            public bool Valid => Shoulder?.T != null && Forearm?.T != null && Wrist?.T != null;
        }

        private sealed class Rig
        {
            public Aircraft Tanker;
            public Arm Fuel, Cargo;
            public Joint Deck;
            public float Deploy;
            public float NextDoor;
        }

        internal static string Probe = "none"; // sim diagnostics

        private readonly Dictionary<uint, Rig> rigs = new Dictionary<uint, Rig>();
        private readonly List<uint> gone = new List<uint>();

        public void ResetForScene()
        {
            rigs.Clear();
            holds.Clear();
        }

        private static Aircraft Resolve(uint id) =>
            id != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit u) ? u as Aircraft : null;

        // ---- docking, FS-41 CATOBAR style (on the peer that simulates the receiver)

        private const float RequiredBrake = 0.8f;
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
            // A player engages by holding the brake near an idle, deployed kit; the script flies the rest.
            if (GameManager.GetLocalAircraft(out Aircraft me) && me != null && !me.disabled && me.LocalSim &&
                !holds.ContainsKey(me) && me.GetInputs().brake >= RequiredBrake && NearestKit(me, out Aircraft kitTanker))
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

        // ---- arms and door (render peers)

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
            for (int i = 0; i < gone.Count; i++) rigs.Remove(gone[i]);
        }

        private static Rig Build(Aircraft tanker)
        {
            if (tanker == null) return null;
            Transform mount = SkywellBoard.Mount(tanker);
            Transform root = mount.parent != null ? mount.parent : tanker.transform;
            var names = new Dictionary<string, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (!names.ContainsKey(t.name)) names[t.name] = t;
            Arm ArmOf(string suf)
            {
                var arm = new Arm
                {
                    Shoulder = new Joint(Find(names, "Shoulder" + suf)),
                    Forearm = new Joint(Find(names, "Forearm" + suf)),
                    Wrist = new Joint(Find(names, "Wrist" + suf)),
                };
                for (int k = 1; Find(names, "Tele" + k + suf) is Transform tele; k++) arm.Tele.Add(new Joint(tele));
                return arm;
            }
            var rig = new Rig { Tanker = tanker, Fuel = ArmOf(""), Cargo = ArmOf("2"), Deck = new Joint(Find(names, "Deck")) };
            Transform tf = tanker.transform;
            string P(Joint j) => j?.T == null ? "-" : tf.InverseTransformPoint(j.T.position).ToString("F1");
            Probe = $"root={root.name} scale={rig.Fuel.Shoulder?.T?.lossyScale.x:F2} sh={P(rig.Fuel.Shoulder)} fa={P(rig.Fuel.Forearm)} wr={P(rig.Fuel.Wrist)} " +
                    $"sh2={P(rig.Cargo.Shoulder)} wr2={P(rig.Cargo.Wrist)} tele={rig.Fuel.Tele.Count} deck={rig.Deck.T != null}";
            Transform kitRoot = rig.Fuel.Shoulder?.T;
            while (kitRoot != null && kitRoot.parent != root) kitRoot = kitRoot.parent;
            if (kitRoot != null)
            {
                Renderer[] rs = kitRoot.GetComponentsInChildren<Renderer>(true);
                int on = 0;
                foreach (Renderer x in rs) if (x.enabled && x.gameObject.activeInHierarchy) on++;
                Renderer r0 = rs.Length > 0 ? rs[0] : null;
                Probe += $" | kit={kitRoot.name} active={kitRoot.gameObject.activeInHierarchy} renderers={rs.Length} on={on} layer={kitRoot.gameObject.layer}" +
                         (r0 != null ? $" r0={r0.name} shader={r0.sharedMaterial?.shader?.name} bounds={tf.InverseTransformPoint(r0.bounds.center).ToString("F1")}/{r0.bounds.size.ToString("F1")} rootActive={root.gameObject.activeInHierarchy}" : "");
            }
            return rig.Fuel.Valid || rig.Cargo.Valid ? rig : null;
        }

        private static Transform Find(Dictionary<string, Transform> names, string name) =>
            names.TryGetValue(name, out Transform t) ? t : null;

        private void Pose(Rig rig, SkywellView v, float dt)
        {
            Aircraft tanker = rig.Tanker;
            if (tanker == null) return;
            rig.Deploy = Mathf.MoveTowards(rig.Deploy, v.Active ? 1f : 0f, dt / DeploySeconds);
            if (rig.Deploy > 0f && Time.time >= rig.NextDoor) HoldDoor(rig);

            Aircraft r = v.Phase != SkywellPhase.Idle
                ? Resolve(v.Receiver) : null;
            Transform tt = tanker.transform;
            rig.Deck.Reset();
            if (rig.Deck.T != null && rig.Deploy > 0f)
            {
                // Deck first (carries both arm bases), then the arms are solved from where it put them.
                Transform deckParent = rig.Deck.T.parent;
                // InverseTransformVector, not ...Direction: the FBX joints carry a 100x scale.
                Vector3 slide = -tt.forward * (DeckSlide * Mathf.SmoothStep(0f, 1f, rig.Deploy));
                rig.Deck.T.localPosition = rig.Deck.Pos + (deckParent != null ? deckParent.InverseTransformVector(slide) : slide);
            }
            Vector3 fuelGoal = r != null ? SkywellBoard.ProbePoint(r) : Vector3.zero;
            Vector3 cargoGoal = r != null ? SkywellBoard.PylonPoint(r) : Vector3.zero;
            PoseArm(rig.Fuel, tt, r != null, fuelGoal, -1f, rig.Deploy, dt);
            PoseArm(rig.Cargo, tt, r != null, cargoGoal, 1f, rig.Deploy, dt);
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

        /// <param name="side">Lateral offset sign of the ready pose (fuel arm -1, cargo arm +1).</param>
        private static void PoseArm(Arm arm, Transform tanker, bool contact, Vector3 goal, float side, float deploy, float dt)
        {
            if (!arm.Valid) return;
            arm.Shoulder.Reset();
            arm.Forearm.Reset();
            arm.Wrist.Reset();
            for (int k = 0; k < arm.Tele.Count; k++) arm.Tele[k].Reset();
            if (deploy <= 0f)
            {
                arm.HasTip = false;
                return;
            }

            Vector3 s = arm.Shoulder.T.position;
            Vector3 f = arm.Forearm.T.position;
            Vector3 w0 = arm.Wrist.T.position;
            float upper = Vector3.Distance(s, f);
            float lower = Vector3.Distance(f, w0);
            float maxExtension = StageRun * arm.Tele.Count;
            float reach = upper + lower + maxExtension;

            // Ready pose hangs below and behind the ramp; contact only once the receiver is inside the reach.
            Vector3 ready = s + tanker.rotation * new Vector3(side * 1.5f, -6f, -14f);
            if (contact) goal -= (goal - s).normalized * 0.9f; // the wrist stops short so the nozzle / cradle lands on the point
            Vector3 want = contact && Vector3.Distance(goal, s) < reach ? goal : ready;
            if (!arm.HasTip)
            {
                arm.TipLocal = tanker.InverseTransformPoint(w0);
                arm.HasTip = true;
            }
            arm.TipLocal = Vector3.MoveTowards(arm.TipLocal, tanker.InverseTransformPoint(want), TipSpeed * dt);
            Vector3 target = Vector3.Lerp(w0, tanker.TransformPoint(arm.TipLocal), deploy);

            float d = Vector3.Distance(target, s);
            float extension = Mathf.Clamp(d - (upper + lower) * 0.9f, 0f, maxExtension) * deploy;
            Vector3 outward = (w0 - f).normalized;
            for (int k = 0; k < arm.Tele.Count; k++)
            {
                Transform t = arm.Tele[k].T;
                if (t == null) continue;
                Vector3 run = outward * (extension / arm.Tele.Count);
                t.localPosition = arm.Tele[k].Pos + (t.parent != null ? t.parent.InverseTransformVector(run) : run);
            }

            ArmIK.Solve(upper, lower + extension, new Vector3(0f, 0f, d), out float lift, out _);
            Vector3 dir = (target - s) / Mathf.Max(d, 1e-3f);
            Vector3 up = tanker.up;
            Vector3 perp = up - dir * Vector3.Dot(up, dir);
            perp = perp.sqrMagnitude < 1e-4f ? tanker.forward : perp.normalized;
            float rad = lift * Mathf.Deg2Rad;
            Vector3 elbow = s + upper * (Mathf.Cos(rad) * dir + Mathf.Sin(rad) * perp);

            Transform sh = arm.Shoulder.T;
            sh.rotation = Quaternion.FromToRotation(f - s, elbow - s) * sh.rotation;
            Transform fa = arm.Forearm.T;
            Vector3 fNow = fa.position;
            fa.rotation = Quaternion.FromToRotation(arm.Wrist.T.position - fNow, target - fNow) * fa.rotation;
        }
    }
}
