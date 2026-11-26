using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Presentation;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// AEGIS-3 countermeasure pod. Attached to each spawned VG_Aegis_Pod and registered with the aircraft's
    /// CountermeasureManager so it appears in the countermeasures cycle/menu. When triggered, automatically
    /// targets and destroys the closest inbound missile heading at the carrier.
    /// </summary>
    internal sealed class AegisCountermeasure : Countermeasure, ICountermeasureCapacity
    {
        private static readonly FieldInfo Fired = AccessTools.Field(typeof(MountedMissile), "fired");
        private static readonly FieldInfo RailDirection = AccessTools.Field(typeof(MountedMissile), "railDirection");
        private static readonly FieldInfo RailVector = AccessTools.Field(typeof(MountedMissile), "railVector");
        private static readonly MethodInfo RemoveFromHardpoint = AccessTools.Method(typeof(MountedMissile), "RemoveFromHardpoint");
        private static readonly MethodInfo RailLaunch = AccessTools.Method(typeof(MountedMissile), "RailLaunch", new[] { typeof(Unit), typeof(Unit) });

        private readonly List<MountedMissile> cells = new List<MountedMissile>();
        private Hardpoint mount;
        private bool rearmPending;

        public int MaxAmmo { get; private set; }
        public int PodIndex { get; internal set; }

        internal static bool HasNativeRail => Fired?.FieldType == typeof(bool) &&
            RailDirection?.FieldType == typeof(MountedMissile.RailDirection) && RailVector?.FieldType == typeof(Vector3) &&
            RemoveFromHardpoint != null && RailLaunch?.ReturnType == typeof(UniTask);

        internal void AddCell(MountedMissile cell, Aircraft carrier, Hardpoint hardpoint)
        {
            if (cell == null) return;
            if (!cells.Contains(cell)) cells.Add(cell);
            if (aircraft != null || carrier == null) return;

            mount = hardpoint;
            if (mount.part != null) mount.part.onPartDetached += OnHardpointDetached;
            displayName = AegisIntercept.DisplayName;
            displayImage = cell.info != null ? cell.info.weaponIcon : null;
            ammo = MaxAmmo = GetComponentsInChildren<MountedMissile>(true).Length;
            AegisDoors.AttachPod(transform);
            AegisTrigger.RegisterPod(carrier, this);
            AttachToUnit(carrier);
        }

        public override void Fire() => AegisTrigger.Fire(aircraft);

        public override void UpdateHUD()
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud != null && aircraft != null && hud.aircraft == aircraft &&
                AircraftReadout.ActiveCountermeasure(aircraft) is AegisCountermeasure)
                hud.DisplayCountermeasures(displayName, displayImage, AegisTrigger.TotalAmmo(aircraft));
        }

        public override void Rearm(Aircraft carrier, Unit rearmer)
        {
            rearmPending = false;
            bool rearmed = false;
            foreach (MountedMissile cell in cells)
            {
                if (cell == null || cell.ammo > 0 || !cell.IsAttached()) continue;
                // A rearm event can arrive during the rail animation on either peer.
                if (cell.gameObject.activeSelf)
                {
                    rearmPending = true;
                    continue;
                }
                cell.Rearm(1, null);
                rearmed = true;
            }
            RecountAmmo();
            if (rearmed && rearmer != null && GameManager.IsLocalAircraft(carrier) && SceneSingleton<AircraftActionsReport>.i != null)
                SceneSingleton<AircraftActionsReport>.i.ReportText("AEGIS rearmed by " + rearmer.unitName, 5f);
        }

        private void RecountAmmo()
        {
            ammo = 0;
            foreach (MountedMissile cell in cells)
                if (cell != null) ammo += cell.ammo;
        }

        internal int ReadyCell()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                MountedMissile cell = cells[i];
                if (cell != null && cell.ammo > 0 && cell.IsAttached()) return i;
            }
            return -1;
        }

#if !PUBLIC_RELEASE
        internal void TrimTo(int keep)
        {
            for (int i = cells.Count - 1; i >= keep; i--)
            {
                MountedMissile cell = cells[i];
                if (cell == null || cell.ammo <= 0) continue;
                Spend(cell);
                cell.gameObject.SetActive(false);
            }
            RefreshSelectedHud();
        }
