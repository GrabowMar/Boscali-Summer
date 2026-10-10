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
        private const float ScanInterval = 0.25f;

        private static AegisService instance;
        private readonly Dictionary<Aircraft, InterceptPicker> watched = new Dictionary<Aircraft, InterceptPicker>();
        private readonly List<ThreatView> views = new List<ThreatView>();
        private readonly List<Missile> threats = new List<Missile>();
        private readonly List<Aircraft> drop = new List<Aircraft>();
        private float nextScan;

        private void Awake() => instance = this;

        public void ResetForScene()
        {
            watched.Clear();
            VanguardRegistry.Clear();
            DroneOrders.Clear();
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
            drop.Clear();
            foreach (KeyValuePair<Aircraft, InterceptPicker> pair in watched)
                if (!Scan(pair.Key, pair.Value)) drop.Add(pair.Key);
            for (int i = 0; i < drop.Count; i++) watched.Remove(drop[i]);
        }

        /// <returns>false when the aircraft no longer needs watching.</returns>
        private bool Scan(Aircraft aircraft, InterceptPicker picker)
        {
            if (aircraft == null || aircraft.disabled) return false;
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            WeaponStation station = AegisStation(aircraft);
            if (warning == null || station == null || warning.knownMissiles.Count == 0) return false;

            views.Clear();
            threats.Clear();
            GlobalPosition self = aircraft.GlobalPosition();
            Vector3 selfVel = aircraft.rb.velocity;
            foreach (Missile m in warning.knownMissiles)
            {
                if (m == null || m.disabled) continue;
                Vector3 rel = self - m.GlobalPosition();
                float range = rel.magnitude;
                float closing = Vector3.Dot(m.rb.velocity - selfVel, rel / Mathf.Max(range, 1f));
                views.Add(new ThreatView(m.GetInstanceID(), range, closing));
                threats.Add(m);
            }
            float now = Time.timeSinceLevelLoad;
            int pick = picker.Pick(now, views);
            if (pick < 0) return true;
            Missile threat = null;
            for (int i = 0; i < views.Count; i++)
                if (views[i].Id == pick) threat = threats[i];
            station.LaunchMount(aircraft, threat, threat.GlobalPosition());
            picker.Fired(now, pick);
            return true;
        }
    }
}
