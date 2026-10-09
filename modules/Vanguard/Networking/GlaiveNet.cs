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
    internal struct GlaiveState
    {
        public byte Protocol;
        public uint MissileId, Shots;
        public byte Phase;
        public float Yaw,Pitch,Slide;
    }

    /// <summary>GLAIVE-2S server-to-client presentation only. No client can deploy, steer or fire the drone.</summary>
    internal sealed class GlaiveNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 3; // 3: GLAIVE-2S SPECTRE adds the HAND-OFF slide
        private HandlerSlot slot;
        private HandlerSlot ClientHandlers => slot ??= HandlerSlot.Of<GlaiveState>(Receive);
        private float nextRegistration;

        private void Awake()
        {
            MirageSerializers.Strict.Install<GlaiveState>(
                (Action<NetworkWriter,GlaiveState>)((w,v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WritePackedUInt32(v.MissileId);
                    w.WriteByte(v.Phase);
                    w.WriteSingle(v.Yaw); w.WriteSingle(v.Pitch);
                    w.WritePackedUInt32(v.Shots);
                    w.WriteSingle(v.Slide);
                }),
                (Func<NetworkReader,GlaiveState>)(r =>
                {
                    byte protocol=r.ReadByte();
                    if (protocol != ProtocolVersion) return new GlaiveState { Protocol=protocol };
                    return new GlaiveState
                    {
                        Protocol=protocol, MissileId=r.ReadPackedUInt32(), Phase=r.ReadByte(),
                        Yaw=r.ReadSingle(),Pitch=r.ReadSingle(),Shots=r.ReadPackedUInt32(),Slide=r.ReadSingle(),
                    };
                }));
        }

        public void ResetForScene() => GlaiveDrone.ResetScene();

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration=Time.unscaledTime+.5f;
            ClientHandlers.Swap(NetworkManagerNuclearOption.i?.Client?.MessageHandler);
        }

        internal static void Broadcast(GlaiveState state)
        {
            NetworkServer server=NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(state, authenticatedOnly:true, excludeLocalPlayer:true);
        }

        private void Receive(INetworkPlayer sender, GlaiveState state)
        {
            if (state.Protocol != ProtocolVersion || GameAccess.IsServer() || state.Shots > Domain.GlaiveProfile.Rounds) return;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id=state.MissileId },out Unit unit) ||
                !(unit is Missile missile) || missile.disabled || missile.definition == null) return;
            GlaiveDrone.Attach(missile)?.Receive(state);
        }

        private void OnDestroy() => ClientHandlers.Release();
    }
}
