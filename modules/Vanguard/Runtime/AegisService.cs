using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// AEGIS hard-kill fire control (server). Aircraft enter the watch list when a missile locks them
    /// (AegisLockPatch) and leave once their warning clears, so idle cost is zero.
    /// </summary>
    internal sealed class AegisService : MonoBehaviour, ISceneService
    {
        private const float ScanInterval = 0.15f;

        private static AegisService instance;
        private readonly Dictionary<Aircraft, InterceptPicker> watched = new Dictionary<Aircraft, InterceptPicker>();
        private readonly List<ThreatView> views = new List<ThreatView>();
        private readonly List<Missile> threats = new List<Missile>();
        private readonly List<Aircraft> drop = new List<Aircraft>();
        private float nextScan;
        private readonly Dictionary<Missile,float> confirmed = new Dictionary<Missile,float>();
        private readonly List<Missile> stale = new List<Missile>();

        private void Awake() => instance = this;

        public void ResetForScene()
        {
            watched.Clear();
            confirmed.Clear();
            VanguardRegistry.Clear();
            DroneOrders.Clear();
            VanguardStats.Clear();
        }

        public static void Watch(Aircraft aircraft)
        {
            if (instance == null || aircraft == null || instance.watched.ContainsKey(aircraft)) return;
            if (AegisStation(aircraft) != null) instance.watched[aircraft] = new InterceptPicker();
        }

        private static WeaponStation AegisStation(Aircraft aircraft)
        {
            var stations = aircraft.weaponStations;
            for (int i = 0; i < stations.Count; i++)
            {
                WeaponStation s = stations[i];
                if (s?.WeaponInfo != null && s.Ammo > 0 && s.WeaponInfo.name.Contains(VanguardKeys.AegisInfo)) return s;
            }
            return null;
        }

        private void FixedUpdate()
        {
            if (watched.Count == 0 || Time.timeSinceLevelLoad < nextScan || !GameAccess.IsServer()) return;
            nextScan = Time.timeSinceLevelLoad + ScanInterval;
            stale.Clear();
            foreach (var pair in confirmed)
                if (pair.Key == null || pair.Key.disabled || Time.timeSinceLevelLoad-pair.Value > 10f) stale.Add(pair.Key);
            foreach (var missile in stale) confirmed.Remove(missile);
            drop.Clear();
            foreach (KeyValuePair<Aircraft, InterceptPicker> pair in watched)
                if (!Scan(pair.Key, pair.Value)) drop.Add(pair.Key);
            for (int i = 0; i < drop.Count; i++) watched.Remove(drop[i]);
        }

        /// <returns>false when the aircraft no longer needs watching.</returns>
        private bool Scan(Aircraft aircraft, InterceptPicker picker)
        {
            if (aircraft == null || aircraft.disabled) return false;
            WeaponStation station = AegisStation(aircraft);
            if (station == null) return false;

            views.Clear();
            ThreatScan.Inbound(aircraft, threats); // Server warning lists can be empty for client-owned aircraft.
            if (threats.Count == 0) return false;
            GlobalPosition self = aircraft.GlobalPosition();
            Vector3 selfVel = aircraft.rb.velocity;
            float now = Time.timeSinceLevelLoad;
            foreach (Missile m in threats)
            {
                if (m == null || m.disabled || m.NetworkHQ == aircraft.NetworkHQ || m.owner == aircraft) continue;
                Vector3 rel = self - m.GlobalPosition();
                float range = rel.magnitude;
                float closing = Vector3.Dot(m.rb.velocity - selfVel, rel / Mathf.Max(range, 1f));
                bool visible=range <= InterceptPicker.MaxRange && !Physics.Linecast(
                    aircraft.transform.position-aircraft.transform.up*.5f,m.transform.position,(int)PhysicsLayers.StaticsMask);
                if (!visible) { confirmed.Remove(m); continue; }
                if (!confirmed.TryGetValue(m,out float acquired)) { confirmed[m]=now; continue; }
                if (now-acquired < AegisEnvelope.SensorConfirmSeconds) continue;
                views.Add(new ThreatView(m.GetInstanceID(), range, closing));
            }
            int pick = picker.Pick(now, views);
            if (pick < 0) return true;
            Missile threat = null;
            for (int i = 0; i < threats.Count; i++)
                if (threats[i] != null && threats[i].GetInstanceID() == pick) threat = threats[i];
            if (threat == null) return true;
            int ammo = station.Ammo;
            station.LaunchMount(aircraft, threat, threat.GlobalPosition());
            if (station.Ammo < ammo) picker.Fired(now, pick);
            return true;
        }
    }
}
