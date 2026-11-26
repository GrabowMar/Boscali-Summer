using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    /// <summary>Server-side AEGIS moments the carrier's pilot hears about on the HUD (Intercept also syncs the pod cell).</summary>
    internal enum AegisCue : byte { Intercept = 1, SnapHit = 2, Destroyed = 3, NoThreat = 4, Empty = 5 }

    [NetworkMessage]
    internal struct AegisEvent
    {
        public byte Protocol;
        public uint LauncherId;
        public byte Cue;
        public byte PodIndex;  // AEGIS pod index on the aircraft (AegisNet.NoPod when not a launch)
        public byte CellIndex; // cell index fired inside that pod
        public byte Ammo;      // that pod's ammo after the launch
        public ushort RangeM;  // threat range in metres (0 when not applicable)
    }

    /// <summary>
    /// Server-to-client HUD cues and AEGIS CM pod cell sync; a client cannot fire or score anything through this.
    /// Launch decisions run authoritatively on the server inside AegisCountermeasure.Fire; Intercept broadcasts the
    /// pod/cell index and new ammo so remote peers and client pilots replay the door open, rail drop and ammo count.
    /// </summary>
    internal sealed class AegisNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 3;
        internal const byte NoPod = byte.MaxValue;
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
                    w.WriteByte(v.PodIndex);
                    w.WriteByte(v.CellIndex);
                    w.WriteByte(v.Ammo);
                    w.WriteUInt16(v.RangeM);
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
                        PodIndex = r.ReadByte(),
                        CellIndex = r.ReadByte(),
                        Ammo = r.ReadByte(),
                        RangeM = r.ReadUInt16(),
                    };
                }));
        }

        public void ResetForScene() { }

        private void Update()
        {
            NetFeed.Poll(ClientHandlers, ref nextRegistration);
        }

        /// <summary>Server: show a non-launch HUD cue to the launcher's pilot.</summary>
        internal static void Raise(Unit launcher, AegisCue cue)
        {
            if (launcher == null) return;
            AegisHud.Cue(launcher, cue);
            NetFeed.Broadcast(new AegisEvent
            {
                Protocol = ProtocolVersion,
                LauncherId = launcher.persistentID.Id,
                Cue = (byte)cue,
                PodIndex = NoPod,
                CellIndex = NoPod,
                Ammo = 0,
                RangeM = 0,
            });
        }

        /// <summary>Server: announce an AEGIS CM dart launch, syncing cell visuals and ammo to clients.</summary>
        internal static void RaiseLaunch(Aircraft launcher, byte podIndex, byte cellIndex, byte ammo, float rangeM)
        {
            if (launcher == null) return;
            ushort clampedRange = (ushort)Mathf.Clamp(Mathf.RoundToInt(rangeM), 0, ushort.MaxValue);
            AegisHud.Cue(launcher, AegisCue.Intercept, clampedRange);
            NetFeed.Broadcast(new AegisEvent
            {
                Protocol = ProtocolVersion,
                LauncherId = launcher.persistentID.Id,
                Cue = (byte)AegisCue.Intercept,
                PodIndex = podIndex,
                CellIndex = cellIndex,
                Ammo = ammo,
                RangeM = clampedRange,
            });
        }

        private void Receive(INetworkPlayer sender, AegisEvent message)
        {
            if (message.Protocol != ProtocolVersion || GameAccess.IsServer()) return;
            if (message.Cue < (byte)AegisCue.Intercept || message.Cue > (byte)AegisCue.Empty) return;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = message.LauncherId }, out Unit unit)) return;
            if (message.Cue == (byte)AegisCue.Intercept && message.PodIndex != NoPod && unit is Aircraft aircraft)
            {
                try
                {
                    AegisTrigger.MirrorLaunch(aircraft, message.PodIndex, message.CellIndex, message.Ammo);
                }
                catch (Exception e)
                {
                    Plugin.Logger?.LogWarning("AEGIS pod sync failed: " + e.Message);
                }
            }
            AegisHud.Cue(unit, (AegisCue)message.Cue, message.RangeM);
        }

        private void OnDestroy() => ClientHandlers.Release();
    }
}
