#if !PUBLIC_RELEASE
using System;
using System.Linq;
using Newtonsoft.Json;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>Explicit host/client sandbox proof; absent from player-facing builds.</summary>
    public static class SkylarkPeerProof
    {
        private static Aircraft spawned;
        private static SkylarkPeerControls controls;
        private static bool Server => NetworkManagerNuclearOption.i?.Server?.Active == true;

        private static void RequireHost()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")) || !Server)
                throw new InvalidOperationException("Skylark peer mutations require a nomodkit loopback host");
        }

        public static string Spawn()
        {
            RequireHost();
            if (spawned != null) throw new InvalidOperationException("Peer proof aircraft already exists");
            AircraftDefinition definition = SkylarkTemplate.Definition;
            if (definition == null) throw new InvalidOperationException("Skylark catalogue is not ready");
            spawned = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null, definition.unitPrefab,
                definition.aircraftParameters.loadouts[0], 1f, default,
                new GlobalPosition(10000f, 2500f, -35000f), Quaternion.identity, Vector3.zero,
                null, FactionRegistry.GetSortedHQs()[0], "SkylarkPeerProof", .5f, .5f);
            if (spawned == null) throw new InvalidOperationException("Native spawner returned null");
            spawned.SetGear(false);
            controls = new SkylarkPeerControls();
            spawned.pilots[0].SwitchStateNew(controls);
            return Snapshot();
        }

        public static string SetMast(bool deployed)
        {
            RequireHost();
            if (spawned == null) throw new InvalidOperationException("Spawn first");
            SkylarkNet.Request(spawned, SkylarkNet.MastAction, (byte)(deployed ? 1 : 0));
            return Snapshot();
        }

        public static string SelectStation(byte station)
        {
            RequireHost();
            if (spawned == null) throw new InvalidOperationException("Spawn first");
            spawned.SetActiveStation(station);
            return Snapshot();
        }

        public static string CommandAngle(float degrees)
        {
            RequireHost();
            if (spawned == null || controls == null) throw new InvalidOperationException("Spawn first");
            controls.Degrees = Mathf.Clamp(degrees, 0f, 90f);
            return Snapshot();
        }

        public static string DamageWingtip()
        {
            RequireHost();
            if (spawned == null) throw new InvalidOperationException("Spawn first");
            UnitPart part = spawned.partLookup.First(p => p.name == "wingtip_L");
            part.TakeDamage(1000f, 0f, 1f, 0f, 0f, default);
            return Snapshot();
        }

        public static string Observe()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")))
                throw new InvalidOperationException("Observation requires a nomodkit loopback peer");
            Aircraft aircraft = UnityEngine.Object.FindObjectsOfType<Aircraft>()
                .Single(a => a.definition != null && a.definition.jsonKey == SkylarkTemplate.Key);
            // Observe each peer's native representation; the host simulates jointed
            // physics and the remote client deliberately receives a simple proxy.
            CameraStateManager.i.SetFollowingUnit(aircraft);
            return Snapshot();
        }

        public static string Snapshot()
        {
            AircraftDefinition definition = SkylarkTemplate.Definition;
            return JsonConvert.SerializeObject(new
            {
                registered = definition != null,
                server = Server,
                definitionIndex = definition == null ? -1 : ((INetworkDefinition)definition).LookupIndex,
                instances = UnityEngine.Object.FindObjectsOfType<Aircraft>()
                    .Where(a => a.definition != null && a.definition.jsonKey == SkylarkTemplate.Key).Select(a => new
                    {
                        key = a.definition.jsonKey,
                        netId = a.GetComponent<Mirage.NetworkIdentity>().NetId,
                        prefabHash = a.GetComponent<Mirage.NetworkIdentity>().PrefabHash,
                        definitionIndex = ((INetworkDefinition)a.definition).LookupIndex,
                        speed = a.speed, altitude = a.radarAlt, disabled = a.disabled, simplePhysics = a.simplePhysics,
                        localSim = a.LocalSim,
                        angle = a.GetComponent<DuctedThrustSystem>().angle,
                        mast = a.GetComponent<SkylarkMast>().Deployed, rcs = a.RCS,
                        gunDeploy = a.GetComponent<SkylarkTurret>().Deploy,
                        weapons = a.weaponStations.Select(s => new { s.Number, s.Ammo, s.FullAmmo, name = s.WeaponInfo.weaponName }),
                        parts = a.partLookup.Select(p => new { p.name, p.hitPoints, detached = p.IsDetached() }),
                        ducts = a.partLookup.SelectMany(p => p.GetComponentsInChildren<Transform>(true)).Distinct()
                            .Where(t => t.name == "Duct_FL" || t.name == "Duct_FR" || t.name == "Duct_RL" || t.name == "Duct_RR")
                            .Select(t => new { t.name, rotation = new[] { t.localRotation.x, t.localRotation.y, t.localRotation.z, t.localRotation.w } })
                    })
            });
        }
    }

    internal sealed class SkylarkPeerControls : PilotBaseState
    {
        internal float Degrees = 90f;
        public override void EnterState(Pilot pilot)
        {
            this.pilot = pilot; aircraft = pilot.aircraft; controlInputs = aircraft.GetInputs();
            stateDisplayName = "Skylark loopback proof";
            if (aircraft.IsAutoHoverEnabled()) aircraft.GetControlsFilter().ToggleAutoHover();
            DuctedThrustSystem duct = aircraft.GetComponent<DuctedThrustSystem>();
            object mode = SkylarkTemplate.Get<object>(duct, "mode");
            SkylarkTemplate.Set(duct, "mode", Enum.Parse(mode.GetType(), "Manual"));
        }
        public override void UpdateState(Pilot pilot) { }
        public override void FixedUpdateState(Pilot pilot)
        {
            controlInputs.pitch = 0; controlInputs.roll = 0; controlInputs.yaw = 0;
            controlInputs.throttle = .8f; controlInputs.customAxis1 = 1f - Degrees / 90f;
            aircraft.FilterInputs();
        }
        public override void LeaveState() { aircraft.SetFlightAssist(true); }
    }
}
#endif
