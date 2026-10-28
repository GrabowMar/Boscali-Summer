using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Vanguard.Presentation;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    [NetworkMessage]
    internal struct MaldxEvent
    {
        public byte Protocol, Kind;
        public uint Bait, Launcher, Site;
        public int Count;
    }

    /// <summary>Server-to-client MALD-X HUD events (presentation only; exposure itself rides the game's tracking RPC).</summary>
    internal sealed class MaldxNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 1;
        internal const byte Airborne = 0, Exposed = 1, Down = 2, Chaffed = 3; // Chaffed: Site = the aircraft the missile was after
        private HandlerSlot slot;
        private HandlerSlot ClientHandlers => slot ??= HandlerSlot.Of<MaldxEvent>(Receive);
        private float nextRegistration;

        private void Awake()
        {
            MirageSerializers.Strict.Install<MaldxEvent>(
                (Action<NetworkWriter, MaldxEvent>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WriteByte(v.Kind);
                    w.WritePackedUInt32(v.Bait);
                    w.WritePackedUInt32(v.Launcher);
                    w.WritePackedUInt32(v.Site);
                    w.WritePackedInt32(v.Count);
                }),
                (Func<NetworkReader, MaldxEvent>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new MaldxEvent { Protocol = protocol };
                    return new MaldxEvent
                    {
                        Protocol = protocol, Kind = r.ReadByte(), Bait = r.ReadPackedUInt32(),
                        Launcher = r.ReadPackedUInt32(), Site = r.ReadPackedUInt32(), Count = r.ReadPackedInt32(),
                    };
                }));
        }

        public void ResetForScene() { }

        private void Update()
        {
            NetFeed.Poll(ClientHandlers, ref nextRegistration);
        }

        /// <summary>Server: show on the host's own HUD and send to every client.</summary>
        internal static void Send(byte kind, Missile bait, Unit launcher, uint site, int count)
        {
            var e = new MaldxEvent
            {
                Protocol = ProtocolVersion, Kind = kind, Site = site, Count = count,
                Bait = bait != null ? bait.persistentID.Id : 0u,
                Launcher = launcher != null ? launcher.persistentID.Id : 0u,
            };
            MaldxHud.Show(e);
            NetFeed.Broadcast(e);
        }

        private void Receive(INetworkPlayer sender, MaldxEvent e)
        {
            if (e.Protocol != ProtocolVersion || GameAccess.IsServer() || e.Kind > Chaffed) return;
            MaldxHud.Show(e);
        }

        private void OnDestroy() => ClientHandlers.Release();
    }
}
