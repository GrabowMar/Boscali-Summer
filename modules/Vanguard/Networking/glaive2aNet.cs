using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    [NetworkMessage]
    internal struct SentinelState
    {
        public byte Protocol;
        public uint MissileId, Shots, TargetId, Rounds, Range;
        public byte Phase, HoverLeft;
        public float Yaw, Pitch;
    }

    /// <summary>GLAIVE-2A server-to-client presentation (phase, turret pose, shots, HUD numbers). Clients never steer or fire.</summary>
    internal sealed class SentinelNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 1;
        private HandlerSlot slot;
        private HandlerSlot ClientHandlers => slot ??= HandlerSlot.Of<SentinelState>(Receive);
        private float nextRegistration;

        private void Awake()
        {
            MirageSerializers.Strict.Install<SentinelState>(
                (Action<NetworkWriter, SentinelState>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WritePackedUInt32(v.MissileId);
                    w.WriteByte(v.Phase);
                    w.WriteByte(v.HoverLeft);
                    w.WriteSingle(v.Yaw); w.WriteSingle(v.Pitch);
                    w.WritePackedUInt32(v.Shots);
                    w.WritePackedUInt32(v.Rounds);
                    w.WritePackedUInt32(v.TargetId);
                    w.WritePackedUInt32(v.Range);
                }),
                (Func<NetworkReader, SentinelState>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new SentinelState { Protocol = protocol };
                    return new SentinelState
                    {
                        Protocol = protocol, MissileId = r.ReadPackedUInt32(), Phase = r.ReadByte(), HoverLeft = r.ReadByte(),
                        Yaw = r.ReadSingle(), Pitch = r.ReadSingle(), Shots = r.ReadPackedUInt32(), Rounds = r.ReadPackedUInt32(),
                        TargetId = r.ReadPackedUInt32(), Range = r.ReadPackedUInt32(),
                    };
                }));
        }

        public void ResetForScene() { }

        private void Update()
        {
            NetFeed.Poll(ClientHandlers, ref nextRegistration);
        }

        private void Receive(INetworkPlayer sender, SentinelState state)
        {
            if (state.Protocol != ProtocolVersion || GameAccess.IsServer() || state.Phase > (byte)SentinelPhase.Drop ||
                state.Rounds > SentinelProfile.Rounds || !float.IsFinite(state.Yaw) || !float.IsFinite(state.Pitch)) return;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = state.MissileId }, out Unit unit) ||
                !(unit is Missile missile) || missile.disabled || missile.definition == null) return;
            SentinelDrone.Attach(missile)?.Receive(state);
        }

        private void OnDestroy() => ClientHandlers.Release();
    }
}
