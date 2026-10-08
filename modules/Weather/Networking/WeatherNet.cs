using System;
using BoscaliSummer.Modules.Weather.Runtime;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Net;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Networking
{
    /// <summary>
    /// Mirage network bridge for weather synchronization between host and remote clients.
    /// Periodic compact model key and native targets, plus immediate setting changes.
    /// </summary>
    internal sealed class WeatherNet : MonoBehaviour
    {
        public const byte ProtocolVersion = 7;

        private WeatherManager manager;
        private HandlerSlot clientSlot;
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<WeatherSyncMessage>(ReceiveWeatherSync);
        private float nextRegistration;
        private int knownPeers;
        private float resyncAt;
        private int resyncsLeft;
        private static bool serializersInstalled;
        private static readonly MirageSerializers Seams = new MirageSerializers(
            "[Weather]", " could not be resolved; network sync will fail.");

        public void Configure(WeatherManager owner)
        {
            manager = owner;
            InstallSerializers();
        }

        public void ResetScene()
        {
            ClientHandlers.Release();
            nextRegistration = 0f;
            knownPeers = 0;
            resyncsLeft = 0;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 1.0f;

            NetworkManagerNuclearOption network = GameAccess.NetworkManagerOrNull;
            MessageHandler server = network?.Server?.Active == true ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.MessageHandler;

            WatchPeers(server != null ? network.Server : null);
            ClientHandlers.Swap(client);
        }

        // A joining peer gets the key within seconds instead of on the next 8 s refresh.
        // Two resends cover a peer whose handler registers after the first one lands.
        private void WatchPeers(NetworkServer server)
        {
            int peers = server?.AuthenticatedPlayers?.Count ?? 0;
            if (peers > knownPeers)
            {
                resyncAt = Time.unscaledTime + 1f;
                resyncsLeft = 2;
            }
            knownPeers = peers;
            if (resyncsLeft > 0 && Time.unscaledTime >= resyncAt)
            {
                resyncsLeft--;
                resyncAt = Time.unscaledTime + 3f;
                manager?.ResendSync();
            }
        }

        public void Broadcast(WeatherSyncMessage message)
        {
            if (!GameAccess.IsServer()) return;
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
            if (server == null || !server.Active) return;

            message.Protocol = ProtocolVersion;
            server.SendToAll(message, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void ReceiveWeatherSync(INetworkPlayer sender, WeatherSyncMessage message)
        {
            if (message.Protocol != ProtocolVersion) return;
            manager?.ApplyNetworkSync(message);
        }

        private static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;

            Seams.Install<WeatherSyncMessage>((Action<NetworkWriter, WeatherSyncMessage>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteSingle(v.TargetConditions);
                w.WriteSingle(v.TargetCloudHeight);
                w.WriteSingle(v.TargetWindX);
                w.WriteSingle(v.TargetWindZ);
                w.WriteSingle(v.TargetTurbulence);
                w.WriteSingle(v.TransitionProgress);
                w.WritePackedUInt32(v.MissionTimeSeconds);
                w.WriteSingle(v.ForcedRain);
                w.WritePackedUInt32(v.FieldSeed);
                w.WriteSingle(v.FieldEpoch);
                w.WriteByte(v.FieldStartRegime);
                w.WriteByte((byte)(v.FieldDynamic ? 1 : 0));
                w.WriteByte((byte)(v.FieldManual ? 1 : 0));
                w.WriteSingle(v.HoldMinutes);
                w.WriteSingle(v.BlendMinutes);
                w.WriteByte(v.FieldSets);
                w.WriteByte(v.FieldSalt);
                w.WriteByte((byte)(v.FieldHasAnchor ? 1 : 0));
                w.WriteSingle(v.FieldAnchorX);
                w.WriteSingle(v.FieldAnchorZ);
                w.WriteByte(v.FieldFrontTurn);
            }),
            (Func<NetworkReader, WeatherSyncMessage>)(r =>
            {
                byte protocol = r.ReadByte();
                var message = new WeatherSyncMessage { Protocol = protocol };
                if (protocol != ProtocolVersion) return message;

                message.TargetConditions = r.ReadSingle();
                message.TargetCloudHeight = r.ReadSingle();
                message.TargetWindX = r.ReadSingle();
                message.TargetWindZ = r.ReadSingle();
                message.TargetTurbulence = r.ReadSingle();
                message.TransitionProgress = r.ReadSingle();
                message.MissionTimeSeconds = r.ReadPackedUInt32();
                message.ForcedRain = r.ReadSingle();
                message.FieldSeed = r.ReadPackedUInt32();
                message.FieldEpoch = r.ReadSingle();
                message.FieldStartRegime = r.ReadByte();
                message.FieldDynamic = r.ReadByte() != 0;
                message.FieldManual = r.ReadByte() != 0;
                message.HoldMinutes = r.ReadSingle();
                message.BlendMinutes = r.ReadSingle();
                message.FieldSets = r.ReadByte();
                message.FieldSalt = r.ReadByte();
                message.FieldHasAnchor = r.ReadByte() != 0;
                message.FieldAnchorX = r.ReadSingle();
                message.FieldAnchorZ = r.ReadSingle();
                message.FieldFrontTurn = r.ReadByte();
                return message;
            }));

        }

    }

}
