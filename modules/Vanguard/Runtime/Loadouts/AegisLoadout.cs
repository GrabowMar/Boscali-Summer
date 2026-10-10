using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Presentation;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// AEGIS-3 SNAP trigger. Runs where the aircraft is simulated (owner client for players, server for AI) in place of
    /// WeaponManager.Fire for the AEGIS station (AegisSnapPatch). One dart per press goes through the vanilla
    /// LaunchMount path, so the rail, the Cmd/Rpc replay on every peer and the server spawn stay native.
    /// </summary>
    internal static class AegisTrigger
    {
        private static readonly Dictionary<WeaponStation, float> LastPress = new Dictionary<WeaponStation, float>();
        private static readonly List<WeaponStation> Dead = new List<WeaponStation>();
        private static readonly Dictionary<WeaponInfo, bool> Known = new Dictionary<WeaponInfo, bool>();
        private static readonly List<Missile> Inbound = new List<Missile>();

        // Per-frame callers (HUD, the WeaponManager.Fire patch): WeaponInfo.name allocates a string, so cache it per info asset.
        public static bool IsAegis(WeaponStation station)
        {
            WeaponInfo info = station?.WeaponInfo;
            if (info == null) return false;
            if (!Known.TryGetValue(info, out bool aegis)) Known[info] = aegis = info.name.Contains(VanguardKeys.AegisInfo);
            return aegis;
        }

        public static void Clear()
        {
            LastPress.Clear();
            Known.Clear();
        }

        // Stations are rebuilt on every loadout change or respawn; their old WeaponStation objects hold dead cells.
        private static void PruneDead()
        {
            foreach (WeaponStation s in LastPress.Keys)
                if (s.Weapons.Count == 0 || s.Weapons[0] == null) Dead.Add(s);
            for (int i = 0; i < Dead.Count; i++) LastPress.Remove(Dead[i]);
            Dead.Clear();
        }

        public static SnapVerdict Press(Aircraft aircraft, WeaponStation station, Unit target)
        {
            float now = Time.timeSinceLevelLoad;
            bool known = LastPress.TryGetValue(station, out float last);
            if (!known) PruneDead();
            bool held = known && now - last < AegisSnap.HeldGap;
            LastPress[station] = now;
            // Tap to fire: holding the trigger must not dump the darts the automatic defence relies on.
            if (held || aircraft.remoteSim || station.SafetyIsOn(aircraft) || station.SalvoInProgress) return SnapVerdict.Busy;
            if (station.Ammo > 0 && !station.Ready())
            {
                // Also covers the pod's own automatic defence launches, which the pilot did not trigger.
                AegisHud.Notify(aircraft, HudTone.Caution, "SNAP — NO SOLUTION", "POD CYCLING");
                return SnapVerdict.Busy;
            }

            bool has = target != null && !target.disabled;
            Vector3 to = has ? target.GlobalPosition() - aircraft.GlobalPosition() : Vector3.zero;
            float range = to.magnitude;
            SnapVerdict verdict = AegisSnap.Judge(has, target is Aircraft, range, Vector3.Angle(aircraft.transform.forward, to),
                station.Ammo, ThreatInbound(aircraft));
            if (verdict != SnapVerdict.Fire)
            {
                VanguardStats.Bump("aegis.noSolution");
                if (verdict == SnapVerdict.Reserved) VanguardStats.Bump("aegis.reserved");
                AegisHud.Notify(aircraft, HudTone.Caution, "SNAP — NO SOLUTION", AegisSnap.Reason(verdict, range));
                return verdict;
            }
            station.LaunchMount(aircraft, target, target.GlobalPosition());
            VanguardStats.Bump("aegis.snapPress");
            AegisHud.Notify(aircraft, HudTone.Info, "SNAP AWAY", AegisSnap.Km(range) + " km");
            return verdict;
        }

        private static bool ThreatInbound(Aircraft aircraft)
        {
            ThreatScan.Inbound(aircraft, Inbound);
            for (int i = 0; i < Inbound.Count; i++)
                if (Inbound[i].NetworkHQ != aircraft.NetworkHQ) return true;
            return false;
        }
    }

    /// <summary>AEGIS dart flight (server): cold-launch slew, lead pursuit and hit-to-kill contact for both uses.</summary>
    internal sealed partial class VanguardFlight
    {
        private bool snap; // manual SNAP at an air unit, as opposed to the automatic missile defence
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

        private float AegisLifetime() => snap ? AegisSnap.Lifetime : AegisSnap.DefenceLifetime;

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
                    threat.TakeDamage(AegisSnap.Damage(PierceDamage(missile), closing.magnitude, 1f), 0f, 1f, 0f, 0f, Dealer());
                    VanguardStats.Bump("aegis.defenceContact");
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
