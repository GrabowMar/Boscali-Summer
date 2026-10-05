using System;
using System.Reflection;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
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
        public float EventFactor;
        public float SilentFactor;
    }

    [NetworkMessage]
    internal struct ActivityPulseMessage
    {
        public byte Protocol;
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

    /// <summary>Client to host SPACE request. Wraps the engine-free command; protocol and request id travel inside it.</summary>
    [NetworkMessage]
    internal struct SpaceCommandMessage { public SpaceCommand Command; }

    /// <summary>Host to the requesting player: the verdict of one MARK, SEND or CLAIM (also the late push of a queued claim).</summary>
    [NetworkMessage]
    internal struct SpaceReplyMessage { public SpaceReply Reply; }

    /// <summary>Host to members of one faction only: headline always, rows while that member feed is open. Full or delta.</summary>
    [NetworkMessage]
    internal struct SpaceStateMessage { public SpaceStateData Data; }

    /// <summary>Host to members of one faction only: that faction's whole CYBER state (anchors, visible nodes, intrusions, recent events). Always a full snapshot.</summary>
    [NetworkMessage]
    internal struct CyberStateMessage { public CyberStateData Data; }

    /// <summary>Host to members of one faction only: that faction's whole SOF state (camps, teams, revealed targets, held buildings, revealed enemy team positions, recent events). Always a full snapshot.</summary>
    [NetworkMessage]
    internal struct SofStateMessage { public SofStateData Data; }

    internal sealed class SupportNet : MonoBehaviour
    {

        /// <summary>
        /// Protocol 29 adds a coalesced player input intent so parked remote operators can earn the active trickle.
        /// The host derives receipt time and limits pulses; the intent carries no credit or client timestamp.
        /// Older peers must not interpret the retired action and result ids.
        /// Protocol 31 adds the faction-only SPACE mirror: SpaceCommand / SpaceReply / SpaceState messages. Every one carries this
        /// byte and a mismatched byte decodes to an empty message. No faction, price, class or favourite is ever sent by a client.
        /// Protocol 32 widens the SPACE headline (always sent, even with the feed closed) by the newest live TASKED post (id, action,
        /// target count, OVERLORD or OPERATOR, the poster's callsign), the RADAR scan-ready deadline and the ENEMY INTENT line, and gives
        /// each post row its poster's callsign. Notices are derived on the client from that mirror: there is no new message.
        /// Protocol 33 adds the CYBER domain: four SpaceCommand kinds (CyberHop, CyberBurn, CyberDrop, CyberSync; ids only, the host derives faction, reach,
        /// fog and cost), the faction-only CyberStateMessage, and a 2-bit Domain on every TASKED post row (checked against the post's action).
        /// Protocol 34 adds the SOF domain: five SpaceCommand kinds (SofRaise, SofOrder, SofMission, SofDivert, SofSync; ids and packed 40 m cells only, the host derives faction,
        /// fog, cost, odds and the roll), the faction-only SofStateMessage, and the SOF TASKED post kinds (COVER TEAM, LASE TARGET).
        /// </summary>
        internal const byte ProtocolVersion = 34;

        private const float QueryInterval = 0.4f;
        private const int MaximumQueries = 64;

        private readonly Dictionary<INetworkPlayer, float> queries = new Dictionary<INetworkPlayer, float>();
        private SupportManager manager;
        private MessageHandler serverHandler;
        private MessageHandler clientHandler;
        private float nextRegistration;
        private float nextActivityPulse;

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
                serverHandler?.UnregisterHandler<ActivityPulseMessage>();
                serverHandler?.UnregisterHandler<SpaceCommandMessage>();
                queries.Clear();
                serverHandler = network.Server.MessageHandler;
                serverHandler.RegisterHandler<SupportRequestMessage>(ReceiveRequest, false);
                serverHandler.RegisterHandler<CruiseWaypointMessage>(ReceiveWaypoint, false);
                serverHandler.RegisterHandler<ActivityPulseMessage>(ReceiveActivityPulse, false);
                serverHandler.RegisterHandler<SpaceCommandMessage>(ReceiveSpaceCommand, false);
            }
            if (network.Client?.MessageHandler != null && network.Client.MessageHandler != clientHandler)
            {
                clientHandler?.UnregisterHandler<SupportResultMessage>();
                clientHandler?.UnregisterHandler<CreditStateMessage>();
                clientHandler?.UnregisterHandler<CruiseLegsMessage>();
                clientHandler?.UnregisterHandler<SpaceReplyMessage>();
                clientHandler?.UnregisterHandler<SpaceStateMessage>();
                clientHandler?.UnregisterHandler<CyberStateMessage>();
                clientHandler?.UnregisterHandler<SofStateMessage>();
                clientHandler = network.Client.MessageHandler;
                manager?.OnSpaceLinked(); // a fresh link: nothing from an earlier session may be shown
                clientHandler.RegisterHandler<SupportResultMessage>(ReceiveResult, false);
                clientHandler.RegisterHandler<CreditStateMessage>(ReceiveCredit, false);
                clientHandler.RegisterHandler<CruiseLegsMessage>(ReceiveCruiseLegs, false);
                clientHandler.RegisterHandler<SpaceReplyMessage>(ReceiveSpaceReply, false);
                clientHandler.RegisterHandler<SpaceStateMessage>(ReceiveSpaceState, false);
                clientHandler.RegisterHandler<CyberStateMessage>(ReceiveCyberState, false);
                clientHandler.RegisterHandler<SofStateMessage>(ReceiveSofState, false);
            }
        }

        private void OnDestroy()
        {
            serverHandler?.UnregisterHandler<SupportRequestMessage>();
            serverHandler?.UnregisterHandler<CruiseWaypointMessage>();
            serverHandler?.UnregisterHandler<ActivityPulseMessage>();
            serverHandler?.UnregisterHandler<SpaceCommandMessage>();
            clientHandler?.UnregisterHandler<SpaceReplyMessage>();
            clientHandler?.UnregisterHandler<SpaceStateMessage>();
            clientHandler?.UnregisterHandler<CyberStateMessage>();
            clientHandler?.UnregisterHandler<SofStateMessage>();
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
                Result = (byte)result, CooldownSeconds = action == SupportActionId.JtacUnlase ? 0f
                    : result == SupportResult.Accepted ? cooldown
                    : result == SupportResult.Cooldown ? manager.ServerCooldownRemaining(player) : 0f,
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

        /// <summary>One input intent per real second at most; the host alone stamps mission time and pays CR.</summary>
        internal void SendActivityPulse()
        {
            if (Time.unscaledTime < nextActivityPulse) return;
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active) return;
            nextActivityPulse = Time.unscaledTime + 1f;
            client.Send(new ActivityPulseMessage { Protocol = ProtocolVersion });
        }

        private void ReceiveActivityPulse(INetworkPlayer sender, ActivityPulseMessage message)
        {
            if (message.Protocol != ProtocolVersion || !GameAccess.IsServer() || sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null || player.HQ == null) return;
            manager?.ReceiveActivityPulse(player);
        }

        /// <summary>Server to owner: the player's CR balance and wallet freeze.</summary>
        internal bool SendCredit(Player player, int balance, int frozenSeconds, float eventFactor, float silentFactor)
        {
            var message = new CreditStateMessage { Protocol = ProtocolVersion, Balance = balance, FrozenSeconds = frozenSeconds, EventFactor = eventFactor, SilentFactor = silentFactor };
            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && ReferenceEquals(local, player))
            {
                manager.ReceiveCredit(message); // the host's own player is served in-process
                return true;
            }
            if (player?.Owner == null) return false;
            player.Owner.Send(message);
            return true;
        }

        // ---- SPACE (faction-only mirror and replayed MARK / SEND / CLAIM) ---------------------------

        /// <summary>
        /// Submits one SPACE command. The server own player runs the same evaluator in-process (singleplayer and listen-host
        /// never depend on the custom-message pipe); a remote client sends it. False when nothing could be sent.
        /// </summary>
        internal bool RequestSpace(SpaceCommand command)
        {
            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
            {
                manager.SpaceNet?.Receive(local, command);
                return true;
            }
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active) return false;
            client.Send(new SpaceCommandMessage { Command = command });
            return true;
        }

        /// <summary>Server to one requester. Only the requesting player is ever addressed.</summary>
        internal void SendSpaceReply(Player player, SpaceReply reply)
        {
            if (player == null) return;
            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && ReferenceEquals(local, player))
            {
                manager.QueueSpaceReply(reply); // delivered next frame, after the requester holds its request id
                return;
            }
            player.Owner?.Send(new SpaceReplyMessage { Reply = reply });
        }

        /// <summary>Server to one faction member (the caller selects members by their faction; nothing is broadcast).</summary>
        internal void SendSpaceState(Player player, SpaceStateData data)
        {
            if (player == null || data == null) return;
            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && ReferenceEquals(local, player))
            {
                manager.ReceiveSpaceState(data);
                return;
            }
            player.Owner?.Send(new SpaceStateMessage { Data = data });
        }

        /// <summary>Server to one faction member. The caller selects members by their faction; nothing is broadcast.</summary>
        internal void SendCyberState(Player player, CyberStateData data)
        {
            if (player == null || data == null) return;
            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && ReferenceEquals(local, player))
            {
                manager.ReceiveCyberState(data); // the mirror copies what it keeps
                return;
            }
            player.Owner?.Send(new CyberStateMessage { Data = data });
        }

        /// <summary>Server to one faction member. The caller selects members by their faction; nothing is broadcast.</summary>
        internal void SendSofState(Player player, SofStateData data)
        {
            if (player == null || data == null) return;
            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && ReferenceEquals(local, player))
            {
                manager.ReceiveSofState(data); // the mirror copies what it keeps
                return;
            }
            player.Owner?.Send(new SofStateMessage { Data = data });
        }

        private void ReceiveSofState(INetworkPlayer _, SofStateMessage message)
        {
            if (message.Data != null && message.Data.Protocol == ProtocolVersion) manager.ReceiveSofState(message.Data);
        }

        private void ReceiveCyberState(INetworkPlayer _, CyberStateMessage message)
        {
            if (message.Data != null && message.Data.Protocol == ProtocolVersion) manager.ReceiveCyberState(message.Data);
        }

        private void ReceiveSpaceCommand(INetworkPlayer sender, SpaceCommandMessage message)
        {
            if (message.Command.Protocol != ProtocolVersion || message.Command.Kind == SpaceCommandKind.None || !GameAccess.IsServer() ||
                sender == null || !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            manager.SpaceNet?.Receive(player, message.Command);
        }

        private void ReceiveSpaceReply(INetworkPlayer _, SpaceReplyMessage message)
        {
            if (message.Reply.Protocol == ProtocolVersion) manager.ReceiveSpaceReply(message.Reply);
        }

        private void ReceiveSpaceState(INetworkPlayer _, SpaceStateMessage message)
        {
            if (message.Data != null && message.Data.Protocol == ProtocolVersion) manager.ReceiveSpaceState(message.Data);
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
                w.WriteSingle(v.EventFactor);
                w.WriteSingle(v.SilentFactor);
            });
            SetReader<CreditStateMessage>(r =>
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return new CreditStateMessage { Protocol = protocol };
                return new CreditStateMessage { Protocol = protocol, Balance = r.ReadInt32(), FrozenSeconds = r.ReadInt32(), EventFactor = r.ReadSingle(), SilentFactor = r.ReadSingle() };
            });
            SetWriter<ActivityPulseMessage>((w, v) => w.WriteByte(v.Protocol));
            SetReader<ActivityPulseMessage>(r => new ActivityPulseMessage { Protocol = r.ReadByte() });
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
            SetWriter<SpaceCommandMessage>((w, v) =>
            {
                WireOut.W = w;
                try { SpaceWire.WriteCommand(WireOut, v.Command); }
                finally { WireOut.W = null; }
            });
            SetReader<SpaceCommandMessage>(r =>
            {
                WireIn.R = r;
                try { return new SpaceCommandMessage { Command = SpaceWire.ReadCommand(WireIn, ProtocolVersion) }; }
                catch (Exception) { return default; } // a malformed message is inert, never a crash
                finally { WireIn.R = null; }
            });
            SetWriter<SpaceReplyMessage>((w, v) =>
            {
                WireOut.W = w;
                try { SpaceWire.WriteReply(WireOut, v.Reply); }
                finally { WireOut.W = null; }
            });
            SetReader<SpaceReplyMessage>(r =>
            {
                WireIn.R = r;
                try { return new SpaceReplyMessage { Reply = SpaceWire.ReadReply(WireIn, ProtocolVersion) }; }
                catch (Exception) { return default; }
                finally { WireIn.R = null; }
            });
            SetWriter<SpaceStateMessage>((w, v) =>
            {
                WireOut.W = w;
                try { SpaceWire.WriteState(WireOut, v.Data ?? new SpaceStateData { Protocol = ProtocolVersion }); }
                finally { WireOut.W = null; }
            });
            SetReader<SpaceStateMessage>(r =>
            {
                WireIn.R = r;
                try { return new SpaceStateMessage { Data = SpaceWire.ReadState(WireIn, ProtocolVersion) }; }
                catch (Exception) { return default; }
                finally { WireIn.R = null; }
            });
            SetWriter<CyberStateMessage>((w, v) =>
            {
                WireOut.W = w;
                try { CyberWire.WriteState(WireOut, v.Data ?? new CyberStateData { Protocol = ProtocolVersion }); }
                finally { WireOut.W = null; }
            });
            SetReader<CyberStateMessage>(r =>
            {
                WireIn.R = r;
                try { return new CyberStateMessage { Data = CyberWire.ReadState(WireIn, ProtocolVersion) }; }
                catch (Exception) { return default; }
                finally { WireIn.R = null; }
            });
            SetWriter<SofStateMessage>((w, v) =>
            {
                WireOut.W = w;
                try { SofWire.WriteState(WireOut, v.Data ?? new SofStateData { Protocol = ProtocolVersion }); }
                finally { WireOut.W = null; }
            });
            SetReader<SofStateMessage>(r =>
            {
                WireIn.R = r;
                try { return new SofStateMessage { Data = SofWire.ReadState(WireIn, ProtocolVersion) }; }
                catch (Exception) { return default; }
                finally { WireIn.R = null; }
            });
            MessagePacker.RegisterMessage<SupportRequestMessage>();
            MessagePacker.RegisterMessage<SupportResultMessage>();
            MessagePacker.RegisterMessage<CreditStateMessage>();
            MessagePacker.RegisterMessage<ActivityPulseMessage>();
            MessagePacker.RegisterMessage<CruiseWaypointMessage>();
            MessagePacker.RegisterMessage<CruiseLegsMessage>();
            MessagePacker.RegisterMessage<SpaceCommandMessage>();
            MessagePacker.RegisterMessage<SpaceReplyMessage>();
            MessagePacker.RegisterMessage<SpaceStateMessage>();
            MessagePacker.RegisterMessage<CyberStateMessage>();
            MessagePacker.RegisterMessage<SofStateMessage>();
        }

        // Mirage reads and writes go through these two adapters so the SPACE codec stays engine-free and testable.
        private sealed class WireWriter : ISpaceWriter
        {
            public NetworkWriter W;
            public void WriteByte(byte value) => W.WriteByte(value);
        }

        private sealed class WireReader : ISpaceReader
        {
            public NetworkReader R;
            public int Remaining => Math.Max(0, (R.BitLength - R.BitPosition) >> 3);
            public bool TryReadByte(out byte value)
            {
                if (Remaining < 1) { value = 0; return false; }
                value = R.ReadByte();
                return true;
            }
        }

        private static readonly WireWriter WireOut = new WireWriter();
        private static readonly WireReader WireIn = new WireReader();

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
