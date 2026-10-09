using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// ALE-X KITE host side. <see cref="Cut"/> is the one door for every cut that comes from a fire press (the pilot's
    /// own peer, a remote pilot's intent, an AI host). The scene service gives AI hosts a fire button: a pod-carrying
    /// AI aircraft deploys when a radar missile tracks it inside AlexKite.AiDeployRange, and cuts through
    /// VanguardFlight.SeduceTowed once the kite holds a missile.
    /// </summary>
    internal sealed class AlexService : MonoBehaviour, ISceneService
    {
        private const float ScanInterval = 1f;
        private const float DeployCooldown = 15f;
        private const float MinAgl = 300f;

        private readonly Dictionary<uint, float> readyAt = new Dictionary<uint, float>();
        private readonly List<uint> expired = new List<uint>();
        private readonly List<Missile> inbound = new List<Missile>();
        private float next;

        public void ResetForScene()
        {
            readyAt.Clear();
            AlexFire.Clear();
            AlexBoard.Clear();
            AlexDeflect.Clear();
            AlexSuppress.Clear();
        }

        /// <summary>Server: cut the fiber of the decoy this aircraft is towing. False when it tows none.</summary>
        public static bool Cut(Aircraft host, KiteCut reason)
        {
            if (host == null || !AlexBoard.TowedBy(host.persistentID.Id, out AlexView view)) return false;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = view.Decoy }, out Unit unit) || !(unit is Missile decoy) ||
                !VanguardRegistry.TryGet(decoy, out VanguardFlight flight)) return false;
            return flight.CutKite(reason);
        }

        /// <summary>The aircraft's ALE-X station, or null; with <paramref name="loaded"/> only one that still has a decoy.</summary>
        public static WeaponStation PodStation(Aircraft a, bool loaded)
        {
            if (a == null || a.weaponStations == null) return null;
            for (int i = 0; i < a.weaponStations.Count; i++)
            {
                WeaponStation s = a.weaponStations[i];
                if (AlexFire.IsPod(s) && (!loaded || s.Ammo > 0)) return s;
            }
            return null;
        }

        private void Update()
        {
            float now = Time.timeSinceLevelLoad;
            if (now < next || !GameAccess.IsServer()) return;
            next = now + ScanInterval;
            PruneCooldowns(now);
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
                if (units[i] is Aircraft a && !a.disabled && a.LocalSim && a.Player == null) TryDeploy(a, now);
        }

        // A cooldown that has run out says nothing, so dead aircraft never pile up in the table.
        private void PruneCooldowns(float now)
        {
            expired.Clear();
            foreach (KeyValuePair<uint, float> pair in readyAt)
                if (now >= pair.Value) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++) readyAt.Remove(expired[i]);
        }

        private void TryDeploy(Aircraft a, float now)
        {
            WeaponStation station = PodStation(a, true);
            if (station == null || AlexBoard.TowedBy(a.persistentID.Id, out _)) return;
            if (readyAt.TryGetValue(a.persistentID.Id, out float at) && now < at) return;
            if (a.radarAlt < MinAgl || !station.Ready()) return;
            float nearest = TowedTrail.NoThreat;
            ThreatScan.Inbound(a, inbound);
            for (int i = 0; i < inbound.Count; i++)
                if (SeductionRule.IsRadarSeeker(inbound[i].GetSeekerType()))
                    nearest = Mathf.Min(nearest, FastMath.Distance(inbound[i].GlobalPosition(), a.GlobalPosition()));
            if (!AlexKite.AiDeploys(nearest)) return;
            readyAt[a.persistentID.Id] = now + DeployCooldown;
            station.LaunchMount(a, null, a.GlobalPosition() + a.transform.forward * 50000f);
            VanguardStats.Bump("alex.ai_deployed");
        }
    }

    /// <summary>
    /// The ALE-X pod's fire button. The player's trigger is a held button: Pilot.Fire calls WeaponManager.Fire every
    /// frame it is down, and the pod's last decoy leaves Ready() false (Ammo 0), so the press is taken in a
    /// WeaponManager.Fire prefix (AlexPressPatch) before any of that. A press acts once, on its first call:
    /// launch the next decoy, or CUT the one on the fiber. The rest of the hold is swallowed, so a held button
    /// never cuts one decoy and launches the next. The owning peer asks, the host decides.
    /// </summary>
    internal static class AlexFire
    {
        private static readonly Dictionary<WeaponInfo, bool> PodInfos = new Dictionary<WeaponInfo, bool>();
        private static readonly Dictionary<uint, float> LastCall = new Dictionary<uint, float>();

        public static void Clear() => LastCall.Clear();

        /// <summary>The station carries the ALE-X decoy. The name test runs once per WeaponInfo: Object.name allocates.</summary>
        public static bool IsPod(WeaponStation station)
        {
            WeaponInfo info = station?.WeaponInfo;
            if (info == null) return false;
            if (!PodInfos.TryGetValue(info, out bool pod)) PodInfos[info] = pod = info.name.Contains(AlexKite.InfoName);
            return pod;
        }

        /// <summary>WeaponManager.Fire prefix, for the aircraft this peer simulates. True = the call is consumed.</summary>
        public static bool Press(Aircraft aircraft, WeaponStation station)
        {
            if (aircraft == null || !aircraft.LocalSim || !IsPod(station)) return false;
            uint id = aircraft.persistentID.Id;
            float now = Time.unscaledTime;
            bool fresh = !LastCall.TryGetValue(id, out float last) || AlexKite.FreshPress(now - last, Time.unscaledDeltaTime);
            LastCall[id] = now;
            switch (AlexKite.Press(fresh, AlexBoard.TowedBy(id, out _)))
            {
                case PodPress.Cut:
                    Ask(aircraft, KiteCut.Pilot);
                    break;
                case PodPress.Launch:
                    // Vanilla would salvo over a multi-target list; the pod is one press, one decoy.
                    if (!station.SafetyIsOn(aircraft) && station.Ready() && !station.SalvoInProgress)
                        station.LaunchMount(aircraft, null, aircraft.GlobalPosition() + aircraft.transform.forward * 50000f);
                    break;
            }
            return true;
        }

        /// <summary>
        /// WeaponStation.LaunchMount prefix: the AI and sim paths, which never come through WeaponManager.Fire
        /// (a player's launch is handled by <see cref="Press"/>). True when the call was swallowed as a cut.
        /// </summary>
        public static bool Intercept(WeaponStation station, Unit owner) =>
            owner is Aircraft aircraft && aircraft.Player == null && IsPod(station) &&
            AlexBoard.TowedBy(aircraft.persistentID.Id, out _) && Ask(aircraft, KiteCut.Auto);

        // The host cuts at once; another peer's owner sends the intent and lets the host decide.
        private static bool Ask(Aircraft aircraft, KiteCut reason)
        {
            if (GameAccess.IsServer()) return AlexService.Cut(aircraft, reason);
            if (!aircraft.LocalSim) return false;
            Networking.AlexNet.RequestCut();
            return true;
        }
    }
}
