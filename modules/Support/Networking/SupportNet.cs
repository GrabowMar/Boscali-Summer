using System;
using System.Reflection;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Core.Game;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Networking
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

    [NetworkMessage]
    internal struct CreditStateMessage
    {
        public byte Protocol;
        public int Balance;
        public int FrozenSeconds;
        public float PriceFactor;
    }

    [NetworkMessage]
    internal struct CruiseWaypointMessage
    {
        public byte Protocol;
        public int RequestId;
        public float X;
        public float Z;
        public bool Clear;
    }

    [NetworkMessage]
    internal struct CruiseLegsMessage
    {
        public byte Protocol;
        public int RequestId;
        public ulong OwnerId;
        public byte Result;
        public string FactionName;
        public byte LegCount;
        public float[] X;
        public float[] Z;
        public bool Dive;
        public float Tti;
    }

    internal sealed class SupportNet : MonoBehaviour
    {

        /// <summary>
        /// Protocol 28 removes the old OPS (station, CYBER, SPEC OPS) messages and adds the CR credit
        /// message; the surviving support request, result and cruise messages are unchanged.
        /// Older peers must not interpret the retired action and result ids.
        /// </summary>
        internal const byte ProtocolVersion = 28;

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
                serverHandler?.UnregisterHandler<CruiseWaypointMessage>();
                queries.Clear();
                serverHandler = network.Server.MessageHandler;
                serverHandler.RegisterHandler<SupportRequestMessage>(ReceiveRequest, false);
                serverHandler.RegisterHandler<CruiseWaypointMessage>(ReceiveWaypoint, false);
            }
            if (network.Client?.MessageHandler != null && network.Client.MessageHandler != clientHandler)
            {
                clientHandler?.UnregisterHandler<SupportResultMessage>();
                clientHandler?.UnregisterHandler<CreditStateMessage>();
                clientHandler?.UnregisterHandler<CruiseLegsMessage>();
                clientHandler = network.Client.MessageHandler;
                clientHandler.RegisterHandler<SupportResultMessage>(ReceiveResult, false);
                clientHandler.RegisterHandler<CreditStateMessage>(ReceiveCredit, false);
                clientHandler.RegisterHandler<CruiseLegsMessage>(ReceiveCruiseLegs, false);
            }
        }

        private void OnDestroy()
        {
            serverHandler?.UnregisterHandler<SupportRequestMessage>();
            serverHandler?.UnregisterHandler<CruiseWaypointMessage>();
            clientHandler?.UnregisterHandler<SupportResultMessage>();
            clientHandler?.UnregisterHandler<CreditStateMessage>();
            clientHandler?.UnregisterHandler<CruiseLegsMessage>();
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
                manager.ReceiveResult(Reply(message, result, manager.ServerCooldownFor(local), local));
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
            float tti = manager.TakeTti(request.RequestId);
            return new SupportResultMessage {
                Protocol = ProtocolVersion, RequestId = request.RequestId, Action = request.Action,
                Result = (byte)result, CooldownSeconds = result == SupportResult.Accepted ? cooldown : 0f,
                Radius = radius, X = target.x, Y = target.y, Z = target.z,
                Contacts = contacts < 0 ? 0 : contacts,
                Duration = action == SupportActionId.Prsm || action == SupportActionId.Cruise ? ClampTti(tti) :
                    action == SupportActionId.Emp ? SupportEffectPolicy.EmpDuration :
                    action == SupportActionId.FlareMissile ? manager.Settings.FlareBarrageDuration.Value :
                    action == SupportActionId.JtacMark ? manager.Settings.JtacMarkDuration.Value : 10f
            };
        }

        private static float ClampTti(float tti) => tti < 0f ? 0f : tti > 60f ? 60f : tti;

        private void ReceiveRequest(INetworkPlayer sender, SupportRequestMessage request)
        {
            if (request.Protocol != ProtocolVersion) return;
            if (sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;
            SupportResult result = manager.Evaluate(player, request);
            sender.Send(Reply(request, result, manager.ServerCooldownFor(player), player));
        }

        private void ReceiveResult(INetworkPlayer _, SupportResultMessage result)
        {
            if (result.Protocol == ProtocolVersion) manager.ReceiveResult(result);
        }

        private void ReceiveCredit(INetworkPlayer _, CreditStateMessage message)
        {
            if (message.Protocol == ProtocolVersion) manager.ReceiveCredit(message);
        }

        /// <summary>Server to owner: the player's CR balance and wallet freeze.</summary>
        internal void SendCredit(Player player, int balance, int frozenSeconds, float priceFactor)
        {
            var message = new CreditStateMessage { Protocol = ProtocolVersion, Balance = balance, FrozenSeconds = frozenSeconds, PriceFactor = priceFactor };
            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && ReferenceEquals(local, player))
            {
                manager.ReceiveCredit(message); // the host's own player is served in-process
                return;
            }
            player?.Owner?.Send(message);
        }

        /// <summary>Submits a cruise leg intent; validated and broadcast in-process on the server.</summary>
        public void SendWaypoint(int requestId, GlobalPosition point, bool clear)
        {
            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
            {
                manager.ApplyWaypoint(local, requestId, point, clear);
                return;
            }
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active)
            {
                manager.ReportOffline();
                return;
            }
            client.Send(new CruiseWaypointMessage
            {
                Protocol = ProtocolVersion,
                RequestId = requestId,
                X = point.x,
                Z = point.z,
                Clear = clear
            });
        }

        public void BroadcastCruiseLegs(CruiseLegsMessage message)
        {
            if (!GameAccess.IsServer()) return;
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(message, authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void ReceiveWaypoint(INetworkPlayer sender, CruiseWaypointMessage intent)
        {
            if (intent.Protocol != ProtocolVersion || !GameAccess.IsServer() || !RateLimit(sender)) return;
            if (sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null)
                return;
            manager.ApplyWaypoint(player, intent.RequestId, new GlobalPosition(intent.X, 0f, intent.Z), intent.Clear);
        }

        private void ReceiveCruiseLegs(INetworkPlayer _, CruiseLegsMessage message)
        {
            if (message.Protocol == ProtocolVersion) manager.ReceiveCruiseLegs(message);
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
            SetWriter<CreditStateMessage>((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteInt32(v.Balance);
                w.WriteInt32(v.FrozenSeconds);
                w.WriteSingle(v.PriceFactor);
            });
            SetReader<CreditStateMessage>(r =>
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return new CreditStateMessage { Protocol = protocol };
                return new CreditStateMessage { Protocol = protocol, Balance = r.ReadInt32(), FrozenSeconds = r.ReadInt32(), PriceFactor = r.ReadSingle() };
            });
            SetWriter<CruiseWaypointMessage>((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WritePackedInt32(v.RequestId);
                w.WriteSingle(v.X);
                w.WriteSingle(v.Z);
                w.WriteByte(v.Clear ? (byte)1 : (byte)0);
            });
            SetReader<CruiseWaypointMessage>(r =>
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return new CruiseWaypointMessage { Protocol = protocol };
                return new CruiseWaypointMessage
                {
                    Protocol = protocol,
                    RequestId = r.ReadPackedInt32(),
                    X = r.ReadSingle(),
                    Z = r.ReadSingle(),
                    Clear = r.ReadByte() != 0
                };
            });
            SetWriter<CruiseLegsMessage>((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WritePackedInt32(v.RequestId);
                w.WriteUInt64(v.OwnerId);
                w.WriteByte(v.Result);
                w.WriteString(v.FactionName ?? string.Empty);
                int legs = Math.Max(0, Math.Min((int)v.LegCount, Math.Min(StrikeBallistics.MaxLegs,
                    Math.Min(v.X?.Length ?? 0, v.Z?.Length ?? 0))));
                w.WriteByte((byte)legs);
                for (int i = 0; i < legs; i++)
                {
                    w.WriteSingle(v.X[i]);
                    w.WriteSingle(v.Z[i]);
                }
                w.WriteByte(v.Dive ? (byte)1 : (byte)0);
                w.WriteSingle(v.Tti);
            });
            SetReader<CruiseLegsMessage>(r =>
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return new CruiseLegsMessage { Protocol = protocol };
                var message = new CruiseLegsMessage
                {
                    Protocol = protocol,
                    RequestId = r.ReadPackedInt32(),
                    OwnerId = r.ReadUInt64(),
                    Result = r.ReadByte(),
                    FactionName = r.ReadString()
                };
                int legs = r.ReadByte();
                if (legs > StrikeBallistics.MaxLegs) return new CruiseLegsMessage { Protocol = 0 };
                message.LegCount = (byte)legs;
                message.X = new float[StrikeBallistics.MaxLegs];
                message.Z = new float[StrikeBallistics.MaxLegs];
                for (int i = 0; i < legs; i++)
                {
                    message.X[i] = r.ReadSingle();
                    message.Z[i] = r.ReadSingle();
                }
                message.Dive = r.ReadByte() != 0;
                message.Tti = r.ReadSingle();
                return message;
            });
            MessagePacker.RegisterMessage<SupportRequestMessage>();
            MessagePacker.RegisterMessage<SupportResultMessage>();
            MessagePacker.RegisterMessage<CreditStateMessage>();
            MessagePacker.RegisterMessage<CruiseWaypointMessage>();
            MessagePacker.RegisterMessage<CruiseLegsMessage>();
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
