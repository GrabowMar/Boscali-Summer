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

        private readonly Dictionary<Unit, float> readyAt = new Dictionary<Unit, float>();
        private readonly List<Missile> inbound = new List<Missile>();
        private float next;

        public void ResetForScene()
        {
            readyAt.Clear();
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
                if (s?.WeaponInfo != null && s.WeaponInfo.name.Contains(AlexKite.InfoName) && (!loaded || s.Ammo > 0)) return s;
            }
            return null;
        }

        private void Update()
        {
            float now = Time.timeSinceLevelLoad;
            if (now < next || !GameAccess.IsServer()) return;
            next = now + ScanInterval;
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
                if (units[i] is Aircraft a && !a.disabled && a.LocalSim && a.Player == null) TryDeploy(a, now);
        }

        private void TryDeploy(Aircraft a, float now)
        {
            WeaponStation station = PodStation(a, true);
            if (station == null || AlexBoard.TowedBy(a.persistentID.Id, out _)) return;
            if (readyAt.TryGetValue(a, out float at) && now < at) return;
            if (a.radarAlt < MinAgl || !station.Ready()) return;
            float nearest = TowedTrail.NoThreat;
            ThreatScan.Inbound(a, inbound);
            for (int i = 0; i < inbound.Count; i++)
                if (SeductionRule.IsRadarSeeker(inbound[i].GetSeekerType()))
                    nearest = Mathf.Min(nearest, FastMath.Distance(inbound[i].GlobalPosition(), a.GlobalPosition()));
            if (!AlexKite.AiDeploys(nearest)) return;
            readyAt[a] = now + DeployCooldown;
            station.LaunchMount(a, null, a.GlobalPosition() + a.transform.forward * 50000f);
            VanguardStats.Bump("alex.ai_deployed");
        }
    }

    /// <summary>
    /// Fire press on an ALE-X station while a decoy is on the fiber cuts it instead of launching the next one
    /// (WeaponStation.LaunchMount prefix, see AlexLaunchPatch). The owning peer asks; the host decides.
    /// </summary>
    internal static class AlexFire
    {
        /// <summary>True when the press was swallowed as a cut.</summary>
        public static bool Intercept(WeaponStation station, Unit owner)
        {
            if (station?.WeaponInfo == null || !station.WeaponInfo.name.Contains(AlexKite.InfoName) ||
                !(owner is Aircraft aircraft) || !AlexBoard.TowedBy(aircraft.persistentID.Id, out _)) return false;
            if (GameAccess.IsServer()) return AlexService.Cut(aircraft, aircraft.Player != null ? KiteCut.Pilot : KiteCut.Auto);
            if (!aircraft.LocalSim) return false; // another pilot's press, replayed here for the visuals
            Networking.AlexNet.RequestCut();
            return true;
        }
    }
}
