using System;
using BoscaliSummer.Modules.Events.Runtime;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Net;
using Mirage;
using Mirage.Serialization;
using UnityEngine;

namespace BoscaliSummer.Modules.Events.Networking
{
    /// <summary>
    /// Host-to-client event state. It carries only the catalog index and mission timestamps
    /// (title and flavor text are looked up locally, like the chain of command never ships
    /// generated text). Index -1 = calm.
    /// </summary>
    [NetworkMessage]
    internal struct ActiveEventChanged
    {
        public byte Protocol;
        public sbyte CatalogIndex;

        /// <summary>Faction the event is aimed at as a name hash; 0 = all theater.</summary>
        public int TargetFactionHash;
        public float StartedAtMissionTime;
        public float EndsAtMissionTime;
        public float EffectStrength;
    }

    internal sealed class EventsNet : MonoBehaviour
    {
        /// <summary>Version 6 is state only: the response intent, reply and faction mirror are gone.</summary>
        internal const byte ProtocolVersion = 6;

        private EventsManager manager;
        private HandlerSlot clientSlot;
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<ActiveEventChanged>(ReceiveChanged);
        private NetworkClient client;
        private float nextRegistration;

        public void Configure(EventsManager owner)
        {
            manager = owner;
            InstallSerializers();
            RefreshClient();
        }

        /// <summary>The event mirror resets with the scene; an unchanged transport keeps its handler.</summary>
        public void ResetScene()
        {
            RefreshClient();
            nextRegistration = 0f;
        }

        private void OnEnable() { if (manager != null) RefreshClient(); }
        private void OnDisable() => ReleaseClient();

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            RefreshClient();
        }

        private void RefreshClient()
        {
            NetworkClient next = this != null && isActiveAndEnabled ? GameAccess.NetworkManagerOrNull?.Client : null;
            if (!ReferenceEquals(next, client))
            {
                client?.Started.RemoveListener(BindClient);
                ClientHandlers.Release();
                client = next;
                // Started runs after Mirage creates MessageHandler, including on a reconnect of this client.
                // Its late-listener replay also covers a module enabled on an existing connection.
                client?.Started.AddListener(BindClient);
            }
            BindClient();
        }

        private void BindClient() =>
            ClientHandlers.Swap(this != null && isActiveAndEnabled ? client?.MessageHandler : null);

        private void ReleaseClient()
        {
            client?.Started.RemoveListener(BindClient);
            client = null;
            ClientHandlers.Release();
        }

        public void Broadcast(sbyte catalogIndex, int targetFactionHash, float start, float end,
            float effectStrength)
        {
            if (!GameAccess.IsServer()) return;
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(new ActiveEventChanged
            {
                Protocol = ProtocolVersion,
                CatalogIndex = catalogIndex,
                TargetFactionHash = targetFactionHash,
                StartedAtMissionTime = start,
                EndsAtMissionTime = end,
                EffectStrength = effectStrength,
            }, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void ReceiveChanged(INetworkPlayer _, ActiveEventChanged message)
        {
            if (GameAccess.IsServer() || message.Protocol != ProtocolVersion) return;
            manager.ApplyRemote(message.CatalogIndex, message.TargetFactionHash,
                message.StartedAtMissionTime, message.EndsAtMissionTime, message.EffectStrength);
        }

        private void OnDestroy() => ReleaseClient();

        private static readonly MirageSerializers Seams = new MirageSerializers(
            "[Events]", " is missing; world events cannot replicate on this game build.");

        private static void InstallSerializers()
        {
            Seams.Install<ActiveEventChanged>((Action<NetworkWriter, ActiveEventChanged>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteByte((byte)v.CatalogIndex);
                w.WritePackedInt32(v.TargetFactionHash);
                w.WriteSingle(v.StartedAtMissionTime);
                w.WriteSingle(v.EndsAtMissionTime);
                w.WriteSingle(v.EffectStrength);
            }),
            (Func<NetworkReader, ActiveEventChanged>)(r =>
            {
                byte protocol = r.ReadByte();
                var message = new ActiveEventChanged { Protocol = protocol };
                if (protocol != ProtocolVersion) return message;
                message.CatalogIndex = unchecked((sbyte)r.ReadByte());
                message.TargetFactionHash = r.ReadPackedInt32();
                message.StartedAtMissionTime = r.ReadSingle();
                message.EndsAtMissionTime = r.ReadSingle();
                message.EffectStrength = r.ReadSingle();
                return message;
            }));
        }
    }
}
