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
        public float Inflation;
        public Vector3 Aim;
    }

    /// <summary>Server-to-client presentation only. No client can deploy, steer or fire a gun pod.</summary>
    internal sealed class GlaiveNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 1;
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
                    w.WriteSingle(v.Inflation);
                    w.WriteSingle(v.Aim.x); w.WriteSingle(v.Aim.y); w.WriteSingle(v.Aim.z);
                    w.WritePackedUInt32(v.Shots);
                }),
                (Func<NetworkReader,GlaiveState>)(r =>
                {
                    byte protocol=r.ReadByte();
                    if (protocol != ProtocolVersion) return new GlaiveState { Protocol=protocol };
                    return new GlaiveState
                    {
                        Protocol=protocol, MissileId=r.ReadPackedUInt32(), Inflation=r.ReadSingle(),
                        Aim=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle()), Shots=r.ReadPackedUInt32(),
                    };
                }));
        }

        public void ResetForScene() => GlaiveTurret.Active.Clear();

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
            GlaiveTurret.Attach(missile)?.Receive(state);
        }

        private void OnDestroy() => ClientHandlers.Release();
    }
}
