using System;
using System.Reflection;
using BoscaliSummer.Modules.Weather.Runtime;
using BoscaliSummer.Core.Game;
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
        private MessageHandler serverHandler;
        private MessageHandler clientHandler;
        private float nextRegistration;
        private int knownPeers;
        private float resyncAt;
        private int resyncsLeft;
        private static bool serializersInstalled;

        public void Configure(WeatherManager owner)
        {
            manager = owner;
            InstallSerializers();
        }

        public void ResetScene()
        {
            clientHandler?.UnregisterHandler<WeatherSyncMessage>();
            serverHandler = null;
            clientHandler = null;
            nextRegistration = 0f;
            knownPeers = 0;
            resyncsLeft = 0;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 1.0f;

            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            MessageHandler server = network?.Server?.Active == true ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.MessageHandler;

            if (server != serverHandler)
            {
                serverHandler = server;
            }
            WatchPeers(server != null ? network.Server : null);

            if (client != clientHandler)
            {
                clientHandler?.UnregisterHandler<WeatherSyncMessage>();
                clientHandler = client;
                clientHandler?.RegisterHandler<WeatherSyncMessage>(ReceiveWeatherSync, false);
            }
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
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
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

            Bind(typeof(Writer<WeatherSyncMessage>), "Write", (Action<NetworkWriter, WeatherSyncMessage>)((w, v) =>
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
            }));

            Bind(typeof(Reader<WeatherSyncMessage>), "Read", (Func<NetworkReader, WeatherSyncMessage>)(r =>
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

            MessagePacker.RegisterMessage<WeatherSyncMessage>();
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(
                property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                Plugin.Logger.LogError(
                    "[Weather] Mirage serializer seam " + holder.Name + "." + property +
                    " could not be resolved; network sync will fail.");
                return;
            }
            target.SetValue(null, value);
        }

    }

}
