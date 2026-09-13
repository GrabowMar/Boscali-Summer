using System;
using System.Reflection;
using System.Collections.Generic;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Networking
{
    [NetworkMessage]
    internal struct SupportRequestMessage
    {
        public byte Protocol;
        public int RequestId;
        public byte Action;
        public float X;
        public float Y;
        public float Z;
    }

    [NetworkMessage]
    internal struct SupportResultMessage
    {
        public byte Protocol;
        public int RequestId;
        public byte Action;
        public byte Result;
        public float CooldownSeconds;
        public float Radius;
        public float Duration;
        public int Contacts;
        public float X;
        public float Y;
        public float Z;
    }

    internal sealed class SupportNet : MonoBehaviour
    {
        /// <summary>
        /// Protocol 6 replaces orbital ground tracks with station-keeping satellites and
        /// their transfer state. Older peers must not interpret fleet or hack ids.
        /// </summary>
        internal const byte ProtocolVersion = 6;

        private const float QueryInterval = 0.4f;
        private const int MaximumQueries = 64;

        private readonly Dictionary<INetworkPlayer, float> queries = new Dictionary<INetworkPlayer, float>();
        private SupportManager manager;
        private MessageHandler serverHandler;
        private MessageHandler clientHandler;
        private float nextRegistration;

        public void Configure(SupportManager support)
        {
            manager = support;
            InstallSerializers();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            if (network == null) return;
            if (network.Server != null && network.Server.Active &&
                network.Server.MessageHandler != null && network.Server.MessageHandler != serverHandler)
            {
                serverHandler?.UnregisterHandler<SupportRequestMessage>();
                serverHandler?.UnregisterHandler<OpsQueryMessage>();
                serverHandler?.UnregisterHandler<OpsCommandMessage>();
                queries.Clear();
                serverHandler = network.Server.MessageHandler;
                serverHandler.RegisterHandler<SupportRequestMessage>(ReceiveRequest, false);
                serverHandler.RegisterHandler<OpsQueryMessage>(ReceiveOpsQuery, false);
                serverHandler.RegisterHandler<OpsCommandMessage>(ReceiveOpsCommand, false);
            }
            if (network.Client?.MessageHandler != null && network.Client.MessageHandler != clientHandler)
            {
                clientHandler?.UnregisterHandler<SupportResultMessage>();
                clientHandler?.UnregisterHandler<OpsStateMessage>();
                clientHandler?.UnregisterHandler<CyberEffectMessage>();
                clientHandler = network.Client.MessageHandler;
                clientHandler.RegisterHandler<SupportResultMessage>(ReceiveResult, false);
                clientHandler.RegisterHandler<OpsStateMessage>(ReceiveOpsState, false);
                clientHandler.RegisterHandler<CyberEffectMessage>(ReceiveCyberEffect, false);
            }
        }

        private void OnDestroy()
        {
            serverHandler?.UnregisterHandler<SupportRequestMessage>();
            serverHandler?.UnregisterHandler<OpsQueryMessage>();
            serverHandler?.UnregisterHandler<OpsCommandMessage>();
            clientHandler?.UnregisterHandler<SupportResultMessage>();
            clientHandler?.UnregisterHandler<OpsStateMessage>();
            clientHandler?.UnregisterHandler<CyberEffectMessage>();
            queries.Clear();
        }

        /// <summary>
        /// Submits one request. When this process is the server the request is validated and
        /// executed in-process, so single-player and listen-host never depend on the
        /// custom-message pipe and a dropped send can no longer look like a pending one.
        /// </summary>
        public void Request(int requestId, SupportActionId action, GlobalPosition target)
        {
            var message = new SupportRequestMessage
            {
                Protocol = ProtocolVersion,
                RequestId = requestId,
                Action = (byte)action,
                X = target.x,
                Y = target.y,
                Z = target.z
            };

            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
            {
                SupportResult result = manager.Evaluate(local, message);
                manager.ReceiveResult(Reply(message, result, manager.ServerCooldown, local));
                return;
            }

            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active)
            {
                manager.ReportOffline();
                return;
            }
            client.Send(message);
        }

        private SupportResultMessage Reply(
            SupportRequestMessage request, SupportResult result, float cooldown, Player player)
        {
            var action = (SupportActionId)request.Action;
            var target = new GlobalPosition(request.X, request.Y, request.Z);
            float radius = manager.GetEffectRadius(action, player != null ? player.HQ : null);
            if (action == SupportActionId.Fortify && result == SupportResult.Accepted)
            {
                var zone = SupportTargeting.NearestOwnedAirbase(player, target.ToLocalPosition(), out _);
                if (zone != null)
                {
                    target = (zone.center != null ? zone.center.position : zone.transform.position).ToGlobalPosition();
                    radius = zone.GetRadius();
                }
            }
            int contacts = manager.TakeContacts(request.RequestId);
            return new SupportResultMessage {
                Protocol = ProtocolVersion, RequestId = request.RequestId, Action = request.Action,
                Result = (byte)result, CooldownSeconds = result == SupportResult.Accepted ? cooldown : 0f,
                Radius = radius, X = target.x, Y = target.y, Z = target.z,
                Contacts = contacts < 0 ? 0 : contacts,
                Duration = action == SupportActionId.Emp ? SupportEffectPolicy.EmpDuration :
                    action == SupportActionId.FlareMissile ? manager.Settings.FlareBarrageDuration.Value : 10f
            };
        }

        private void ReceiveRequest(INetworkPlayer sender, SupportRequestMessage request)
        {
            if (request.Protocol != ProtocolVersion) return;
            if (sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;
            SupportResult result = manager.Evaluate(player, request);
            sender.Send(Reply(request, result, manager.ServerCooldown, player));
        }

        private void ReceiveResult(INetworkPlayer _, SupportResultMessage result)
        {
            if (result.Protocol == ProtocolVersion) manager.ReceiveResult(result);
        }

        // ---- Fleet and infrastructure ------------------------------------------------------

        public void QueryOps()
        {
            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local))
            {
                manager.ReceiveOps(manager.Snapshot(local, 0, SupportResult.None));
                return;
            }
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client != null && client.Active)
                client.Send(new OpsQueryMessage { Protocol = ProtocolVersion });
        }

        public void Command(int requestId, OpsCommand command, byte arg, byte arg2, GlobalPosition target)
        {
            var message = new OpsCommandMessage
            {
                Protocol = ProtocolVersion,
                RequestId = requestId,
                Command = (byte)command,
                Arg = arg,
                Arg2 = arg2,
                X = target.x,
                Z = target.z
            };
            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
            {
                SupportResult result = manager.EvaluateCommand(local, message);
                manager.ReceiveOps(manager.Snapshot(local, requestId, result));
                return;
            }
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active)
            {
                manager.ReportOffline();
                return;
            }
            client.Send(message);
        }

        private void ReceiveOpsQuery(INetworkPlayer sender, OpsQueryMessage query)
        {
            if (query.Protocol != ProtocolVersion || !GameAccess.IsServer() || !RateLimit(sender)) return;
            if (!sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            sender.Send(manager.Snapshot(player, 0, SupportResult.None));
        }

        private void ReceiveOpsCommand(INetworkPlayer sender, OpsCommandMessage command)
        {
            if (command.Protocol != ProtocolVersion) return;
            if (sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null ||
                !RateLimit(sender)) return;
            SupportResult result = manager.EvaluateCommand(player, command);
            sender.Send(manager.Snapshot(player, command.RequestId, result));
        }

        private void ReceiveOpsState(INetworkPlayer _, OpsStateMessage state)
        {
            if (state.Protocol == ProtocolVersion) manager.ReceiveOps(state);
        }

        private void ReceiveCyberEffect(INetworkPlayer _, CyberEffectMessage message)
        {
            if (message.Protocol == ProtocolVersion) manager.ReceiveCyberEffect(message);
        }

        public void BroadcastCyberEffect(HackKind kind, string factionName, float x, float z, float duration)
        {
            if (!GameAccess.IsServer()) return;
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(new CyberEffectMessage
            {
                Protocol = ProtocolVersion,
                Kind = (byte)kind,
                FactionName = factionName ?? string.Empty,
                X = x,
                Z = z,
                Duration = duration
            }, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private bool RateLimit(INetworkPlayer sender)
        {
            if (sender == null) return false;
            float now = Time.unscaledTime;
            if (queries.TryGetValue(sender, out float last) && now - last < QueryInterval) return false;
            if (!queries.ContainsKey(sender) && queries.Count >= MaximumQueries)
            {
                // Bounded cache: evict only an idle query source, never reset active rate limits.
                INetworkPlayer expired = null;
                foreach (var pair in queries) if (now - pair.Value > 10f) { expired = pair.Key; break; }
                if (expired == null) return false;
                queries.Remove(expired);
            }
            queries[sender] = now;
            return true;
        }

        // ---- Serializers ---------------------------------------------------------------------

        private static bool serializersInstalled;

        private static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;
            SetWriter<SupportRequestMessage>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WritePackedInt32(value.RequestId);
                writer.WriteByte(value.Action);
                writer.WriteSingle(value.X);
                writer.WriteSingle(value.Y);
                writer.WriteSingle(value.Z);
            });
            SetReader<SupportRequestMessage>(reader => new SupportRequestMessage
            {
                Protocol = reader.ReadByte(),
                RequestId = reader.ReadPackedInt32(),
                Action = reader.ReadByte(),
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle()
            });
            SetWriter<SupportResultMessage>((writer, value) =>
            {
                writer.WriteByte(value.Protocol);
                writer.WritePackedInt32(value.RequestId);
                writer.WriteByte(value.Action);
                writer.WriteByte(value.Result);
                writer.WriteSingle(value.CooldownSeconds);
                writer.WriteSingle(value.Radius);
                writer.WriteSingle(value.Duration);
                writer.WritePackedInt32(value.Contacts);
                writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Z);
            });
            SetReader<SupportResultMessage>(reader =>
            {
                byte protocol = reader.ReadByte();
                if (protocol != ProtocolVersion) return new SupportResultMessage { Protocol = protocol };
                return new SupportResultMessage {
                    Protocol = protocol, RequestId = reader.ReadPackedInt32(),
                    Action = reader.ReadByte(), Result = reader.ReadByte(),
                    CooldownSeconds = reader.ReadSingle(),
                    Radius = reader.ReadSingle(), Duration = reader.ReadSingle(),
                    Contacts = reader.ReadPackedInt32(),
                    X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle()
                };
            });
            SetWriter<OpsQueryMessage>((w, v) => w.WriteByte(v.Protocol));
            SetReader<OpsQueryMessage>(r => new OpsQueryMessage { Protocol = r.ReadByte() });
            SetWriter<OpsCommandMessage>((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WritePackedInt32(v.RequestId);
                w.WriteByte(v.Command);
                w.WriteByte(v.Arg);
                w.WriteByte(v.Arg2);
                w.WriteSingle(v.X);
                w.WriteSingle(v.Z);
            });
            SetReader<OpsCommandMessage>(r => new OpsCommandMessage
            {
                Protocol = r.ReadByte(),
                RequestId = r.ReadPackedInt32(),
                Command = r.ReadByte(),
                Arg = r.ReadByte(),
                Arg2 = r.ReadByte(),
                X = r.ReadSingle(),
                Z = r.ReadSingle()
            });
            SetWriter<OpsStateMessage>((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WritePackedInt32(v.RequestId);
                w.WriteByte(v.Result);
                int count = ClampCount(v.SatelliteCount, v.SatelliteIds);
                w.WriteByte((byte)count);
                for (int i = 0; i < count; i++)
                {
                    w.WriteByte(v.SatelliteIds[i]);
                    w.WriteByte(v.SatelliteRoles[i]);
                    w.WriteByte(v.SatelliteAltitudes[i]);
                    w.WriteByte(v.SatelliteStates[i]);
                    w.WriteByte(v.SatelliteFuel[i]);
                    w.WriteSingle(v.StationXs[i]);
                    w.WriteSingle(v.StationZs[i]);
                    w.WriteSingle(v.OriginXs[i]);
                    w.WriteSingle(v.OriginZs[i]);
                    w.WriteSingle(v.TransitLeft[i]);
                    w.WriteSingle(v.TransitTotal[i]);
                }
                w.WriteByte(v.Sigint); w.WriteByte(v.Crypto);
                w.WriteByte(v.Disrupt); w.WriteByte(v.Ew);
            });
            SetReader<OpsStateMessage>(r =>
            {
                byte protocol = r.ReadByte();
                var message = new OpsStateMessage
                {
                    Protocol = protocol,
                    SatelliteIds = new byte[SpaceOperations.MaximumSatellites],
                    SatelliteRoles = new byte[SpaceOperations.MaximumSatellites],
                    SatelliteAltitudes = new byte[SpaceOperations.MaximumSatellites],
                    SatelliteStates = new byte[SpaceOperations.MaximumSatellites],
                    SatelliteFuel = new byte[SpaceOperations.MaximumSatellites],
                    StationXs = new float[SpaceOperations.MaximumSatellites],
                    StationZs = new float[SpaceOperations.MaximumSatellites],
                    OriginXs = new float[SpaceOperations.MaximumSatellites],
                    OriginZs = new float[SpaceOperations.MaximumSatellites],
                    TransitLeft = new float[SpaceOperations.MaximumSatellites],
                    TransitTotal = new float[SpaceOperations.MaximumSatellites]
                };
                if (protocol != ProtocolVersion) return message;
                message.RequestId = r.ReadPackedInt32();
                message.Result = r.ReadByte();
                int count = Math.Min((int)r.ReadByte(), SpaceOperations.MaximumSatellites);
                message.SatelliteCount = (byte)count;
                for (int i = 0; i < count; i++)
                {
                    message.SatelliteIds[i] = r.ReadByte();
                    message.SatelliteRoles[i] = r.ReadByte();
                    message.SatelliteAltitudes[i] = r.ReadByte();
                    message.SatelliteStates[i] = r.ReadByte();
                    message.SatelliteFuel[i] = r.ReadByte();
                    message.StationXs[i] = r.ReadSingle();
                    message.StationZs[i] = r.ReadSingle();
                    message.OriginXs[i] = r.ReadSingle();
                    message.OriginZs[i] = r.ReadSingle();
                    message.TransitLeft[i] = r.ReadSingle();
                    message.TransitTotal[i] = r.ReadSingle();
                }
                message.Sigint = r.ReadByte(); message.Crypto = r.ReadByte();
                message.Disrupt = r.ReadByte(); message.Ew = r.ReadByte();
                return message;
            });
            SetWriter<CyberEffectMessage>((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteByte(v.Kind);
                w.WriteString(v.FactionName ?? string.Empty);
                w.WriteSingle(v.X);
                w.WriteSingle(v.Z);
                w.WriteSingle(v.Duration);
            });
            SetReader<CyberEffectMessage>(r =>
            {
                byte protocol = r.ReadByte();
                var message = new CyberEffectMessage { Protocol = protocol };
                if (protocol != ProtocolVersion) return message;
                message.Kind = r.ReadByte();
                message.FactionName = r.ReadString();
                message.X = r.ReadSingle();
                message.Z = r.ReadSingle();
                message.Duration = r.ReadSingle();
                return message;
            });
            MessagePacker.RegisterMessage<OpsQueryMessage>();
            MessagePacker.RegisterMessage<OpsCommandMessage>();
            MessagePacker.RegisterMessage<OpsStateMessage>();
            MessagePacker.RegisterMessage<CyberEffectMessage>();
            MessagePacker.RegisterMessage<SupportRequestMessage>();
            MessagePacker.RegisterMessage<SupportResultMessage>();
        }

        private static int ClampCount(byte count, byte[] ids)
        {
            if (ids == null || ids.Length == 0) return 0;
            return Math.Min((int)count, Math.Min(SpaceOperations.MaximumSatellites, ids.Length));
        }

        private static void SetWriter<T>(Action<NetworkWriter, T> writer) =>
            Bind(typeof(Writer<T>), "Write", writer);

        private static void SetReader<T>(Func<NetworkReader, T> reader) =>
            Bind(typeof(Reader<T>), "Read", reader);

        // A missing seam used to be swallowed by a null-conditional, leaving every support
        // message silently unable to round-trip. Report it instead.
        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(
                property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                Plugin.Logger.LogError(
                    "[Support] Mirage serializer seam " + holder.Name + "." + property +
                    " is missing; support requests cannot replicate on this game build.");
                return;
            }
            target.SetValue(null, value, null);
        }
    }
}
