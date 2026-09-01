using System;
using System.Collections;
using System.Collections.Generic;
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
    internal struct BuildingDamagedMessage
    {
        public float X;
        public float Y;
        public float Z;
        public float Severity;
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
        private sealed class PendingBuilding
        {
            public GlobalPosition Position;
            public float Severity;
            public float Expires;
        }

        private sealed class DamagedBuilding
        {
            public GlobalPosition Position;
            public float Severity;
        }

        public static ModNet Instance { get; private set; }

        private const int MaximumDamagedBuildings = 256;
        private const int MaximumPendingBuildings = 64;
        private static bool serializersInstalled;
        private static readonly List<DamagedBuilding> damagedBuildings = new List<DamagedBuilding>();
        private readonly List<PendingBuilding> pendingBuildings = new List<PendingBuilding>();
        private MessageHandler registeredClientHandler;
        private NetworkServer subscribedServer;
        private float nextRegistrationCheck;
        private float nextDamageRetry;

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
                registeredClientHandler.UnregisterHandler<BuildingDamagedMessage>();
                registeredClientHandler.UnregisterHandler<RuinCreatedMessage>();
            }
            if (Instance == this) Instance = null;
        }

        public void ResetForScene()
        {
            damagedBuildings.Clear();
            pendingBuildings.Clear();
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextRegistrationCheck)
            {
                nextRegistrationCheck = Time.unscaledTime + 0.5f;
                RegisterLiveEndpoints();
            }
            if (Time.unscaledTime >= nextDamageRetry)
            {
                nextDamageRetry = Time.unscaledTime + 1f;
                RetryPendingBuildings();
            }
        }

        internal static void BroadcastFire(
            GlobalPosition position, float remainingLifetime, bool forest, float clusterScale)
        {
            if (!IsServer()) return;
            NetworkManagerNuclearOption.i.Server.SendToAll(
                ToFireMessage(position, remainingLifetime, forest, clusterScale),
                authenticatedOnly: true,
                excludeLocalPlayer: true);
        }

        internal static void SendFire(INetworkPlayer player, GlobalPosition position,
            float remainingLifetime, bool forest, float clusterScale)
        {
            if (!IsServer() || player == null || remainingLifetime <= 0f) return;
            player.Send(ToFireMessage(position, remainingLifetime, forest, clusterScale));
        }

        internal static void BroadcastBuildingDamage(GlobalPosition position, float severity = 0.62f)
        {
            if (!IsServer()) return;
            severity = BuildingDamageVisual.QuantizeSeverity(severity);
            for (int i = 0; i < damagedBuildings.Count; i++)
            {
                DamagedBuilding existing = damagedBuildings[i];
                if ((existing.Position - position).sqrMagnitude >= 4f) continue;
                if (severity <= existing.Severity + 0.08f) return;
                existing.Severity = severity;
                NetworkManagerNuclearOption.i.Server.SendToAll(
                    ToBuildingMessage(position, severity),
                    authenticatedOnly: true,
                    excludeLocalPlayer: true);
                return;
            }
            if (damagedBuildings.Count >= MaximumDamagedBuildings)
                damagedBuildings.RemoveAt(0);
            damagedBuildings.Add(new DamagedBuilding { Position = position, Severity = severity });
            NetworkManagerNuclearOption.i.Server.SendToAll(
                ToBuildingMessage(position, severity),
                authenticatedOnly: true,
                excludeLocalPlayer: true);
        }

        internal static void BroadcastRuin(GlobalPosition position, Vector2 halfExtents)
        {
            if (!IsServer()) return;
            NetworkManagerNuclearOption.i.Server.SendToAll(
                ToRuinMessage(position, halfExtents, 0f),
                authenticatedOnly: true,
                excludeLocalPlayer: true);
        }

        internal static void ForgetBuildingDamage(GlobalPosition position)
        {
            for (int i = damagedBuildings.Count - 1; i >= 0; i--)
                if ((damagedBuildings[i].Position - position).sqrMagnitude < 4f)
                    damagedBuildings.RemoveAt(i);
            ModNet instance = Instance;
            if (instance == null) return;
            for (int i = instance.pendingBuildings.Count - 1; i >= 0; i--)
                if ((instance.pendingBuildings[i].Position - position).sqrMagnitude < 4f)
                    instance.pendingBuildings.RemoveAt(i);
        }

        internal static void SendRuin(
            INetworkPlayer player, GlobalPosition position, Vector2 halfExtents, float ageSeconds)
        {
            if (!IsServer() || player == null) return;
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
                handler.RegisterHandler<BuildingDamagedMessage>(ReceiveBuildingDamage, false);
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
            if (!IsServer() || player == null || !player.IsAuthenticated) return;
            ImpactFireManager.Instance?.SendSnapshot(player);
            RuinAftermathManager.Instance?.SendSnapshot(player);
            for (int i = 0; i < damagedBuildings.Count; i++)
                player.Send(ToBuildingMessage(
                    damagedBuildings[i].Position, damagedBuildings[i].Severity));
        }

        private void ReceiveFire(INetworkPlayer player, FireIgnitedMessage message)
        {
            ImpactFireManager.Instance?.ReceiveIgnition(
                new GlobalPosition(message.X, message.Y, message.Z),
                message.RemainingLifetime, message.Forest, message.ClusterScale);
        }

        private void ReceiveBuildingDamage(INetworkPlayer player, BuildingDamagedMessage message)
        {
            var position = new GlobalPosition(message.X, message.Y, message.Z);
            float severity = Mathf.Clamp01(message.Severity);
            if (BuildingDamageVisual.ApplyNearest(position, severity)) return;
            for (int i = 0; i < pendingBuildings.Count; i++)
            {
                PendingBuilding existing = pendingBuildings[i];
                if ((existing.Position - position).sqrMagnitude >= 4f) continue;
                if (severity > existing.Severity) existing.Severity = severity;
                existing.Expires = Time.unscaledTime + 20f;
                return;
            }
            if (pendingBuildings.Count >= MaximumPendingBuildings)
                pendingBuildings.RemoveAt(0);
            pendingBuildings.Add(new PendingBuilding
            {
                Position = position,
                Severity = severity,
                Expires = Time.unscaledTime + 20f
            });
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

        private void RetryPendingBuildings()
        {
            for (int i = pendingBuildings.Count - 1; i >= 0; i--)
            {
                PendingBuilding item = pendingBuildings[i];
                if (Time.unscaledTime >= item.Expires ||
                    BuildingDamageVisual.ApplyNearest(item.Position, item.Severity))
                    pendingBuildings.RemoveAt(i);
            }
        }

        private static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;
            SetWriter<FireIgnitedMessage>(WriteFire);
            SetReader<FireIgnitedMessage>(ReadFire);
            SetWriter<BuildingDamagedMessage>(WriteBuilding);
            SetReader<BuildingDamagedMessage>(ReadBuilding);
            SetWriter<RuinCreatedMessage>(WriteRuin);
            SetReader<RuinCreatedMessage>(ReadRuin);
            MessagePacker.RegisterMessage<FireIgnitedMessage>();
            MessagePacker.RegisterMessage<BuildingDamagedMessage>();
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

        private static void WriteBuilding(NetworkWriter writer, BuildingDamagedMessage message)
        {
            writer.WriteSingle(message.X); writer.WriteSingle(message.Y); writer.WriteSingle(message.Z);
            writer.WriteSingle(message.Severity);
        }

        private static BuildingDamagedMessage ReadBuilding(NetworkReader reader) => new BuildingDamagedMessage
        {
            X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle(),
            Severity = reader.ReadSingle()
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

        private static BuildingDamagedMessage ToBuildingMessage(
            GlobalPosition position, float severity) => new BuildingDamagedMessage
        {
            X = position.x, Y = position.y, Z = position.z, Severity = severity
        };

        private static RuinCreatedMessage ToRuinMessage(
            GlobalPosition position, Vector2 halfExtents, float ageSeconds) => new RuinCreatedMessage
        {
            X = position.x, Y = position.y, Z = position.z,
            HalfX = halfExtents.x, HalfZ = halfExtents.y,
            AgeSeconds = ageSeconds
        };

        private static bool IsServer()
        {
            try { return NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active; }
            catch { return false; }
        }
    }
}
