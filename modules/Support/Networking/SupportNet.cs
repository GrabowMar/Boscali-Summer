using System;
using System.Reflection;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
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

    /// <summary>Host to every member: the viewer's own faction's OPERATIONS (bars, countdowns, satellites), the pings of enemy operations, and any ASAT in flight. Always a full snapshot.</summary>
    [NetworkMessage]
    internal struct OpsStateMessage { public OpsStateData Data; }

    /// <summary>Host to members of one faction only: that faction's three fronts (readiness, superiority, budget, posture, funding weights, focus pin, programme queue, log). Always a full snapshot, sent on change.</summary>
    [NetworkMessage]
    internal struct FrontStateMessage { public FrontStateData Data; }

    internal sealed class SupportNet : MonoBehaviour
    {

        /// <summary>
        /// Protocol 29 adds a coalesced player input intent so parked remote operators can earn the active trickle.
        /// The host derives receipt time and limits pulses; the intent carries no credit or client timestamp (removed again by protocol 37).
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
        /// Protocol 35 adds OPERATIONS: four SpaceCommand kinds (OpFund, OpPlan, OpCancel, OpSync; a domain and a tier, a kind and an opaque target id, never a price or a faction) and the
        /// OpsStateMessage (own bars and satellites, enemy pings only, ASAT flights for everyone).
        /// Protocol 36 adds WATCH OFFICER OVERLORD for CYBER and SOF and the AI factions: no new message or command, one field on the OpsStateMessage, the faction's last three OVERLORD
        /// actions (a sequence, a domain, a reason code and two argument bytes each, rebuilt into words on the client), which also rides a state with OPERATIONS off.
        /// Protocol 37 (OPS FRONTS S0) pays perks in vanilla allocation: CreditStateMessage and ActivityPulseMessage are removed (the client reads Player.Allocation natively),
        /// RADAR SCAN and MTI SWEEP merge into RECON PASS (the MTI id retires) and four perk ids are appended (RECON TEAM, SABOTAGE STRIKE, RADAR BLIND, SAM NET DOWN).
        /// Protocol 38 (OPS FRONTS S1b) adds the fronts: six SpaceCommand kinds (FrontDirective, FrontPriority, FrontFocus, FrontQueue, FrontDonate and FrontSync; a front and a posture or
        /// programme id, three weights, a packed focus point or an allocation amount, never a price or a faction), the faction-only FrontStateMessage and the front verdict bytes on the
        /// SpaceReply. OperationFund, plan and cancel are refused by the host (the programmes replace the OPERATIONS bars).
        /// Protocol 39 (GEO) makes every satellite a station-keeping GeoBird: one SpaceCommand kind (RelocateBird: a bird and a packed map point; the host judges fuel, burn state and distance and answers with a front verdict byte),
        /// and the OpsStateMessage carries each own and enemy bird's from / to point, burn start and fuel (11 bytes a bird). RECON PASS, SAT CAMERA and ORBITAL ROD are refused outside their bird's footprint.
        /// </summary>
        internal const byte ProtocolVersion = 39;

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
            NetworkManagerNuclearOption network = GameAccess.NetworkManagerOrNull;
            if (network == null) return;
            if (network.Server != null && network.Server.Active &&
                network.Server.MessageHandler != null && network.Server.MessageHandler != serverHandler)
            {
                serverHandler?.UnregisterHandler<SupportRequestMessage>();
                serverHandler?.UnregisterHandler<CruiseWaypointMessage>();
                serverHandler?.UnregisterHandler<SpaceCommandMessage>();
                queries.Clear();
                serverHandler = network.Server.MessageHandler;
                serverHandler.RegisterHandler<SupportRequestMessage>(ReceiveRequest, false);
                serverHandler.RegisterHandler<CruiseWaypointMessage>(ReceiveWaypoint, false);
                serverHandler.RegisterHandler<SpaceCommandMessage>(ReceiveSpaceCommand, false);
            }
            if (network.Client?.MessageHandler != null && network.Client.MessageHandler != clientHandler)
            {
                clientHandler?.UnregisterHandler<SupportResultMessage>();
                clientHandler?.UnregisterHandler<CruiseLegsMessage>();
                clientHandler?.UnregisterHandler<SpaceReplyMessage>();
                clientHandler?.UnregisterHandler<SpaceStateMessage>();
                clientHandler?.UnregisterHandler<CyberStateMessage>();
                clientHandler?.UnregisterHandler<SofStateMessage>();
                clientHandler?.UnregisterHandler<OpsStateMessage>();
                clientHandler?.UnregisterHandler<FrontStateMessage>();
                clientHandler = network.Client.MessageHandler;
                manager?.OnSpaceLinked(); // a fresh link: nothing from an earlier session may be shown
                clientHandler.RegisterHandler<SupportResultMessage>(ReceiveResult, false);
                clientHandler.RegisterHandler<CruiseLegsMessage>(ReceiveCruiseLegs, false);
                clientHandler.RegisterHandler<SpaceReplyMessage>(ReceiveSpaceReply, false);
                clientHandler.RegisterHandler<SpaceStateMessage>(ReceiveSpaceState, false);
                clientHandler.RegisterHandler<CyberStateMessage>(ReceiveCyberState, false);
                clientHandler.RegisterHandler<SofStateMessage>(ReceiveSofState, false);
                clientHandler.RegisterHandler<OpsStateMessage>(ReceiveOpsState, false);
                clientHandler.RegisterHandler<FrontStateMessage>(ReceiveFrontState, false);
            }
        }

        private void OnDestroy()
        {
            serverHandler?.UnregisterHandler<SupportRequestMessage>();
            serverHandler?.UnregisterHandler<CruiseWaypointMessage>();
            serverHandler?.UnregisterHandler<SpaceCommandMessage>();
            clientHandler?.UnregisterHandler<SpaceReplyMessage>();
            clientHandler?.UnregisterHandler<SpaceStateMessage>();
            clientHandler?.UnregisterHandler<CyberStateMessage>();
            clientHandler?.UnregisterHandler<SofStateMessage>();
            clientHandler?.UnregisterHandler<OpsStateMessage>();
            clientHandler?.UnregisterHandler<FrontStateMessage>();
            clientHandler?.UnregisterHandler<SupportResultMessage>();
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
                manager.ReceiveResult(Reply(message, result, manager.ServerCooldownFor(local, action), local));
                return;
            }

            NetworkClient client = GameAccess.NetworkManagerOrNull?.Client;
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
                    : result == SupportResult.Cooldown ? manager.ServerCooldownRemaining(player, action) : 0f,
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
            sender.Send(Reply(request, result, manager.ServerCooldownFor(player, (SupportActionId)request.Action), player));
        }

        private void ReceiveResult(INetworkPlayer _, SupportResultMessage result)
        {
            if (result.Protocol == ProtocolVersion) manager.ReceiveResult(result);
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
            NetworkClient client = GameAccess.NetworkManagerOrNull?.Client;
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

        /// <summary>
        /// Server to one faction member (CYBER, SOF, OPERATIONS): the caller selects members by their faction and builds the view per member; nothing is broadcast.
        /// The host's own player is handed the data in-process (the mirror copies what it keeps).
        /// </summary>
        internal void SendFactionState<TData, TMsg>(Player player, TData data, MirrorFeed<TData> feed, TMsg message) where TData : FactionStateData<TData>, new()
        {
            if (player == null || data == null) return;
            if (GameAccess.IsServer() && GameManager.GetLocalPlayer<Player>(out Player local) && ReferenceEquals(local, player))
            {
                feed.Receive(data);
                return;
            }
            player.Owner?.Send(message);
        }

        private void ReceiveOpsState(INetworkPlayer _, OpsStateMessage message) => manager.OpsFeed.Receive(message.Data);

        private void ReceiveFrontState(INetworkPlayer _, FrontStateMessage message) => manager.FrontFeed.Receive(message.Data);

        private void ReceiveSofState(INetworkPlayer _, SofStateMessage message) => manager.SofFeed.Receive(message.Data);

        private void ReceiveCyberState(INetworkPlayer _, CyberStateMessage message) => manager.CyberFeed.Receive(message.Data);

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

        public void BroadcastCruiseLegs(CruiseLegsMessage message)
        {
            if (!GameAccess.IsServer()) return;
            NetworkServer server = GameAccess.NetworkManagerOrNull?.Server;
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
            SetStateCodec<SpaceStateMessage, SpaceStateData>(v => v.Data, d => new SpaceStateMessage { Data = d }, () => new SpaceStateData { Protocol = ProtocolVersion },
                SpaceWire.WriteState, SpaceWire.ReadState);
            SetStateCodec<CyberStateMessage, CyberStateData>(v => v.Data, d => new CyberStateMessage { Data = d }, () => new CyberStateData { Protocol = ProtocolVersion },
                CyberWire.WriteState, CyberWire.ReadState);
            SetStateCodec<SofStateMessage, SofStateData>(v => v.Data, d => new SofStateMessage { Data = d }, () => new SofStateData { Protocol = ProtocolVersion },
                SofWire.WriteState, SofWire.ReadState);
            SetStateCodec<OpsStateMessage, OpsStateData>(v => v.Data, d => new OpsStateMessage { Data = d }, () => new OpsStateData { Protocol = ProtocolVersion },
                OpsWire.WriteState, OpsWire.ReadState);
            SetStateCodec<FrontStateMessage, FrontStateData>(v => v.Data, d => new FrontStateMessage { Data = d }, () => new FrontStateData { Protocol = ProtocolVersion },
                FrontWire.WriteState, FrontWire.ReadState);
            MessagePacker.RegisterMessage<SupportRequestMessage>();
            MessagePacker.RegisterMessage<SupportResultMessage>();
            MessagePacker.RegisterMessage<CruiseWaypointMessage>();
            MessagePacker.RegisterMessage<CruiseLegsMessage>();
            MessagePacker.RegisterMessage<SpaceCommandMessage>();
            MessagePacker.RegisterMessage<SpaceReplyMessage>();
            MessagePacker.RegisterMessage<SpaceStateMessage>();
            MessagePacker.RegisterMessage<CyberStateMessage>();
            MessagePacker.RegisterMessage<SofStateMessage>();
            MessagePacker.RegisterMessage<OpsStateMessage>();
            MessagePacker.RegisterMessage<FrontStateMessage>();
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

        // The four state messages (SPACE, CYBER, SOF, OPS) are one codec: an inert value stands in for a null write, and a malformed read is inert, never a crash.
        private static void SetStateCodec<TMsg, TData>(Func<TMsg, TData> data, Func<TData, TMsg> wrap, Func<TData> inert,
                                                       Action<ISpaceWriter, TData> write, Func<ISpaceReader, byte, TData> read) where TData : class
        {
            SetWriter<TMsg>((w, v) =>
            {
                WireOut.W = w;
                try { write(WireOut, data(v) ?? inert()); }
                finally { WireOut.W = null; }
            });
            SetReader<TMsg>(r =>
            {
                WireIn.R = r;
                try { return wrap(read(WireIn, ProtocolVersion)); }
                catch (Exception) { return default; }
                finally { WireIn.R = null; }
            });
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
