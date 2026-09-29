using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using BoscaliSummer.Features.Trenches.Domain;
using BoscaliSummer.Features.Trenches.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Networking
{
    [NetworkMessage]
    internal struct TrenchGeometryMessage
    {
        public byte Protocol;
        public int LineId;
        public byte Stage;
        public int OwnerHash;
        public Vector3[] Curve;
        public Vector3[] Threat;
        public Vector3[] Support;
        public Vector3[] Redoubt;
        public Vector3[][] Links;
        public Vector3[][] Spurs;
    }

    [NetworkMessage]
    internal struct TrenchStateMessage
    {
        public byte Protocol;
        public int LineId;
        public byte Stage;
        public byte Defenders;
        public bool Suppressed;
        public bool Overrun;
    }

    [NetworkMessage]
    internal struct TrenchLineRemovedMessage
    {
        public byte Protocol;
        public int LineId;
    }

    /// <summary>
    /// Carries committed trench curves to clients, who carve the same ditches locally.
    /// Geometry crosses on commit, growth and late join only; per-tick state (defenders,
    /// suppression, overrun) rides a tiny state message on transitions. Serializers are
    /// installed explicitly because a runtime BepInEx assembly is not processed by
    /// Mirage's compile-time weaver.
    /// </summary>
    internal sealed class TrenchNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = TrenchWire.ProtocolVersion;

        private static bool serializersInstalled;
        private TrenchManager manager;
        private ManualLogSource logger;
        private MessageHandler registeredClientHandler;
        private NetworkServer subscribedServer;
        private float nextRegistrationCheck;

        internal void Configure(TrenchManager owner, ManualLogSource log)
        {
            manager = owner;
            logger = log;
            InstallSerializers();
        }

        public void ResetForScene()
        {
            StopAllCoroutines();
            if (registeredClientHandler != null)
            {
                registeredClientHandler.UnregisterHandler<TrenchGeometryMessage>();
                registeredClientHandler.UnregisterHandler<TrenchStateMessage>();
                registeredClientHandler.UnregisterHandler<TrenchLineRemovedMessage>();
                registeredClientHandler = null;
            }
            nextRegistrationCheck = 0f;
        }

        private void OnDestroy()
        {
            if (subscribedServer != null) subscribedServer.Authenticated.RemoveListener(OnServerAuthenticated);
            if (registeredClientHandler != null)
            {
                registeredClientHandler.UnregisterHandler<TrenchGeometryMessage>();
                registeredClientHandler.UnregisterHandler<TrenchStateMessage>();
                registeredClientHandler.UnregisterHandler<TrenchLineRemovedMessage>();
            }
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextRegistrationCheck)
            {
                nextRegistrationCheck = Time.unscaledTime + 0.5f;
                RegisterLiveEndpoints();
            }
        }

        internal static void BroadcastGeometry(TrenchLine line)
        {
            if (!GameAccess.IsServer() || line == null || line.Curve == null || line.Curve.Length < 2) return;
            NetworkManagerNuclearOption.i.Server.SendToAll(ToGeometry(line),
                authenticatedOnly: true, excludeLocalPlayer: true);
        }

        internal static void SendGeometry(INetworkPlayer player, TrenchLine line)
        {
            if (!GameAccess.IsServer() || player == null || line == null ||
                line.Curve == null || line.Curve.Length < 2) return;
            player.Send(ToGeometry(line));
        }

        internal static void BroadcastState(TrenchLine line)
        {
            if (!GameAccess.IsServer() || line == null) return;
            NetworkManagerNuclearOption.i.Server.SendToAll(ToState(line),
                authenticatedOnly: true, excludeLocalPlayer: true);
        }

        internal static void SendState(INetworkPlayer player, TrenchLine line)
        {
            if (!GameAccess.IsServer() || player == null || line == null) return;
            player.Send(ToState(line));
        }

        internal static void BroadcastRemoved(int lineId)
        {
            if (!GameAccess.IsServer() || lineId <= 0) return;
            NetworkManagerNuclearOption.i.Server.SendToAll(
                new TrenchLineRemovedMessage { Protocol = ProtocolVersion, LineId = lineId },
                authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void RegisterLiveEndpoints()
        {
            NetworkManagerNuclearOption service;
            try { service = NetworkManagerNuclearOption.i; }
            catch { return; }
            if (service == null) return;

            if (service.Server != null && service.Server.Active && subscribedServer != service.Server)
            {
                if (subscribedServer != null) subscribedServer.Authenticated.RemoveListener(OnServerAuthenticated);
                subscribedServer = service.Server;
                subscribedServer.Authenticated.AddListener(OnServerAuthenticated);
            }

            MessageHandler handler = service.Client?.MessageHandler;
            if (handler != registeredClientHandler)
            {
                registeredClientHandler?.UnregisterHandler<TrenchGeometryMessage>();
                registeredClientHandler?.UnregisterHandler<TrenchStateMessage>();
                registeredClientHandler?.UnregisterHandler<TrenchLineRemovedMessage>();
                registeredClientHandler = handler;
                registeredClientHandler?.RegisterHandler<TrenchGeometryMessage>(ReceiveGeometry, false);
                registeredClientHandler?.RegisterHandler<TrenchStateMessage>(ReceiveState, false);
                registeredClientHandler?.RegisterHandler<TrenchLineRemovedMessage>(ReceiveRemoved, false);
                if (registeredClientHandler != null)
                    logger?.LogInfo("[TRENCHES] Registered multiplayer trench handlers.");
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

        private void SendSnapshot(INetworkPlayer player)
        {
            if (!GameAccess.IsServer() || player == null || !player.IsAuthenticated || manager == null) return;
            var lines = manager.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                SendGeometry(player, lines[i]);
                SendState(player, lines[i]);
            }
        }

        private void ReceiveGeometry(INetworkPlayer player, TrenchGeometryMessage message)
        {
            if (GameAccess.IsServer() || !MissionManager.IsRunning || manager == null) return;
            if (message.Protocol != ProtocolVersion || message.LineId <= 0) return;
            if (!TrenchWire.ValidStage(message.Stage)) return;
            int curve = message.Curve != null ? message.Curve.Length : 0;
            int threat = message.Threat != null ? message.Threat.Length : 0;
            int support = message.Support != null ? message.Support.Length : 0;
            int redoubt = message.Redoubt != null ? message.Redoubt.Length : 0;
            int links = message.Links != null ? message.Links.Length : 0;
            int spurs = message.Spurs != null ? message.Spurs.Length : 0;
            if (!TrenchWire.ValidTraceCounts(curve, threat, support, redoubt, links, spurs)) return;
            if (!AllFinite(message.Curve) || !AllFinite(message.Threat) ||
                !AllFinite(message.Support) || !AllFinite(message.Redoubt) ||
                !AllFinite(message.Links) || !AllFinite(message.Spurs)) return;
            manager.ReceiveGeometry(message.LineId, message.OwnerHash, (TrenchStage)message.Stage,
                message.Curve, message.Threat, message.Support, message.Redoubt,
                message.Links, message.Spurs);
        }

        private void ReceiveState(INetworkPlayer player, TrenchStateMessage message)
        {
            if (GameAccess.IsServer() || !MissionManager.IsRunning || manager == null) return;
            if (message.Protocol != ProtocolVersion || message.LineId <= 0) return;
            if (!TrenchWire.ValidStage(message.Stage)) return;
            if (!TrenchWire.ValidDefenders(message.Defenders)) return;
            manager.ReceiveState(message.LineId, (TrenchStage)message.Stage, message.Defenders,
                message.Suppressed, message.Overrun);
        }

        private void ReceiveRemoved(INetworkPlayer player, TrenchLineRemovedMessage message)
        {
            if (GameAccess.IsServer() || !MissionManager.IsRunning || manager == null) return;
            if (message.Protocol != ProtocolVersion || message.LineId <= 0) return;
            manager.ReceiveRemoved(message.LineId);
        }

        internal static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;
            Bind(typeof(Writer<TrenchGeometryMessage>), "Write",
                (Action<NetworkWriter, TrenchGeometryMessage>)WriteGeometry);
            Bind(typeof(Reader<TrenchGeometryMessage>), "Read",
                (Func<NetworkReader, TrenchGeometryMessage>)ReadGeometry);
            Bind(typeof(Writer<TrenchStateMessage>), "Write",
                (Action<NetworkWriter, TrenchStateMessage>)WriteState);
            Bind(typeof(Reader<TrenchStateMessage>), "Read",
                (Func<NetworkReader, TrenchStateMessage>)ReadState);
            Bind(typeof(Writer<TrenchLineRemovedMessage>), "Write",
                (Action<NetworkWriter, TrenchLineRemovedMessage>)WriteRemoved);
            Bind(typeof(Reader<TrenchLineRemovedMessage>), "Read",
                (Func<NetworkReader, TrenchLineRemovedMessage>)ReadRemoved);
            MessagePacker.RegisterMessage<TrenchGeometryMessage>();
            MessagePacker.RegisterMessage<TrenchStateMessage>();
            MessagePacker.RegisterMessage<TrenchLineRemovedMessage>();
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(property,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null) throw new MissingMemberException(holder.Name, property);
            target.SetValue(null, value, null);
        }

        private static void WriteGeometry(NetworkWriter writer, TrenchGeometryMessage message)
        {
            writer.WriteByte(message.Protocol);
            writer.WritePackedInt32(message.LineId);
            writer.WriteByte(message.Stage);
            writer.WritePackedInt32(message.OwnerHash);
            WriteTrace(writer, message.Curve);
            WriteTrace(writer, message.Threat);
            WriteTrace(writer, message.Support);
            WriteTrace(writer, message.Redoubt);
            WriteTraceSet(writer, message.Links);
            WriteTraceSet(writer, message.Spurs);
        }

        private static TrenchGeometryMessage ReadGeometry(NetworkReader reader)
        {
            byte protocol = reader.ReadByte();
            var drop = new TrenchGeometryMessage { Protocol = protocol };
            if (protocol != ProtocolVersion) return drop;
            int lineId = reader.ReadPackedInt32();
            byte stage = reader.ReadByte();
            int ownerHash = reader.ReadPackedInt32();
            Vector3[] curve = ReadTrace(reader, TrenchWire.MaximumStations);
            Vector3[] threat = ReadTrace(reader, TrenchWire.MaximumStations);
            Vector3[] support = ReadTrace(reader, TrenchWire.MaximumStations);
            Vector3[] redoubt = ReadTrace(reader, TrenchWire.MaximumStations);
            Vector3[][] links = ReadTraceSet(reader, TrenchTraceMath.MaximumLinkTraces, TrenchWire.MaximumLinkStations);
            Vector3[][] spurs = ReadTraceSet(reader, TrenchTraceMath.MaximumSpurTraces, TrenchWire.MaximumSpurStations);
            if (lineId <= 0 || curve == null || threat == null || support == null ||
                redoubt == null || links == null || spurs == null) return drop;
            return new TrenchGeometryMessage
            {
                Protocol = protocol, LineId = lineId, Stage = stage, OwnerHash = ownerHash,
                Curve = curve, Threat = threat, Support = support, Redoubt = redoubt,
                Links = links, Spurs = spurs
            };
        }

        private static void WriteTrace(NetworkWriter writer, Vector3[] trace)
        {
            int count = trace != null ? trace.Length : 0;
            writer.WritePackedInt32(count);
            for (int i = 0; i < count; i++)
            {
                // Plan-view only: every consumer re-snaps height to its own terrain.
                writer.WriteSingle(trace[i].x);
                writer.WriteSingle(trace[i].z);
            }
        }

        private static Vector3[] ReadTrace(NetworkReader reader, int maximum)
        {
            int count = reader.ReadPackedInt32();
            if (count < 0 || count > maximum) return null;
            var trace = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float x = reader.ReadSingle();
                float z = reader.ReadSingle();
                if (!TrenchWire.Finite(x) || !TrenchWire.Finite(z)) return null;
                trace[i] = new Vector3(x, 0f, z);
            }
            return trace;
        }

        private static void WriteTraceSet(NetworkWriter writer, Vector3[][] traces)
        {
            int count = traces != null ? traces.Length : 0;
            writer.WritePackedInt32(count);
            for (int i = 0; i < count; i++) WriteTrace(writer, traces[i]);
        }

        private static Vector3[][] ReadTraceSet(NetworkReader reader, int maximumTraces, int maximumStations)
        {
            int count = reader.ReadPackedInt32();
            if (count < 0 || count > maximumTraces) return null;
            var traces = new Vector3[count][];
            for (int i = 0; i < count; i++)
            {
                traces[i] = ReadTrace(reader, maximumStations);
                if (traces[i] == null) return null;
            }
            return traces;
        }

        private static void WriteState(NetworkWriter writer, TrenchStateMessage message)
        {
            writer.WriteByte(message.Protocol);
            writer.WritePackedInt32(message.LineId);
            writer.WriteByte(message.Stage);
            writer.WriteByte(message.Defenders);
            writer.WriteBooleanExtension(message.Suppressed);
            writer.WriteBooleanExtension(message.Overrun);
        }

        private static TrenchStateMessage ReadState(NetworkReader reader)
        {
            byte protocol = reader.ReadByte();
            var drop = new TrenchStateMessage { Protocol = protocol };
            if (protocol != ProtocolVersion) return drop;
            return new TrenchStateMessage
            {
                Protocol = protocol,
                LineId = reader.ReadPackedInt32(),
                Stage = reader.ReadByte(),
                Defenders = reader.ReadByte(),
                Suppressed = reader.ReadBooleanExtension(),
                Overrun = reader.ReadBooleanExtension()
            };
        }

        private static void WriteRemoved(NetworkWriter writer, TrenchLineRemovedMessage message)
        {
            writer.WriteByte(message.Protocol);
            writer.WritePackedInt32(message.LineId);
        }

        private static TrenchLineRemovedMessage ReadRemoved(NetworkReader reader)
        {
            byte protocol = reader.ReadByte();
            var drop = new TrenchLineRemovedMessage { Protocol = protocol };
            if (protocol != ProtocolVersion) return drop;
            return new TrenchLineRemovedMessage { Protocol = protocol, LineId = reader.ReadPackedInt32() };
        }

        private static TrenchGeometryMessage ToGeometry(TrenchLine line) => new TrenchGeometryMessage
        {
            Protocol = ProtocolVersion,
            LineId = line.Id,
            Stage = (byte)line.Stage,
            OwnerHash = line.OwnerHash,
            Curve = line.Curve,
            Threat = line.Threat,
            Support = line.Support,
            Redoubt = line.Redoubt,
            Links = line.Links,
            Spurs = line.Spurs
        };

        private static TrenchStateMessage ToState(TrenchLine line) => new TrenchStateMessage
        {
            Protocol = ProtocolVersion,
            LineId = line.Id,
            Stage = (byte)line.Stage,
            Defenders = (byte)Math.Max(0, Math.Min(TrenchWire.MaximumDefenders, line.DefenderCount)),
            Suppressed = line.Suppressed,
            Overrun = line.Overrun
        };

        private static bool AllFinite(Vector3[] trace)
        {
            if (trace == null) return true;
            for (int i = 0; i < trace.Length; i++)
                if (!TrenchWire.Finite(trace[i].x) || !TrenchWire.Finite(trace[i].z)) return false;
            return true;
        }

        private static bool AllFinite(Vector3[][] traces)
        {
            if (traces == null) return true;
            for (int i = 0; i < traces.Length; i++)
                if (!AllFinite(traces[i])) return false;
            return true;
        }
    }
}
