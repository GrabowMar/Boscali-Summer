using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    /// <summary>Server-side AEGIS moments the carrier's pilot hears about on the HUD (Intercept also syncs the pod).</summary>
    internal enum AegisCue : byte { Intercept = 1, SnapHit = 2 }

    [NetworkMessage]
    internal struct AegisEvent
    {
        public byte Protocol;
        public uint LauncherId;
        public byte Cue;
        public byte Station; // weapon station number the server fired from (AegisNet.NoStation when the cue is not a launch)
        public byte Ammo;    // that station's ammo after the launch
    }

    /// <summary>
    /// Server-to-client HUD cues and pod sync only; a client cannot fire or score anything through this. A defence dart
    /// is launched by the server, and Aircraft.RpcLaunchMissile skips the peer that flies the aircraft, so that peer's
    /// station would keep its old ammo and an unfired cell: the Intercept cue carries the new ammo and replays the cell.
    /// </summary>
    internal sealed class AegisNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 2;
        internal const byte NoStation = byte.MaxValue;
        private HandlerSlot slot;
        private HandlerSlot ClientHandlers => slot ??= HandlerSlot.Of<AegisEvent>(Receive);
        private float nextRegistration;

        private void Awake()
        {
            MirageSerializers.Strict.Install<AegisEvent>(
                (Action<NetworkWriter, AegisEvent>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WritePackedUInt32(v.LauncherId);
                    w.WriteByte(v.Cue);
                    w.WriteByte(v.Station);
                    w.WriteByte(v.Ammo);
                }),
                (Func<NetworkReader, AegisEvent>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new AegisEvent { Protocol = protocol };
                    return new AegisEvent
                    {
                        Protocol = protocol,
                        LauncherId = r.ReadPackedUInt32(),
                        Cue = r.ReadByte(),
                        Station = r.ReadByte(),
                        Ammo = r.ReadByte(),
                    };
                }));
        }

        public void ResetForScene() { }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + .5f;
            ClientHandlers.Swap(NetworkManagerNuclearOption.i?.Client?.MessageHandler);
        }

        /// <summary>Server: show the cue to the launcher's pilot, wherever that pilot sits. <paramref name="station"/> = the pod just fired.</summary>
        internal static void Raise(Unit launcher, AegisCue cue, WeaponStation station = null)
        {
            if (launcher == null) return;
            AegisHud.Cue(launcher, cue); // host's own aircraft
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(new AegisEvent
            {
                Protocol = ProtocolVersion,
                LauncherId = launcher.persistentID.Id,
                Cue = (byte)cue,
                Station = station != null ? station.Number : NoStation,
                Ammo = station != null ? (byte)Mathf.Clamp(station.Ammo, 0, byte.MaxValue) : (byte)0,
            }, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void Receive(INetworkPlayer sender, AegisEvent message)
        {
            if (message.Protocol != ProtocolVersion || GameAccess.IsServer()) return;
            if (message.Cue != (byte)AegisCue.Intercept && message.Cue != (byte)AegisCue.SnapHit) return;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = message.LauncherId }, out Unit unit)) return;
            if (message.Cue == (byte)AegisCue.Intercept && message.Station != NoStation && unit is Aircraft aircraft && aircraft.LocalSim)
            {
                try
                {
                    MirrorLaunch(aircraft, message.Station, message.Ammo);
                }
                catch (Exception e)
                {
                    Plugin.Logger?.LogWarning("AEGIS pod sync failed: " + e.Message);
                }
            }
            AegisHud.Cue(unit, (AegisCue)message.Cue);
        }

        // Private WeaponStation/MountedMissile handles, resolved on first use so a rename only costs the sync.
        internal static class Cells
        {
            public static readonly AccessTools.FieldRef<WeaponStation, int> Index = AccessTools.FieldRefAccess<WeaponStation, int>("weaponIndex");
            public static readonly AccessTools.FieldRef<MountedMissile, bool> Fired = AccessTools.FieldRefAccess<MountedMissile, bool>("fired");
        }

        /// <summary>
        /// What WeaponStation.LaunchMount did on the server, minus Weapon.Fire (which would Cmd a second launch from this
        /// peer): ammo, next cell, the cell marked fired and hidden (the cover opens from it), cooldown stamp.
        /// </summary>
        private static void MirrorLaunch(Aircraft aircraft, byte stationNumber, int ammo)
        {
            if (stationNumber >= aircraft.weaponStations.Count) return;
            WeaponStation station = aircraft.weaponStations[stationNumber];
            if (!AegisTrigger.IsAegis(station)) return;
            if (station.Ammo > ammo) // already in step (a resync arrived first): nothing to replay
            {
                ref int index = ref Cells.Index(station);
                if (index < station.Weapons.Count)
                {
                    Weapon cell = station.Weapons[index];
                    if (cell is MountedMissile mounted) Cells.Fired(mounted) = true;
                    cell.ammo = 0;
                    cell.gameObject.SetActive(false);
                    index++;
                }
            }
            station.Ammo = ammo;
            station.LastFiredTime = Time.timeSinceLevelLoad;
            station.Updated();
            VanguardStats.Bump("aegis.mirror");
        }

        private void OnDestroy() => ClientHandlers.Release();
    }
}
