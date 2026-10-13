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
        private float nextRegistration;

        public void Configure(EventsManager owner)
        {
            manager = owner;
            InstallSerializers();
        }

        /// <summary>Nothing is scoped to a scene, but a reset still drops a stale transport.</summary>
        public void ResetScene()
        {
            ClientHandlers.Release();
            nextRegistration = 0f;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            ClientHandlers.Swap(GameAccess.NetworkManagerOrNull?.Client?.MessageHandler);
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

        private void OnDestroy() => ClientHandlers.Release();

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
