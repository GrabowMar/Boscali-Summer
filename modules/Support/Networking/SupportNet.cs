using System;
using System.Reflection;
using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Domain.SpecOps;
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
        /// Protocol 8 added program investment and EW posture. Protocol 9 replaces
        /// station-keeping satellites with orbital elements (payload, regime, seed, mission
        /// clock, battery, magazine) plus undisclosed foreign satellites. Protocol 10 adds the
        /// base-of-operations ranks (fortification doctrine and insertion rigging) to the
        /// snapshot and the upgrade command. Protocol 11 replaces the satellites with the
        /// modular orbital station. Protocol 12 replaces the single EW truck with the CYBER
        /// network (sites, incidents, notices, origin names) and its site and console
        /// commands. Protocol 13 raises the network to sixteen slots and adds the static and
        /// down site flags for the airbase infrastructure. Protocol 14 adds intrusion and field
        /// operations; protocol 15 adds tactical board state and expected command revisions.
        /// Protocol 16 removes the truck network: nodes carry a stage and a capstone, the
        /// faction carries computing and intel, and the snapshot carries the live breach.
        /// Protocol 18 replaces moving pass seeds with fixed station-sector routes (origin * 9 + destination).
        /// Protocol 19 extends the SPEC OPS snapshot with three ability recharges and the STEAL mission.
        /// Command 9 carries the selected sector in Arg. Protocol 17 replaced the programs, reserves, doctrine and infiltration board with the
        /// SPEC OPS detachment (teams, objectives, recharges, notices) in its own state message,
        /// sent before each ops snapshot, and its three orders.
        /// Older peers must not interpret fleet, hack, team or node ids.
        /// </summary>
        internal const byte ProtocolVersion = 19;

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
                clientHandler?.UnregisterHandler<SpecOpsStateMessage>();
                clientHandler?.UnregisterHandler<CyberEffectMessage>();
                clientHandler = network.Client.MessageHandler;
                clientHandler.RegisterHandler<SupportResultMessage>(ReceiveResult, false);
                clientHandler.RegisterHandler<OpsStateMessage>(ReceiveOpsState, false);
                clientHandler.RegisterHandler<SpecOpsStateMessage>(ReceiveSpecOpsState, false);
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
            clientHandler?.UnregisterHandler<SpecOpsStateMessage>();
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
            return new SupportResultMessage {
                Protocol = ProtocolVersion, RequestId = request.RequestId, Action = request.Action,
                Result = (byte)result, CooldownSeconds = result == SupportResult.Accepted ? cooldown : 0f,
                Radius = radius, X = target.x, Y = target.y, Z = target.z,
                Contacts = contacts < 0 ? 0 : contacts,
                Duration = action == SupportActionId.Emp ? SupportEffectPolicy.EmpDuration :
                    action == SupportActionId.FlareMissile ? manager.Settings.FlareBarrageDuration.Value :
                    manager.FieldEffectDuration(action, player != null ? player.HQ : null)
            };
        }

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

        // ---- Station and infrastructure ----------------------------------------------------

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

        public void Command(int requestId, OpsCommand command, byte arg, byte arg2, GlobalPosition target, uint revision = 0)
        {
            var message = new OpsCommandMessage
            {
                Protocol = ProtocolVersion,
                RequestId = requestId,
                Command = (byte)command,
                Arg = arg,
                Arg2 = arg2,
                X = target.x,
                Z = target.z,
                Revision = revision
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
            sender.Send(manager.SpecOpsSnapshot(player));
            sender.Send(manager.Snapshot(player, 0, SupportResult.None));
        }

        private void ReceiveOpsCommand(INetworkPlayer sender, OpsCommandMessage command)
        {
            if (command.Protocol != ProtocolVersion) return;
            if (sender == null || !sender.IsAuthenticated ||
                !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            // Like ordinary support requests, orders use the manager's bounded attempt ledger
            // and always receive an acknowledgement, including rate denials. Polls cannot
            // consume their slot and leave a valid client pending until timeout.
            SupportResult result = manager.EvaluateCommand(player, command);
            sender.Send(manager.SpecOpsSnapshot(player));
            sender.Send(manager.Snapshot(player, command.RequestId, result));
        }

        private void ReceiveOpsState(INetworkPlayer _, OpsStateMessage state)
        {
            if (state.Protocol == ProtocolVersion) manager.ReceiveOps(state);
        }

        private void ReceiveSpecOpsState(INetworkPlayer _, SpecOpsStateMessage state)
        {
            if (state.Protocol == ProtocolVersion) manager.ReceiveSpecOps(state);
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
                w.WriteUInt32(v.Revision);
            });
            SetReader<OpsCommandMessage>(r =>
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return new OpsCommandMessage { Protocol = protocol };
                return new OpsCommandMessage
                {
                    Protocol = protocol,
                    RequestId = r.ReadPackedInt32(),
                    Command = r.ReadByte(),
                    Arg = r.ReadByte(),
                    Arg2 = r.ReadByte(),
                    X = r.ReadSingle(),
                    Z = r.ReadSingle(),
                    Revision = r.ReadUInt32()
                };
            });
            SetWriter<OpsStateMessage>((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WritePackedInt32(v.RequestId);
                w.WriteByte(v.Result);
                bool active = v.PlatformActive && OpsStateMessageBuffers.ValidArrays(v);
                w.WriteByte(active ? (byte)1 : (byte)0);
                if (active)
                {
                    for (int i = 0; i < OrbitalPlatform.CellCount; i++) w.WriteByte(v.PlatformModules[i]);
                    for (int i = 0; i < OrbitalPlatform.CellCount; i++) w.WriteByte(v.PlatformOffline[i]);
                    w.WriteByte(v.PlatformRegime);
                    w.WritePackedInt32(v.PlatformSeed);
                    w.WriteSingle(v.PlatformClock);
                    w.WriteByte(v.PlatformHold);
                    w.WriteSingle(v.PlatformEnergy);
                    w.WriteSingle(v.PlatformFuel);
                    w.WriteByte(v.PlatformRods);
                    w.WriteByte(v.PlatformBrownout ? (byte)1 : (byte)0);
                    w.WriteByte(v.PlatformPending);
                    w.WriteByte(v.PlatformPendingCell);
                    w.WriteSingle(v.PlatformDockIn);
                    for (int i = 0; i < PlatformAbilities.Count; i++) w.WriteSingle(v.PlatformRecharge[i]);
                    w.WriteSingle(v.PlatformElapsed);
                    w.WriteByte(v.PlatformNotice);
                    w.WriteByte(v.PlatformNoticeCell);
                    w.WriteByte(v.PlatformNoticeSerial);
                }
                int foreignCount = Math.Max(0, Math.Min((int)v.ForeignCount, Math.Min(SpaceOperations.MaximumForeign,
                    Math.Min(v.ForeignRegimes?.Length ?? 0, Math.Min(v.ForeignSeeds?.Length ?? 0,
                        Math.Min(v.ForeignClocks?.Length ?? 0, v.ForeignLayouts?.Length ?? 0))))));
                w.WriteByte((byte)foreignCount);
                for (int i = 0; i < foreignCount; i++)
                {
                    w.WriteByte(v.ForeignRegimes[i]);
                    w.WritePackedInt32(v.ForeignSeeds[i]);
                    w.WriteSingle(v.ForeignClocks[i]);
                    w.WritePackedInt32(v.ForeignLayouts[i]);
                }
                WriteCyber(w, v.Cyber, v.CyberOriginCount, v.CyberOrigins);
            });
            SetReader<OpsStateMessage>(r =>
            {
                byte protocol = r.ReadByte();
                var message = OpsStateMessageBuffers.Create();
                message.Protocol = protocol;
                if (protocol != ProtocolVersion) return message;
                message.RequestId = r.ReadPackedInt32();
                message.Result = r.ReadByte();
                // A flag or count past its bound is a malformed or hostile message: stop reading
                // rather than consume bytes that belong to later fields.
                int active = r.ReadByte();
                if (active > 1) return new OpsStateMessage { Protocol = 0 };
                message.PlatformActive = active == 1;
                if (message.PlatformActive)
                {
                    for (int i = 0; i < OrbitalPlatform.CellCount; i++) message.PlatformModules[i] = r.ReadByte();
                    for (int i = 0; i < OrbitalPlatform.CellCount; i++) message.PlatformOffline[i] = r.ReadByte();
                    message.PlatformRegime = r.ReadByte();
                    message.PlatformSeed = r.ReadPackedInt32();
                    message.PlatformClock = r.ReadSingle();
                    message.PlatformHold = r.ReadByte();
                    message.PlatformEnergy = r.ReadSingle();
                    message.PlatformFuel = r.ReadSingle();
                    message.PlatformRods = r.ReadByte();
                    message.PlatformBrownout = r.ReadByte() != 0;
                    message.PlatformPending = r.ReadByte();
                    message.PlatformPendingCell = r.ReadByte();
                    message.PlatformDockIn = r.ReadSingle();
                    for (int i = 0; i < PlatformAbilities.Count; i++) message.PlatformRecharge[i] = r.ReadSingle();
                    message.PlatformElapsed = r.ReadSingle();
                    message.PlatformNotice = r.ReadByte();
                    message.PlatformNoticeCell = r.ReadByte();
                    message.PlatformNoticeSerial = r.ReadByte();
                }
                int foreignCount = r.ReadByte();
                if (foreignCount > SpaceOperations.MaximumForeign) return new OpsStateMessage { Protocol = 0 };
                message.ForeignCount = (byte)foreignCount;
                for (int i = 0; i < foreignCount; i++)
                {
                    message.ForeignRegimes[i] = r.ReadByte();
                    message.ForeignSeeds[i] = r.ReadPackedInt32();
                    message.ForeignClocks[i] = r.ReadSingle();
                    message.ForeignLayouts[i] = r.ReadPackedInt32();
                }
                if (!ReadCyber(r, ref message)) return new OpsStateMessage { Protocol = 0 };
                return message;
            });
            SetWriter<SpecOpsStateMessage>((w, v) =>
            {
                w.WriteByte(v.Protocol);
                if (v.Protocol == ProtocolVersion) WriteSpecOps(w, v.State);
            });
            SetReader<SpecOpsStateMessage>(r =>
            {
                byte protocol = r.ReadByte();
                var message = new SpecOpsStateMessage { Protocol = protocol, State = new SpecOpsSnapshot() };
                if (protocol != ProtocolVersion) return message;
                if (!ReadSpecOps(r, message.State)) message.Protocol = 0;
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
            MessagePacker.RegisterMessage<SpecOpsStateMessage>();
            MessagePacker.RegisterMessage<CyberEffectMessage>();
            MessagePacker.RegisterMessage<SupportRequestMessage>();
            MessagePacker.RegisterMessage<SupportResultMessage>();
        }

        private static readonly CyberSnapshot EmptyCyber = new CyberSnapshot();
        private static readonly SpecOpsSnapshot EmptySpecOps = new SpecOpsSnapshot();

        /// <summary>The SPEC OPS block: four teams, the objective list, two recharges, the notice
        /// ring. Counts clamp on write; names clip to the detachment's name length.</summary>
        private static void WriteSpecOps(NetworkWriter w, SpecOpsSnapshot s)
        {
            s = s ?? EmptySpecOps;
            w.WriteByte(s.Flags);
            for (int i = 0; i < SpecOpsDetachment.TeamCount; i++)
            {
                w.WriteByte(s.TeamState[i]);
                w.WriteByte(s.TeamRank[i]);
                w.WriteByte(s.TeamWins[i]);
                w.WriteByte(s.TeamMission[i]);
                w.WriteByte(s.TeamChance[i]);
                w.WriteByte(s.TeamLoss[i]);
                w.WriteByte(s.TeamLast[i]);
                w.WritePackedInt32(s.TeamAnchor[i]);
                w.WriteSingle(s.TeamX[i]);
                w.WriteSingle(s.TeamZ[i]);
                w.WriteSingle(s.TeamRemaining[i]);
                w.WriteSingle(s.TeamDuration[i]);
                w.WriteString(SpecOpsDetachment.Clip(s.TeamTarget[i]));
            }
            int objectives = Math.Min((int)s.ObjectiveCount, SpecOpsDetachment.ObjectiveSlots);
            w.WriteByte((byte)objectives);
            for (int i = 0; i < objectives; i++)
            {
                w.WriteByte(s.ObjectiveKind[i]);
                w.WritePackedInt32(s.ObjectiveAnchor[i]);
                w.WriteSingle(s.ObjectiveX[i]);
                w.WriteSingle(s.ObjectiveZ[i]);
                w.WriteByte(s.ObjectiveThreat[i]);
                w.WriteByte(s.ObjectiveRadars[i]);
                w.WriteByte(s.ObjectiveHostile[i] ? (byte)1 : (byte)0);
                w.WriteSingle(s.ObjectiveScout[i]);
                w.WriteString(SpecOpsDetachment.Clip(s.ObjectiveName[i]));
            }
            for (int a = 0; a < FieldCatalog.AbilityCount; a++) w.WriteSingle(s.AbilityRecharge[a]);
            w.WritePackedInt32(s.NoticeSerial);
            int notices = Math.Min((int)s.NoticeCount, SpecOpsDetachment.NoticeSlots);
            w.WriteByte((byte)notices);
            for (int i = 0; i < notices; i++)
            {
                w.WriteByte(s.NoticeKind[i]);
                w.WriteByte(s.NoticeTeam[i]);
                w.WriteByte(s.NoticeMission[i]);
            }
        }

        /// <summary>False for a count past its bound; values themselves clamp in the model's mirror.</summary>
        private static bool ReadSpecOps(NetworkReader r, SpecOpsSnapshot s)
        {
            s.Clear();
            s.Flags = r.ReadByte();
            for (int i = 0; i < SpecOpsDetachment.TeamCount; i++)
            {
                s.TeamState[i] = r.ReadByte();
                s.TeamRank[i] = r.ReadByte();
                s.TeamWins[i] = r.ReadByte();
                s.TeamMission[i] = r.ReadByte();
                s.TeamChance[i] = r.ReadByte();
                s.TeamLoss[i] = r.ReadByte();
                s.TeamLast[i] = r.ReadByte();
                s.TeamAnchor[i] = r.ReadPackedInt32();
                s.TeamX[i] = r.ReadSingle();
                s.TeamZ[i] = r.ReadSingle();
                s.TeamRemaining[i] = r.ReadSingle();
                s.TeamDuration[i] = r.ReadSingle();
                s.TeamTarget[i] = SpecOpsDetachment.Clip(r.ReadString());
            }
            int objectives = r.ReadByte();
            if (objectives > SpecOpsDetachment.ObjectiveSlots) return false;
            s.ObjectiveCount = (byte)objectives;
            for (int i = 0; i < objectives; i++)
            {
                s.ObjectiveKind[i] = r.ReadByte();
                s.ObjectiveAnchor[i] = r.ReadPackedInt32();
                s.ObjectiveX[i] = r.ReadSingle();
                s.ObjectiveZ[i] = r.ReadSingle();
                s.ObjectiveThreat[i] = r.ReadByte();
                s.ObjectiveRadars[i] = r.ReadByte();
                s.ObjectiveHostile[i] = r.ReadByte() != 0;
                s.ObjectiveScout[i] = r.ReadSingle();
                s.ObjectiveName[i] = SpecOpsDetachment.Clip(r.ReadString());
            }
            for (int a = 0; a < FieldCatalog.AbilityCount; a++) s.AbilityRecharge[a] = r.ReadSingle();
            s.NoticeSerial = r.ReadPackedInt32();
            int notices = r.ReadByte();
            if (notices > SpecOpsDetachment.NoticeSlots) return false;
            s.NoticeCount = (byte)notices;
            for (int i = 0; i < notices; i++)
            {
                s.NoticeKind[i] = r.ReadByte();
                s.NoticeTeam[i] = r.ReadByte();
                s.NoticeMission[i] = r.ReadByte();
            }
            return true;
        }

        /// <summary>The CYBER block of a snapshot. Counts are clamped to their bounds on write.</summary>
        private static void WriteCyber(NetworkWriter w, CyberSnapshot c, byte originCount, string[] origins)
        {
            c = c ?? EmptyCyber;
            int nodes = Math.Min((int)c.NodeCount, CyberNetwork.SlotCount);
            w.WriteByte((byte)nodes);
            for (int i = 0; i < nodes; i++)
            {
                w.WriteByte(c.Slot[i]);
                w.WriteByte(c.Kind[i]);
                w.WriteByte(c.Stage[i]);
                w.WriteSingle(c.X[i]);
                w.WriteSingle(c.Z[i]);
                w.WriteByte(c.Flags[i]);
                w.WriteByte(c.Capstone[i]);
                w.WriteByte(c.PatchIn[i]);
                w.WriteByte(c.BaitIn[i]);
                w.WriteByte(c.LockIn[i]);
            }
            w.WriteSingle(c.Computing);
            w.WriteSingle(c.Intel);
            for (int u = 0; u < c.Upgrade.Length; u++) w.WriteByte(c.Upgrade[u]);
            w.WriteByte(c.BreachTarget);
            w.WriteByte(c.BreachPhase);
            w.WriteByte(c.BreachFlags);
            w.WriteSingle(c.BreachTrace);
            w.WriteSingle(c.BreachIn);
            w.WriteSingle(c.SpoofIn);
            w.WritePackedInt32(c.Defended);
            w.WritePackedInt32(c.Breached);            for (int v = 0; v < CyberNetwork.VerbCount; v++) w.WriteSingle(c.Recharge[v]);
            w.WriteByte(c.Heat);
            w.WriteSingle(c.NextIncidentIn);
            w.WriteSingle(c.ExposedIn);

            int incidents = Math.Min((int)c.IncidentCount, CyberNetwork.IncidentSlots);
            w.WriteByte((byte)incidents);
            for (int i = 0; i < incidents; i++)
            {
                w.WriteByte(c.IncidentKind[i]);
                w.WriteByte(c.IncidentState[i]);
                w.WriteByte(c.IncidentSite[i]);
                w.WriteByte(c.IncidentOrigin[i]);
                w.WriteSingle(c.IncidentX[i]);
                w.WriteSingle(c.IncidentZ[i]);
                w.WriteSingle(c.IncidentAge[i]);
                w.WriteSingle(c.IncidentLeft[i]);
                w.WriteByte(c.IncidentTrace[i]);
            }
            for (int f = 0; f < CyberNetwork.MaximumOrigins; f++) w.WriteByte(c.Foothold[f]);

            w.WritePackedInt32(c.NoticeSerial);
            int notices = Math.Min((int)c.NoticeCount, CyberNetwork.NoticeSlots);
            w.WriteByte((byte)notices);
            for (int i = 0; i < notices; i++)
            {
                w.WriteByte(c.NoticeKind[i]);
                w.WriteByte(c.NoticeSite[i]);
                w.WriteByte(c.NoticeOrigin[i]);
            }

            int names = origins == null ? 0
                : Math.Min((int)originCount, Math.Min(origins.Length, OpsStateMessageBuffers.MaximumOriginNames));
            w.WriteByte((byte)names);
            for (int i = 0; i < names; i++)
            {
                string name = origins[i] ?? string.Empty;
                w.WriteString(name.Length > OpsStateMessageBuffers.MaximumOriginLength
                    ? name.Substring(0, OpsStateMessageBuffers.MaximumOriginLength)
                    : name);
            }
        }

        /// <summary>False for a count past its bound: the rest of the message cannot be trusted.</summary>
        private static bool ReadCyber(NetworkReader r, ref OpsStateMessage m)
        {
            CyberSnapshot c = m.Cyber;
            int nodes = r.ReadByte();
            if (nodes > CyberNetwork.SlotCount) return false;
            c.NodeCount = (byte)nodes;
            for (int i = 0; i < nodes; i++)
            {
                c.Slot[i] = r.ReadByte();
                c.Kind[i] = r.ReadByte();
                c.Stage[i] = r.ReadByte();
                c.X[i] = r.ReadSingle();
                c.Z[i] = r.ReadSingle();
                c.Flags[i] = r.ReadByte();
                c.Capstone[i] = r.ReadByte();
                c.PatchIn[i] = r.ReadByte();
                c.BaitIn[i] = r.ReadByte();
                c.LockIn[i] = r.ReadByte();
            }
            c.Computing = r.ReadSingle();
            c.Intel = r.ReadSingle();
            for (int u = 0; u < c.Upgrade.Length; u++) c.Upgrade[u] = r.ReadByte();
            c.BreachTarget = r.ReadByte();
            c.BreachPhase = r.ReadByte();
            c.BreachFlags = r.ReadByte();
            c.BreachTrace = r.ReadSingle();
            c.BreachIn = r.ReadSingle();
            c.SpoofIn = r.ReadSingle();
            c.Defended = r.ReadPackedInt32();
            c.Breached = r.ReadPackedInt32();
            for (int v = 0; v < CyberNetwork.VerbCount; v++) c.Recharge[v] = r.ReadSingle();
            c.Heat = r.ReadByte();
            c.NextIncidentIn = r.ReadSingle();
            c.ExposedIn = r.ReadSingle();

            int incidents = r.ReadByte();
            if (incidents > CyberNetwork.IncidentSlots) return false;
            c.IncidentCount = (byte)incidents;
            for (int i = 0; i < incidents; i++)
            {
                c.IncidentKind[i] = r.ReadByte();
                c.IncidentState[i] = r.ReadByte();
                c.IncidentSite[i] = r.ReadByte();
                c.IncidentOrigin[i] = r.ReadByte();
                c.IncidentX[i] = r.ReadSingle();
                c.IncidentZ[i] = r.ReadSingle();
                c.IncidentAge[i] = r.ReadSingle();
                c.IncidentLeft[i] = r.ReadSingle();
                c.IncidentTrace[i] = r.ReadByte();
            }
            for (int f = 0; f < CyberNetwork.MaximumOrigins; f++) c.Foothold[f] = r.ReadByte();

            c.NoticeSerial = r.ReadPackedInt32();
            int notices = r.ReadByte();
            if (notices > CyberNetwork.NoticeSlots) return false;
            c.NoticeCount = (byte)notices;
            for (int i = 0; i < notices; i++)
            {
                c.NoticeKind[i] = r.ReadByte();
                c.NoticeSite[i] = r.ReadByte();
                c.NoticeOrigin[i] = r.ReadByte();
            }

            int names = r.ReadByte();
            if (names > OpsStateMessageBuffers.MaximumOriginNames) return false;
            m.CyberOriginCount = (byte)names;
            for (int i = 0; i < names; i++)
            {
                string name = r.ReadString() ?? string.Empty;
                m.CyberOrigins[i] = name.Length > OpsStateMessageBuffers.MaximumOriginLength
                    ? name.Substring(0, OpsStateMessageBuffers.MaximumOriginLength)
                    : name;
            }
            return true;
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
