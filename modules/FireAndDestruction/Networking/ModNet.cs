using System;
using System.Collections;
using System.Reflection;
using BoscaliSummer.Fire;
using BoscaliSummer.Framework.Lifecycle;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Runtime
{
    [NetworkMessage]
    internal struct FireIgnitedMessage
    {
        public float X;
        public float Y;
        public float Z;
        public float RemainingLifetime;
        public float ClusterScale;
        public bool Forest;
    }

    [NetworkMessage]
    internal struct RuinCreatedMessage
    {
        public float X;
        public float Y;
        public float Z;
        public float HalfX;
        public float HalfZ;
        public float AgeSeconds;
    }

    /// <summary>
    /// A deliberately tiny event bridge. It sends only authoritative transitions;
    /// particles, lights and smoke evolution stay local and never generate frame traffic.
    /// Serializers are installed explicitly because a runtime BepInEx assembly is not
    /// processed by Mirage's compile-time weaver.
    /// </summary>
    internal sealed class ModNet : MonoBehaviour, ISceneService
    {
        public static ModNet Instance { get; private set; }

        private static bool serializersInstalled;
        private MessageHandler registeredClientHandler;
        private NetworkServer subscribedServer;
        private float nextRegistrationCheck;

        private void Awake()
        {
            Instance = this;
            InstallSerializers();
        }

        private void OnDestroy()
        {
            if (subscribedServer != null) subscribedServer.Authenticated.RemoveListener(OnServerAuthenticated);
            if (registeredClientHandler != null)
            {
                registeredClientHandler.UnregisterHandler<FireIgnitedMessage>();
                registeredClientHandler.UnregisterHandler<RuinCreatedMessage>();
            }
            if (Instance == this) Instance = null;
        }

        public void ResetForScene()
        {
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextRegistrationCheck)
            {
                nextRegistrationCheck = Time.unscaledTime + 0.5f;
                RegisterLiveEndpoints();
            }
        }

        internal static void BroadcastFire(
            GlobalPosition position, float remainingLifetime, bool forest, float clusterScale)
        {
            if (!GameAccess.IsServer()) return;
            NetworkManagerNuclearOption.i.Server.SendToAll(
                ToFireMessage(position, remainingLifetime, forest, clusterScale),
                authenticatedOnly: true,
                excludeLocalPlayer: true);
        }

        internal static void SendFire(INetworkPlayer player, GlobalPosition position,
            float remainingLifetime, bool forest, float clusterScale)
        {
            if (!GameAccess.IsServer() || player == null || remainingLifetime <= 0f) return;
            player.Send(ToFireMessage(position, remainingLifetime, forest, clusterScale));
        }

        internal static void BroadcastRuin(GlobalPosition position, Vector2 halfExtents)
        {
            if (!GameAccess.IsServer()) return;
            NetworkManagerNuclearOption.i.Server.SendToAll(
                ToRuinMessage(position, halfExtents, 0f),
                authenticatedOnly: true,
                excludeLocalPlayer: true);
        }

        internal static void SendRuin(
            INetworkPlayer player, GlobalPosition position, Vector2 halfExtents, float ageSeconds)
        {
            if (!GameAccess.IsServer() || player == null) return;
            player.Send(ToRuinMessage(position, halfExtents, ageSeconds));
        }

        private void RegisterLiveEndpoints()
        {
            NetworkManagerNuclearOption manager;
            try { manager = NetworkManagerNuclearOption.i; }
            catch { return; }
            if (manager == null) return;

            if (manager.Server != null && manager.Server.Active && subscribedServer != manager.Server)
            {
                if (subscribedServer != null) subscribedServer.Authenticated.RemoveListener(OnServerAuthenticated);
                subscribedServer = manager.Server;
                subscribedServer.Authenticated.AddListener(OnServerAuthenticated);
            }

            MessageHandler handler = manager.Client?.MessageHandler;
            if (handler != null && handler != registeredClientHandler)
            {
                handler.RegisterHandler<FireIgnitedMessage>(ReceiveFire, false);
                handler.RegisterHandler<RuinCreatedMessage>(ReceiveRuin, false);
                registeredClientHandler = handler;
                Plugin.Logger.LogInfo("Registered Boscali Summer multiplayer event handlers.");
            }
        }

        private void OnServerAuthenticated(INetworkPlayer player)
        {
            StartCoroutine(SendLateJoinSnapshot(player));
        }

        private IEnumerator SendLateJoinSnapshot(INetworkPlayer player)
        {
            // Authentication normally precedes the client's mission scene. Two bounded
            // resends make the snapshot survive that transition without periodic traffic.
            yield return new WaitForSecondsRealtime(3f);
            SendSnapshot(player);
            yield return new WaitForSecondsRealtime(6f);
            SendSnapshot(player);
        }

        private static void SendSnapshot(INetworkPlayer player)
        {
            if (!GameAccess.IsServer() || player == null || !player.IsAuthenticated) return;
            ImpactFireManager.Instance?.SendSnapshot(player);
            RuinAftermathManager.Instance?.SendSnapshot(player);
        }

        private void ReceiveFire(INetworkPlayer player, FireIgnitedMessage message)
        {
            ImpactFireManager.Instance?.ReceiveIgnition(
                new GlobalPosition(message.X, message.Y, message.Z),
                message.RemainingLifetime, message.Forest, message.ClusterScale);
        }

        private void ReceiveRuin(INetworkPlayer player, RuinCreatedMessage message)
        {
            RuinAftermathManager.Instance?.RegisterRuin(
                new GlobalPosition(message.X, message.Y, message.Z),
                new Vector2(message.HalfX, message.HalfZ),
                message.AgeSeconds,
                false,
                message.AgeSeconds < 2f);
        }

        private static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;
            SetWriter<FireIgnitedMessage>(WriteFire);
            SetReader<FireIgnitedMessage>(ReadFire);
            SetWriter<RuinCreatedMessage>(WriteRuin);
            SetReader<RuinCreatedMessage>(ReadRuin);
            MessagePacker.RegisterMessage<FireIgnitedMessage>();
            MessagePacker.RegisterMessage<RuinCreatedMessage>();
        }

        private static void SetWriter<T>(Action<NetworkWriter, T> writer)
        {
            PropertyInfo property = typeof(Writer<T>).GetProperty("Write", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            property?.SetValue(null, writer, null);
        }

        private static void SetReader<T>(Func<NetworkReader, T> reader)
        {
            PropertyInfo property = typeof(Reader<T>).GetProperty("Read", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            property?.SetValue(null, reader, null);
        }

        private static void WriteFire(NetworkWriter writer, FireIgnitedMessage message)
        {
            writer.WriteSingle(message.X); writer.WriteSingle(message.Y); writer.WriteSingle(message.Z);
            writer.WriteSingle(message.RemainingLifetime);
            writer.WriteSingle(message.ClusterScale);
            writer.WriteBooleanExtension(message.Forest);
        }

        private static FireIgnitedMessage ReadFire(NetworkReader reader) => new FireIgnitedMessage
        {
            X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle(),
            RemainingLifetime = reader.ReadSingle(), ClusterScale = reader.ReadSingle(),
            Forest = reader.ReadBooleanExtension()
        };

        private static void WriteRuin(NetworkWriter writer, RuinCreatedMessage message)
        {
            writer.WriteSingle(message.X); writer.WriteSingle(message.Y); writer.WriteSingle(message.Z);
            writer.WriteSingle(message.HalfX); writer.WriteSingle(message.HalfZ);
            writer.WriteSingle(message.AgeSeconds);
        }

        private static RuinCreatedMessage ReadRuin(NetworkReader reader) => new RuinCreatedMessage
        {
            X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle(),
            HalfX = reader.ReadSingle(), HalfZ = reader.ReadSingle(),
            AgeSeconds = reader.ReadSingle()
        };

        private static FireIgnitedMessage ToFireMessage(
            GlobalPosition position, float lifetime, bool forest, float clusterScale) => new FireIgnitedMessage
        {
            X = position.x, Y = position.y, Z = position.z, RemainingLifetime = lifetime,
            Forest = forest, ClusterScale = clusterScale
        };

        private static RuinCreatedMessage ToRuinMessage(
            GlobalPosition position, Vector2 halfExtents, float ageSeconds) => new RuinCreatedMessage
        {
            X = position.x, Y = position.y, Z = position.z,
            HalfX = halfExtents.x, HalfZ = halfExtents.y,
            AgeSeconds = ageSeconds
        };
    }
}
