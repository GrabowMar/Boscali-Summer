using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Vanguard.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    [NetworkMessage]
    internal struct KestrelState
    {
        public byte Protocol;
        public uint MissileId, StationLeft, LasedId; // StationLeft: whole seconds; LasedId 0 = no mark
        public byte Phase, Spotted;
    }

    /// <summary>KS-01 KESTREL server-to-client presentation only. No client can steer the glider, spot or lase.</summary>
    internal sealed class KestrelNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 1;
        private HandlerSlot slot;
        private HandlerSlot ClientHandlers => slot ??= HandlerSlot.Of<KestrelState>(Receive);
        private float nextRegistration;

        private void Awake()
        {
            MirageSerializers.Strict.Install<KestrelState>(
                (Action<NetworkWriter, KestrelState>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WritePackedUInt32(v.MissileId);
                    w.WriteByte(v.Phase);
                    w.WritePackedUInt32(v.StationLeft);
                    w.WriteByte(v.Spotted);
                    w.WritePackedUInt32(v.LasedId);
                }),
                (Func<NetworkReader, KestrelState>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new KestrelState { Protocol = protocol };
                    return new KestrelState
                    {
                        Protocol = protocol, MissileId = r.ReadPackedUInt32(), Phase = r.ReadByte(),
                        StationLeft = r.ReadPackedUInt32(), Spotted = r.ReadByte(), LasedId = r.ReadPackedUInt32(),
                    };
                }));
        }

        public void ResetForScene() { } // the glider's state lives on its missile and goes with it

        private void Update()
        {
            NetFeed.Poll(ClientHandlers, ref nextRegistration);
        }

        private void Receive(INetworkPlayer sender, KestrelState state)
        {
            if (state.Protocol != ProtocolVersion || GameAccess.IsServer()) return;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = state.MissileId }, out Unit unit) ||
                !(unit is Missile missile) || missile.disabled || missile.definition == null) return;
            KestrelDrone.Attach(missile)?.Receive(state);
        }

        private void OnDestroy() => ClientHandlers.Release();
    }
}
