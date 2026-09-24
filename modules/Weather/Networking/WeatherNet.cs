using System;
using System.Reflection;
using BoscaliSummer.Features.Weather.Runtime;
using BoscaliSummer.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Networking
{
    /// <summary>
    /// Mirage network bridge for weather synchronization between host and remote clients.
    /// Rate-limited to at most once every 5 seconds or upon explicit regime change.
    /// </summary>
    internal sealed class WeatherNet : MonoBehaviour
    {
        public const byte ProtocolVersion = 2;

        private WeatherManager manager;
        private MessageHandler serverHandler;
        private MessageHandler clientHandler;
        private float nextRegistration;
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

            if (client != clientHandler)
            {
                clientHandler?.UnregisterHandler<WeatherSyncMessage>();
                clientHandler = client;
                clientHandler?.RegisterHandler<WeatherSyncMessage>(ReceiveWeatherSync, false);
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