#endif

        internal bool LaunchCell(int cellIndex, Missile threat)
        {
            if (cellIndex < 0 || cellIndex >= cells.Count) return false;
            MountedMissile cell = cells[cellIndex];
            if (cell == null || cell.ammo <= 0 || !cell.IsAttached()) return false;

            RailVector.SetValue(cell, (MountedMissile.RailDirection)RailDirection.GetValue(cell) switch
            {
                MountedMissile.RailDirection.Forward => Vector3.forward,
                MountedMissile.RailDirection.Backward => Vector3.back,
                MountedMissile.RailDirection.Up => Vector3.up,
                MountedMissile.RailDirection.Left => Vector3.left,
                MountedMissile.RailDirection.Right => Vector3.right,
                _ => Vector3.down,
            });
            Spend(cell);
            if (mount.bayDoors != null) mount.SpringOpenBayDoors();
            AegisDoors.Open(cell);
            Release(cell, threat).Forget();
            return true;
        }

        private void Spend(MountedMissile cell)
        {
            RemoveFromHardpoint.Invoke(cell, null);
            Fired.SetValue(cell, true);
            cell.ammo = 0;
            RecountAmmo();
        }

        private async UniTask Release(MountedMissile cell, Missile threat)
        {
            // Use the game's rail animation, launch FX and server spawn without its weapon-station RPC.
            await (UniTask)RailLaunch.Invoke(cell, new object[] { aircraft, threat });
            if (this != null && aircraft != null && rearmPending)
            {
                Rearm(aircraft, null);
                RefreshSelectedHud();
            }
        }

        internal void RefreshSelectedHud()
        {
            if (aircraft != null && GameManager.IsLocalAircraft(aircraft) &&
                AircraftReadout.ActiveCountermeasure(aircraft) is AegisCountermeasure)
                aircraft.countermeasureManager.UpdateHUD();
        }

        private void OnHardpointDetached(UnitPart part) => Destroy(this);

        protected override void OnDestroy()
        {
            if (mount?.part != null) mount.part.onPartDetached -= OnHardpointDetached;
            if (aircraft != null) AegisTrigger.UnregisterPod(aircraft, this);
            base.OnDestroy();
        }
    }

    /// <summary>
    /// AEGIS-3 countermeasure coordinator. Manages per-aircraft pods, tap/cooldown gating, and closest inbound
    /// missile selection when the pilot triggers the AEGIS countermeasure.
    /// </summary>
    internal static class AegisTrigger
    {
        private sealed class DeployState
        {
            public readonly List<AegisCountermeasure> Pods = new List<AegisCountermeasure>();
            public int LastFrame = -1;
            public float LastSeen = -1f;
            public float NextFire;
            public float NextNotice;
        }

        private static readonly Dictionary<Aircraft, DeployState> States = new Dictionary<Aircraft, DeployState>();
        private static readonly List<Aircraft> DeadAircraft = new List<Aircraft>();
        private static readonly List<Missile> CandidateMissiles = new List<Missile>();
        private static AegisThreat[] candidateBuf = new AegisThreat[32];

        public static bool IsAegis(WeaponInfo info) =>
            info != null && info.name.StartsWith(VanguardKeys.AegisInfo, StringComparison.Ordinal);

        public static void Clear()
        {
            States.Clear();
            CandidateMissiles.Clear();
        }

        internal static void RegisterPod(Aircraft aircraft, AegisCountermeasure pod)
        {
            if (aircraft == null || pod == null) return;
            PruneDead();
            if (!States.TryGetValue(aircraft, out DeployState state))
                States[aircraft] = state = new DeployState();
            pod.PodIndex = state.Pods.Count;
            state.Pods.Add(pod);
        }

        internal static void UnregisterPod(Aircraft aircraft, AegisCountermeasure pod)
        {
            if (aircraft == null || !States.TryGetValue(aircraft, out DeployState state)) return;
            state.Pods.Remove(pod);
            for (int i = 0; i < state.Pods.Count; i++)
                if (state.Pods[i] != null) state.Pods[i].PodIndex = i;
            if (state.Pods.Count == 0) States.Remove(aircraft);
        }

        internal static bool HasPods(Aircraft aircraft) =>
            aircraft != null && States.ContainsKey(aircraft);

        internal static int TotalAmmo(Aircraft aircraft)
        {
            if (aircraft == null || !States.TryGetValue(aircraft, out DeployState state)) return 0;
            int sum = 0;
            foreach (AegisCountermeasure pod in state.Pods)
                if (pod != null) sum += pod.ammo;
            return sum;
        }

#if !PUBLIC_RELEASE
        internal static void TrimAircraftDarts(Aircraft aircraft, int keep)
        {
            if (aircraft == null || !States.TryGetValue(aircraft, out DeployState state)) return;
            int remaining = Mathf.Max(0, keep);
            foreach (AegisCountermeasure pod in state.Pods)
            {
                if (pod == null) continue;
                int take = Mathf.Min(remaining, pod.MaxAmmo);
                pod.TrimTo(take);
                remaining -= take;
            }
        }
#endif

        internal static void MirrorLaunch(Aircraft aircraft, byte podIndex, byte cellIndex, byte reportedAmmo)
        {
            if (aircraft == null || aircraft.IsServer || !States.TryGetValue(aircraft, out DeployState state) ||
                podIndex >= state.Pods.Count) return;
            AegisCountermeasure pod = state.Pods[podIndex];
            if (pod == null || !pod.LaunchCell(cellIndex, null)) return;
            pod.ammo = Mathf.Clamp(reportedAmmo, 0, pod.MaxAmmo);
            pod.RefreshSelectedHud();
            VanguardStats.Bump("aegis.mirror");
        }

        internal static void Fire(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.disabled || !aircraft.IsServer ||
                !States.TryGetValue(aircraft, out DeployState state) || Time.frameCount == state.LastFrame) return;
            float now = Time.timeSinceLevelLoad;
            bool held = AegisIntercept.IsHeld(now, state.LastSeen);
            state.LastFrame = Time.frameCount;
            state.LastSeen = now;
            if (now < state.NextFire) return;

            AegisCountermeasure pod = null;
            int cellIndex = -1;
            foreach (AegisCountermeasure candidate in state.Pods)
            {
                if (candidate != null && (cellIndex = candidate.ReadyCell()) >= 0)
                {
                    pod = candidate;
                    break;
                }
            }
            if (pod == null)
            {
                Notice(aircraft, state, AegisCue.Empty, now, held);
                return;
            }

            Missile threat = FindClosestThreat(aircraft, allowEngagedFallback: !held);
            if (threat == null)
            {
                VanguardStats.Bump("aegis.noThreat");
                Notice(aircraft, state, AegisCue.NoThreat, now, held);
                return;
            }

            if (!pod.LaunchCell(cellIndex, threat)) return;
            state.NextFire = now + AegisIntercept.FireCooldown;
            VanguardStats.Bump("aegis.cmLaunch");
            float range = FastMath.Distance(threat.GlobalPosition(), aircraft.GlobalPosition());
            AegisNet.RaiseLaunch(aircraft, (byte)pod.PodIndex, (byte)cellIndex, (byte)Mathf.Clamp(pod.ammo, 0, byte.MaxValue), range);
        }

        private static void Notice(Aircraft aircraft, DeployState state, AegisCue cue, float now, bool held)
        {
            if (held || now < state.NextNotice) return;
            state.NextNotice = now + AegisIntercept.NoticeCooldown;
            AegisNet.Raise(aircraft, cue);
        }

        private static Missile FindClosestThreat(Aircraft aircraft, bool allowEngagedFallback)
        {
            CandidateMissiles.Clear();
            GlobalPosition hostPos = aircraft.GlobalPosition();
            Vector3 hostVel = aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
            List<Unit> units = UnitRegistry.allUnits;
            int count = 0;

            for (int i = 0; i < units.Count; i++)
            {
                if (!(units[i] is Missile m) || m == null || m.disabled || m.owner == aircraft) continue;
                if (m.definition != null && VanguardKeys.RoleOf(m.definition.jsonKey) == VanguardRole.Interceptor) continue;

                if (count >= candidateBuf.Length) Array.Resize(ref candidateBuf, candidateBuf.Length * 2);
                Vector3 mVel = m.rb != null ? m.rb.velocity : Vector3.zero;
                candidateBuf[count] = new AegisThreat
                {
                    RelativePosition = m.GlobalPosition() - hostPos,
                    RelativeVelocity = mVel - hostVel,
                    MissileVelocity = mVel,
                    TrackingHost = m.targetID == aircraft.persistentID,
                    EnemyFaction = m.NetworkHQ != aircraft.NetworkHQ,
                    AlreadyEngaged = VanguardRegistry.IsAegisTargeting(aircraft, m),
                };
                CandidateMissiles.Add(m);
                count++;
            }

            int pick = AegisIntercept.PickClosest(candidateBuf, count, allowEngagedFallback);
            return pick >= 0 ? CandidateMissiles[pick] : null;
        }

        private static void PruneDead()
        {
            foreach (Aircraft a in States.Keys)
                if (a == null) DeadAircraft.Add(a);
            foreach (Aircraft a in DeadAircraft) States.Remove(a);
            DeadAircraft.Clear();
        }
    }

    /// <summary>AEGIS dart flight (server): cold-launch slew, lead pursuit and hit-to-kill contact.</summary>
    internal sealed partial class VanguardFlight
    {
        private bool snap; // manual SNAP at an air unit; a rejected snap flies out as a plain dart
        private int passDm = int.MaxValue;

        private void InitAegis()
        {
            snap = target is Aircraft;
            if (!missile.IsServer) return;
            if (snap && !SnapInEnvelope())
            {
                // The press was judged on the owner's peer; the server does not take its word for the envelope.
                snap = false;
                VanguardStats.Bump("aegis.snapRejected");
            }
            if (snap)
            {
                VanguardStats.Bump("aegis.snapAway"); // carried nose-forward and the target is ahead: no pitch-up
                return;
            }
            Vector3 up = launcher != null ? launcher.transform.up : Vector3.up;
            Vector3 reference = launcher != null ? launcher.transform.forward : Vector3.forward;
            missile.rb.rotation = Quaternion.LookRotation(up, reference); // Native rail has already dropped it .55 m clear.
            missile.rb.angularVelocity = Vector3.zero;
        }

        private float AegisLifetime() => snap ? AegisSnap.Lifetime : AegisIntercept.DefenceLifetime;

        private bool SnapInEnvelope()
        {
            if (launcher == null || target == null) return false;
            Vector3 to = target.GlobalPosition() - launcher.GlobalPosition();
            return AegisSnap.ServerAccepts(to.magnitude, Vector3.Angle(launcher.transform.forward, to));
        }

        private void PlanIntercept(GlobalPosition pos)
        {
            if (target == null || target.disabled)
            {
                Detonate();
                return;
            }
            GlobalPosition tPos = target.GlobalPosition();
            Vector3 tVel = target.rb != null ? target.rb.velocity : Vector3.zero;
            Vector3 rel = tPos - pos;
            float closing = Mathf.Max(100f, -Vector3.Dot(tVel - missile.rb.velocity, rel.normalized));
            float tgo = Mathf.Clamp(rel.magnitude / closing, 0f, 4f);
            missile.SetAimpoint(tPos + tVel * tgo, tVel);
        }

        private void SlewDuringDrop()
        {
            if (target == null || target.disabled) return;
            Rigidbody rb = missile.rb;
            Vector3 tVel = target.rb != null ? target.rb.velocity : Vector3.zero;
            Vector3 to = target.GlobalPosition() + tVel * 0.5f - missile.GlobalPosition();
            if (to.sqrMagnitude < 1f) return;
            Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, want, DropSlewDegPerSec * Time.fixedDeltaTime));
        }

        private void CheckIntercept()
        {
            if (target == null || target.disabled) return;
            Vector3 relative = target.GlobalPosition() - missile.GlobalPosition();
            if (missile.IsServer && missile.timeSinceSpawn >= AegisEnvelope.ArmSeconds && hasInterceptSample)
            {
                Vector3 closing = (target.rb != null ? target.rb.velocity : Vector3.zero) - missile.rb.velocity;
                if (!snap)
                {
                    // Sim readout: closest pass of the latest defence dart to its threat, in decimetres (swept over the tick).
                    Vector3 step = relative - previousInterceptRelative;
                    float along = step.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(-Vector3.Dot(previousInterceptRelative, step) / step.sqrMagnitude);
                    int pass = (int)((previousInterceptRelative + step * along).magnitude * 10f);
                    if (pass < passDm) VanguardStats.Counters["aegis.defencePassDm"] = passDm = pass;
                }
                if (target is Missile threat && AegisEnvelope.Contact(previousInterceptRelative, relative, AegisEnvelope.ProximityRadius))
                {
                    float damage = Mathf.Max(AegisSnap.Damage(PierceDamage(missile), closing.magnitude, 1f), AegisIntercept.MinKillDamage);
                    threat.TakeDamage(damage, 0f, 1f, 0f, 0f, Dealer());
                    VanguardStats.Bump("aegis.defenceContact");
                    AegisNet.Raise(launcher, AegisCue.Destroyed);
                    Detonate();
                    return;
                }
                if (snap && AegisSnap.Contact(previousInterceptRelative, relative, closing, Time.fixedDeltaTime,
                        AegisSnap.ContactRadius(target.maxRadius)))
                {
                    SnapHit(closing.magnitude);
                    return;
                }
            }
            previousInterceptRelative = relative;
            hasInterceptSample = true;
        }

        // The struck airframe part takes the dart's pierce (x2) directly, like a penetrating hit.
        private void SnapHit(float closing)
        {
            UnitPart struck = null;
            float best = float.MaxValue;
            Vector3 at = missile.transform.position;
            foreach (UnitPart part in target.partLookup)
            {
                if (part == null || part.IsDetached()) continue;
                float d = (part.transform.position - at).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    struck = part;
                }
            }
            struck?.TakeDamage(AegisSnap.Damage(PierceDamage(missile), closing, AegisSnap.DamageScale), 0f, 1f, 0f, 0f, Dealer());
            VanguardStats.Bump("aegis.snapHit");
            AegisNet.Raise(launcher, AegisCue.SnapHit);
            Detonate();
        }

        private PersistentID Dealer() => launcher != null ? launcher.persistentID : missile.persistentID;
    }
}
