using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Spooktober.Kaiju.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Kaiju.Networking
{
    [NetworkMessage]
    internal struct KaijuState
    {
        public byte Protocol, Gen, Action; // Action 255: the kaiju is gone
        public float X, Y, Z, Yaw, Elapsed, AimX, AimY, AimZ;
        public bool HasAim;
    }

    [NetworkMessage]
    internal struct KaijuSpikes
    {
        public byte Protocol;
        public uint[] Ids;
    }

    /// <summary>Host-to-client presentation of the kaiju: its pose stream and which missiles are spikes. Clients never
    /// act on it beyond drawing; the hardpoints and all damage replicate through the game's own units.</summary>
    internal sealed class KaijuNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 1;
        public const byte Gone = 255;
        private HandlerSlot stateSlot, spikeSlot;
        private HandlerSlot StateHandlers => stateSlot ??= HandlerSlot.Of<KaijuState>(OnState);
        private HandlerSlot SpikeHandlers => spikeSlot ??= HandlerSlot.Of<KaijuSpikes>(OnSpikes);
        private float nextRegistration;

        private void Awake()
        {
            MirageSerializers.Strict.Install<KaijuState>(
                (Action<NetworkWriter, KaijuState>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WriteByte(v.Gen);
                    w.WriteByte(v.Action);
                    w.WriteSingle(v.X); w.WriteSingle(v.Y); w.WriteSingle(v.Z);
                    w.WriteSingle(v.Yaw); w.WriteSingle(v.Elapsed);
                    w.WriteSingle(v.AimX); w.WriteSingle(v.AimY); w.WriteSingle(v.AimZ);
                    w.WriteBoolean(v.HasAim);
                }),
                (Func<NetworkReader, KaijuState>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new KaijuState { Protocol = protocol };
                    return new KaijuState
                    {
                        Protocol = protocol, Gen = r.ReadByte(), Action = r.ReadByte(),
                        X = r.ReadSingle(), Y = r.ReadSingle(), Z = r.ReadSingle(), Yaw = r.ReadSingle(), Elapsed = r.ReadSingle(),
                        AimX = r.ReadSingle(), AimY = r.ReadSingle(), AimZ = r.ReadSingle(), HasAim = r.ReadBoolean(),
                    };
                }));
            MirageSerializers.Strict.Install<KaijuSpikes>(
                (Action<NetworkWriter, KaijuSpikes>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    int count = v.Ids != null ? Math.Min(v.Ids.Length, 64) : 0;
                    w.WriteByte((byte)count);
                    for (int i = 0; i < count; i++) w.WritePackedUInt32(v.Ids[i]);
                }),
                (Func<NetworkReader, KaijuSpikes>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new KaijuSpikes { Protocol = protocol };
                    var ids = new uint[r.ReadByte()];
                    for (int i = 0; i < ids.Length; i++) ids[i] = r.ReadPackedUInt32();
                    return new KaijuSpikes { Protocol = protocol, Ids = ids };
                }));
        }

        public void ResetForScene() { }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            var handler = NetworkManagerNuclearOption.i?.Client?.MessageHandler;
            StateHandlers.Swap(handler);
            SpikeHandlers.Swap(handler);
        }

        internal static void Send(KaijuState state)
        {
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            state.Protocol = ProtocolVersion;
            server.SendToAll(state, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        internal static void SendSpikes(uint[] ids)
        {
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active || ids.Length == 0) return;
            server.SendToAll(new KaijuSpikes { Protocol = ProtocolVersion, Ids = ids }, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void OnState(INetworkPlayer sender, KaijuState state)
        {
            if (state.Protocol == ProtocolVersion && !GameAccess.IsServer()) KaijuDirector.Instance?.Receive(state);
        }

        private void OnSpikes(INetworkPlayer sender, KaijuSpikes spikes)
        {
            if (spikes.Protocol != ProtocolVersion || GameAccess.IsServer() || spikes.Ids == null) return;
            foreach (uint id in spikes.Ids)
                if (UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit unit) && unit is Missile missile) KaijuBody.Restyle(missile);
        }

        private void OnDestroy()
        {
            StateHandlers.Release();
            SpikeHandlers.Release();
        }
    }
}
