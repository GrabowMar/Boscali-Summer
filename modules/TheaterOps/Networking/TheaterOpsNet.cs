using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Features.TheaterOps.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.TheaterOps.Networking
{
    /// <summary>Client intent: send me the host's theatre priorities. Answered privately.</summary>
    [NetworkMessage]
    internal struct TheaterPriorityQuery
    {
        public byte Protocol;
    }

    /// <summary>
    /// One faction's main effort, or its clear (Active 0). The host sends one per change and
    /// answers a query with one per set faction, so the wire shape carries no list to grow.
    /// </summary>
    [NetworkMessage]
    internal struct TheaterPriorityState
    {
        public byte Protocol;
        public byte Active;
        public string Faction;
        public string Key;
        public string Label;
        public float X;
        public float Y;
        public float Z;
    }

    /// <summary>
    /// One faction's offensive board: Count 0 clears it, otherwise this message carries the
    /// plan at Index and Index 0 resets the client's copy first. The host sends one message
    /// per plan it currently holds, so the wire carries no list that can grow unbounded.
    /// </summary>
    [NetworkMessage]
    internal struct TheaterOperationState
    {
        public byte Protocol;
        public byte Count;
        public byte Index;
        public string Faction;
        public byte Phase;
        public byte Outcome;
        public string Name;
        public string Target;
        public float Progress;
        public float Budget;
        public float Committed;
        public float Spent;
        public float Duration;
        public float Countdown;
        public int WavesPlanned;
        public int WavesLaunched;

        /// <summary>What holds the effort when the plan is due at H-hour but cannot launch.</summary>
        public string Holder;
    }

    /// <summary>
    /// Client intent: one standing order for the director. The faction and the setter are
    /// never on the wire — the host derives both from the sender — so a forged message
    /// can neither move another faction's war nor sign another name.
    /// </summary>
    [NetworkMessage]
    internal struct TheaterInfluenceIntent
    {
        public byte Protocol;
        public byte Kind;
        public float Value;
        public float Value2;
        public string Key;
    }

    /// <summary>
    /// One faction's director snapshot: its standing orders, posture and effort. Fixed
    /// fields (four axis pairs, empty when absent) so the wire carries no list to grow.
    /// </summary>
    [NetworkMessage]
    internal struct TheaterDirectorState
    {
        public byte Protocol;
        public string Faction;
        public float Stance;
        public byte Hold;
        public float MaxEscrow;
        public float Reserve;
        public string Setter;
        public string AxisKey0;
        public string AxisKey1;
        public string AxisKey2;
        public string AxisKey3;
        public float AxisWeight0;
        public float AxisWeight1;
        public float AxisWeight2;
        public float AxisWeight3;
        public byte Posture;
        public byte EffortDefense;
        public string DefenseLabel;
        public byte ActivePlans;
    }

    /// <summary>
    /// One faction's staff log, newest first. Eight fixed lines, empty when absent.
    /// </summary>
    [NetworkMessage]
    internal struct TheaterDirectorLog
    {
        public byte Protocol;
        public string Faction;
        public string Line0;
        public string Line1;
        public string Line2;
        public string Line3;
        public string Line4;
        public string Line5;
        public string Line6;
        public string Line7;
    }

    /// <summary>
    /// Replicates the host's theater war read-only — main effort, offensive board, director
    /// posture, influence and staff log — and carries faction members' standing orders back.
    /// </summary>
    internal sealed class TheaterOpsNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 3;

        internal const byte InfluenceStance = 0;
        internal const byte InfluenceHold = 1;
        internal const byte InfluenceChest = 2;
        internal const byte InfluenceAxis = 3;
        private const int MaximumEntries = 8;
        private const int MaximumFactionLength = TheaterPriorityService.MaximumFactionLength;
        private const int MaximumKeyLength = PriorityDirective.MaximumKeyLength;
        private const int MaximumLabelLength = PriorityDirective.MaximumLabelLength;
        private const int MaximumOperationNameLength = OffensivePlan.MaximumNameLength;
        private const int MaximumTargetLabelLength = OffensivePlan.MaximumTargetLabelLength;
        private const int MaximumSetterLength = InfluenceState.MaximumSetterLength;
        private const int MaximumLogLineLength = StaffLog.MaximumTextLength;
        private const int MaximumQueries = 64;
        private const float QueryInterval = 1f;
        private const float ClientQueryInterval = 4f;

        private TheaterPriorityService service;
        private TheaterOperationsService operationService;
        private TheaterDirectorService directorService;
        private MessageHandler serverHandler, clientHandler;
        private readonly Dictionary<ulong, float> nextQuery = new Dictionary<ulong, float>(16);
        private readonly Dictionary<ulong, float> nextIntent = new Dictionary<ulong, float>(16);
        private readonly List<ulong> expired = new List<ulong>(16);
        private readonly List<KeyValuePair<string, PriorityDirective>> buffer =
            new List<KeyValuePair<string, PriorityDirective>>(MaximumEntries);
        private readonly List<OffensivePlan> operations =
            new List<OffensivePlan>(OffensiveTable.MaximumOperations);

        private float nextRegistration, nextPrune, lastClientQuery;
        private bool queried;

        public void Configure(
            TheaterPriorityService owner, TheaterOperationsService operationOwner,
            TheaterDirectorService directorOwner)
        {
            service = owner;
            operationService = operationOwner;
            directorService = directorOwner;
            InstallSerializers();
        }

        /// <summary>Drops the transport and asks again on the next scene.</summary>
        public void ResetScene()
        {
            serverHandler?.UnregisterHandler<TheaterPriorityQuery>();
            serverHandler?.UnregisterHandler<TheaterInfluenceIntent>();
            clientHandler?.UnregisterHandler<TheaterPriorityState>();
            clientHandler?.UnregisterHandler<TheaterOperationState>();
            clientHandler?.UnregisterHandler<TheaterDirectorState>();
            clientHandler?.UnregisterHandler<TheaterDirectorLog>();
            serverHandler = null;
            clientHandler = null;
            queried = false;
            nextRegistration = 0f;
            lastClientQuery = -10f;
            nextQuery.Clear();
            nextIntent.Clear();
            expired.Clear();
            operations.Clear();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now < nextRegistration) return;
            nextRegistration = now + 0.5f;

            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            MessageHandler server = network?.Server?.Active == true ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.Active == true ? network.Client.MessageHandler : null;

            if (server != serverHandler)
            {
                serverHandler?.UnregisterHandler<TheaterPriorityQuery>();
                serverHandler?.UnregisterHandler<TheaterInfluenceIntent>();
                serverHandler = server;
                serverHandler?.RegisterHandler<TheaterPriorityQuery>(ReceiveQuery, false);
                serverHandler?.RegisterHandler<TheaterInfluenceIntent>(ReceiveInfluenceIntent, false);
            }
            if (client != clientHandler)
            {
                clientHandler?.UnregisterHandler<TheaterPriorityState>();
                clientHandler?.UnregisterHandler<TheaterOperationState>();
                clientHandler?.UnregisterHandler<TheaterDirectorState>();
                clientHandler?.UnregisterHandler<TheaterDirectorLog>();
                clientHandler = client;
                queried = false;
                clientHandler?.RegisterHandler<TheaterPriorityState>(ReceiveState, false);
                clientHandler?.RegisterHandler<TheaterOperationState>(ReceiveOperation, false);
                clientHandler?.RegisterHandler<TheaterDirectorState>(ReceiveDirectorState, false);
                clientHandler?.RegisterHandler<TheaterDirectorLog>(ReceiveDirectorLog, false);
            }

            // One query per connection. Silence is a valid answer (the host has nothing set),
            // so there is no retry storm; a reconnect re-registers the client handler and asks
            // again, and a change after that is broadcast.
            if (clientHandler != null && !queried && !GameAccess.IsServer() &&
                now - lastClientQuery >= ClientQueryInterval)
            {
                NetworkClient transport = NetworkManagerNuclearOption.i?.Client;
                if (transport != null && transport.Active)
                {
                    queried = true;
                    lastClientQuery = now;
                    transport.Send(new TheaterPriorityQuery { Protocol = ProtocolVersion });
                }
            }

            if (now < nextPrune) return;
            nextPrune = now + 10f;
            expired.Clear();
            foreach (KeyValuePair<ulong, float> pair in nextQuery)
                if (now - pair.Value > 30f) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++) nextQuery.Remove(expired[i]);
            expired.Clear();
            foreach (KeyValuePair<ulong, float> pair in nextIntent)
                if (now - pair.Value > 30f) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++) nextIntent.Remove(expired[i]);
        }

        /// <summary>Host broadcast: one faction set, replaced or cleared.</summary>
        internal void BroadcastState(string faction, PriorityDirective? directive)
        {
            if (!GameAccess.IsServer()) return;
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(StateOf(faction, directive), authenticatedOnly: true, excludeLocalPlayer: true);
        }

        /// <summary>
        /// Host broadcast: the faction's offensive board, one message per plan, or one clear
        /// message when the board emptied.
        /// </summary>
        internal void BroadcastOperations(string faction, FactionOffensives offensives)
        {
            if (!GameAccess.IsServer()) return;
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;

            int count = offensives != null ? offensives.Count : 0;
            if (count == 0)
            {
                server.SendToAll(OperationOf(faction, 0, 0, null),
                    authenticatedOnly: true, excludeLocalPlayer: true);
                return;
            }

            for (int i = 0; i < count; i++)
                server.SendToAll(OperationOf(faction, (byte)count, (byte)i, offensives[i]),
                    authenticatedOnly: true, excludeLocalPlayer: true);
        }

        /// <summary>Client send: one standing order for the director. The host validates all of it.</summary>
        internal void SendInfluenceIntent(byte kind, float value, float value2, string key)
        {
            if (GameAccess.IsServer()) return;
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active) return;
            client.Send(new TheaterInfluenceIntent
            {
                Protocol = ProtocolVersion,
                Kind = kind,
                Value = value,
                Value2 = value2,
                Key = Text(key, MaximumKeyLength),
            });
        }

        /// <summary>Host broadcast: one faction's director snapshot and staff log.</summary>
        internal void BroadcastDirector(
            string faction, TheaterInfluenceView influence, TheaterDirectorPosture posture,
            bool effortDefense, string defenseLabel, int activePlans)
        {
            if (!GameAccess.IsServer()) return;
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(
                DirectorStateOf(faction, influence, posture, effortDefense, defenseLabel, activePlans),
                authenticatedOnly: true, excludeLocalPlayer: true);
        }

        internal void BroadcastDirectorLog(string faction, StaffLog log)
        {
            if (!GameAccess.IsServer()) return;
            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            server.SendToAll(DirectorLogOf(faction, log),
                authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void ReceiveQuery(INetworkPlayer sender, TheaterPriorityQuery query)
        {
            if (!GameAccess.IsServer() || query.Protocol != ProtocolVersion || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null ||
                !RateLimit(player))
                return;

            int count = service.CopyDirectives(buffer);
            for (int i = 0; i < count; i++)
                sender.Send(StateOf(buffer[i].Key, buffer[i].Value));

            // The offensive board and the director are the querying player's own faction's business.
            if (player.HQ != null && player.HQ.faction != null)
            {
                SendOperations(sender, player.HQ.faction.factionName);
                SendDirector(sender, player.HQ.faction.factionName);
            }
        }

        private void ReceiveInfluenceIntent(INetworkPlayer sender, TheaterInfluenceIntent intent)
        {
            if (!GameAccess.IsServer() || intent.Protocol != ProtocolVersion || sender == null ||
                !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null ||
                player.HQ == null || player.HQ.faction == null || directorService == null ||
                !RateLimitIntent(player))
                return;

            string setter;
            try
            {
                setter = player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard);
            }
            catch (Exception)
            {
                setter = null;
            }
            directorService.ApplyInfluence(
                player.HQ.faction.factionName, intent.Kind, intent.Value, intent.Value2,
                Text(intent.Key, MaximumKeyLength), setter);
        }

        private void ReceiveDirectorState(INetworkPlayer _, TheaterDirectorState state)
        {
            if (GameAccess.IsServer() || state.Protocol != ProtocolVersion) return;

            string faction = Text(state.Faction, MaximumFactionLength);
            if (string.IsNullOrEmpty(faction)) return;

            operationService?.ApplyRemoteDirector(faction, state);
        }

        private void ReceiveDirectorLog(INetworkPlayer _, TheaterDirectorLog log)
        {
            if (GameAccess.IsServer() || log.Protocol != ProtocolVersion) return;

            string faction = Text(log.Faction, MaximumFactionLength);
            if (string.IsNullOrEmpty(faction)) return;

            operationService?.ApplyRemoteDirectorLog(faction, new[]
            {
                log.Line0, log.Line1, log.Line2, log.Line3,
                log.Line4, log.Line5, log.Line6, log.Line7,
            });
        }

        private void SendOperations(INetworkPlayer player, string faction)
        {
            int count = operationService != null
                ? operationService.CopyOperations(faction, operations)
                : 0;
            if (count == 0)
            {
                player.Send(OperationOf(faction, 0, 0, null));
                return;
            }
            for (int i = 0; i < count; i++)
                player.Send(OperationOf(faction, (byte)count, (byte)i, operations[i]));
        }

        private void SendDirector(INetworkPlayer player, string faction)
        {
            if (directorService == null) return;
            TheaterDirectionView direction = directorService.DirectionOf(faction);
            TheaterInfluenceView influence = directorService.InfluenceOf(faction);
            player.Send(DirectorStateOf(
                faction, influence, direction.Posture, direction.EffortIsDefense,
                direction.DefenseLabel, direction.ActivePlans));
            var lines = new List<string>(StaffLog.MaximumEntries);
            directorService.CopyStaffLog(faction, lines);
            player.Send(DirectorLogOf(faction, lines));
        }

        private void ReceiveState(INetworkPlayer _, TheaterPriorityState state)
        {
            if (GameAccess.IsServer() || state.Protocol != ProtocolVersion) return;

            string faction = Text(state.Faction, MaximumFactionLength);
            if (string.IsNullOrEmpty(faction)) return;

            if (state.Active == 0)
            {
                service.ApplyRemoteClear(faction);
                return;
            }

            service.ApplyRemote(
                faction, Text(state.Key, MaximumKeyLength), Text(state.Label, MaximumLabelLength),
                state.X, state.Y, state.Z);
        }

        private void ReceiveOperation(INetworkPlayer _, TheaterOperationState state)
        {
            if (GameAccess.IsServer() || state.Protocol != ProtocolVersion) return;

            string faction = Text(state.Faction, MaximumFactionLength);
            if (string.IsNullOrEmpty(faction)) return;

            operationService?.ApplyRemoteOperation(faction, state.Count, state.Index, state);
        }

        private static TheaterPriorityState StateOf(string faction, PriorityDirective? directive)
        {
            if (!directive.HasValue)
                return new TheaterPriorityState { Protocol = ProtocolVersion, Faction = Text(faction, MaximumFactionLength) };

            PriorityDirective value = directive.Value;
            return new TheaterPriorityState
            {
                Protocol = ProtocolVersion,
                Active = 1,
                Faction = Text(faction, MaximumFactionLength),
                Key = Text(value.Key, MaximumKeyLength),
                Label = Text(value.Label, MaximumLabelLength),
                X = value.X,
                Y = value.Y,
                Z = value.Z,
            };
        }

        private TheaterOperationState OperationOf(
            string faction, byte count, byte index, OffensivePlan plan)
        {
            var state = new TheaterOperationState
            {
                Protocol = ProtocolVersion,
                Count = count,
                Index = index,
                Faction = Text(faction, MaximumFactionLength),
            };
            if (plan == null) return state;

            state.Phase = (byte)plan.Phase;
            state.Outcome = (byte)plan.Outcome;
            state.Name = Text(plan.Name, MaximumOperationNameLength);
            state.Target = Text(plan.TargetLabel, MaximumTargetLabelLength);
            state.Progress = plan.Progress;
            state.Budget = plan.Budget;
            state.Committed = plan.Committed;
            state.Spent = plan.Spent;
            state.Duration = plan.ElapsedSeconds;
            state.Countdown = plan.Countdown;
            state.WavesPlanned = plan.WavesPlanned;
            state.WavesLaunched = plan.WavesLaunched;
            state.Holder = Text(
                operationService != null ? operationService.OperationHolder(faction, plan) : null,
                MaximumOperationNameLength);
            return state;
        }

        private bool RateLimit(Player player)
        {
            float now = Time.unscaledTime;
            ulong id = PlayerIdentity.Of(player);
            if (nextQuery.TryGetValue(id, out float next) && now < next) return false;
            if (!nextQuery.ContainsKey(id) && nextQuery.Count >= MaximumQueries) return false;
            nextQuery[id] = now + QueryInterval;
            return true;
        }

        private bool RateLimitIntent(Player player)
        {
            float now = Time.unscaledTime;
            ulong id = PlayerIdentity.Of(player);
            if (nextIntent.TryGetValue(id, out float next) && now < next) return false;
            if (!nextIntent.ContainsKey(id) && nextIntent.Count >= MaximumQueries) return false;
            nextIntent[id] = now + QueryInterval;
            return true;
        }

        private static TheaterDirectorState DirectorStateOf(
            string faction, TheaterInfluenceView influence, TheaterDirectorPosture posture,
            bool effortDefense, string defenseLabel, int activePlans)
        {
            var state = new TheaterDirectorState
            {
                Protocol = ProtocolVersion,
                Faction = Text(faction, MaximumFactionLength),
                Stance = influence != null ? influence.Stance : InfluenceState.DefaultStance,
                Hold = influence != null && influence.HoldOffense ? (byte)1 : (byte)0,
                MaxEscrow = influence != null ? influence.MaxEscrowPerPlan : InfluenceState.DefaultMaxEscrow,
                Reserve = influence != null ? influence.ReserveFloor : 0f,
                Setter = Text(influence != null ? influence.Setter : null, MaximumSetterLength),
                Posture = (byte)posture,
                EffortDefense = effortDefense ? (byte)1 : (byte)0,
                DefenseLabel = Text(defenseLabel, MaximumLabelLength),
                ActivePlans = (byte)Mathf.Clamp(activePlans, 0, OffensiveTable.MaximumOperations),
            };
            if (influence == null || influence.Axes == null) return state;
            for (int i = 0; i < influence.Axes.Count && i < InfluenceState.MaximumAxes; i++)
            {
                TheaterAxisView axis = influence.Axes[i];
                if (axis == null || string.IsNullOrEmpty(axis.Key)) continue;
                string key = Text(axis.Key, MaximumKeyLength);
                switch (i)
                {
                    case 0: state.AxisKey0 = key; state.AxisWeight0 = axis.Weight; break;
                    case 1: state.AxisKey1 = key; state.AxisWeight1 = axis.Weight; break;
                    case 2: state.AxisKey2 = key; state.AxisWeight2 = axis.Weight; break;
                    case 3: state.AxisKey3 = key; state.AxisWeight3 = axis.Weight; break;
                }
            }
            return state;
        }

        private static TheaterDirectorLog DirectorLogOf(string faction, StaffLog log)
        {
            var lines = new List<string>(StaffLog.MaximumEntries);
            if (log != null)
                for (int i = 0; i < log.Entries.Count && i < StaffLog.MaximumEntries; i++)
                    lines.Add(log.Entries[i]);
            return DirectorLogOf(faction, lines);
        }

        private static TheaterDirectorLog DirectorLogOf(string faction, List<string> lines)
        {
            var log = new TheaterDirectorLog
            {
                Protocol = ProtocolVersion,
                Faction = Text(faction, MaximumFactionLength),
            };
            if (lines == null) return log;
            for (int i = 0; i < lines.Count && i < StaffLog.MaximumEntries; i++)
            {
                string line = Text(lines[i], MaximumLogLineLength);
                switch (i)
                {
                    case 0: log.Line0 = line; break;
                    case 1: log.Line1 = line; break;
                    case 2: log.Line2 = line; break;
                    case 3: log.Line3 = line; break;
                    case 4: log.Line4 = line; break;
                    case 5: log.Line5 = line; break;
                    case 6: log.Line6 = line; break;
                    case 7: log.Line7 = line; break;
                }
            }
            return log;
        }

        private void OnDestroy()
        {
            serverHandler?.UnregisterHandler<TheaterPriorityQuery>();
            serverHandler?.UnregisterHandler<TheaterInfluenceIntent>();
            clientHandler?.UnregisterHandler<TheaterPriorityState>();
            clientHandler?.UnregisterHandler<TheaterOperationState>();
            clientHandler?.UnregisterHandler<TheaterDirectorState>();
            clientHandler?.UnregisterHandler<TheaterDirectorLog>();
        }

        internal static string Text(string value, int max) => string.IsNullOrEmpty(value) ? ""
            : value.Length > max ? value.Substring(0, max) : value;

        private static void InstallSerializers()
        {
            Bind(typeof(Writer<TheaterPriorityQuery>), "Write", (Action<NetworkWriter, TheaterPriorityQuery>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
            }));
            Bind(typeof(Reader<TheaterPriorityQuery>), "Read", (Func<NetworkReader, TheaterPriorityQuery>)(r =>
            {
                byte protocol = r.ReadByte();
                return new TheaterPriorityQuery { Protocol = protocol };
            }));

            Bind(typeof(Writer<TheaterPriorityState>), "Write", (Action<NetworkWriter, TheaterPriorityState>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteByte(v.Active);
                w.WriteString(Text(v.Faction, MaximumFactionLength));
                if (v.Active == 0) return;
                w.WriteString(Text(v.Key, MaximumKeyLength));
                w.WriteString(Text(v.Label, MaximumLabelLength));
                w.WriteSingle(v.X);
                w.WriteSingle(v.Y);
                w.WriteSingle(v.Z);
            }));
            Bind(typeof(Reader<TheaterPriorityState>), "Read", (Func<NetworkReader, TheaterPriorityState>)(r =>
            {
                byte protocol = r.ReadByte();
                var state = new TheaterPriorityState { Protocol = protocol };
                if (protocol != ProtocolVersion) return state;

                state.Active = r.ReadByte();
                state.Faction = r.ReadString();
                if (state.Active == 0) return state;
                state.Key = r.ReadString();
                state.Label = r.ReadString();
                state.X = r.ReadSingle();
                state.Y = r.ReadSingle();
                state.Z = r.ReadSingle();
                return state;
            }));

            Bind(typeof(Writer<TheaterOperationState>), "Write", (Action<NetworkWriter, TheaterOperationState>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteByte(v.Count);
                w.WriteByte(v.Index);
                w.WriteString(Text(v.Faction, MaximumFactionLength));
                if (v.Count == 0) return;
                w.WriteByte(v.Phase);
                w.WriteByte(v.Outcome);
                w.WriteString(Text(v.Name, MaximumOperationNameLength));
                w.WriteString(Text(v.Target, MaximumTargetLabelLength));
                w.WriteSingle(v.Progress);
                w.WriteSingle(v.Budget);
                w.WriteSingle(v.Committed);
                w.WriteSingle(v.Spent);
                w.WriteSingle(v.Duration);
                w.WriteSingle(v.Countdown);
                w.WriteInt32(v.WavesPlanned);
                w.WriteInt32(v.WavesLaunched);
                w.WriteString(Text(v.Holder, MaximumOperationNameLength));
            }));
            Bind(typeof(Reader<TheaterOperationState>), "Read", (Func<NetworkReader, TheaterOperationState>)(r =>
            {
                byte protocol = r.ReadByte();
                var state = new TheaterOperationState { Protocol = protocol };
                if (protocol != ProtocolVersion) return state;

                state.Count = r.ReadByte();
                state.Index = r.ReadByte();
                state.Faction = r.ReadString();
                if (state.Count == 0) return state;
                state.Phase = r.ReadByte();
                state.Outcome = r.ReadByte();
                state.Name = r.ReadString();
                state.Target = r.ReadString();
                state.Progress = r.ReadSingle();
                state.Budget = r.ReadSingle();
                state.Committed = r.ReadSingle();
                state.Spent = r.ReadSingle();
                state.Duration = r.ReadSingle();
                state.Countdown = r.ReadSingle();
                state.WavesPlanned = r.ReadInt32();
                state.WavesLaunched = r.ReadInt32();
                state.Holder = r.ReadString();
                return state;
            }));

            Bind(typeof(Writer<TheaterInfluenceIntent>), "Write", (Action<NetworkWriter, TheaterInfluenceIntent>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteByte(v.Kind);
                w.WriteSingle(v.Value);
                w.WriteSingle(v.Value2);
                w.WriteString(Text(v.Key, MaximumKeyLength));
            }));
            Bind(typeof(Reader<TheaterInfluenceIntent>), "Read", (Func<NetworkReader, TheaterInfluenceIntent>)(r =>
            {
                byte protocol = r.ReadByte();
                var intent = new TheaterInfluenceIntent { Protocol = protocol };
                if (protocol != ProtocolVersion) return intent;

                intent.Kind = r.ReadByte();
                intent.Value = r.ReadSingle();
                intent.Value2 = r.ReadSingle();
                intent.Key = r.ReadString();
                return intent;
            }));

            Bind(typeof(Writer<TheaterDirectorState>), "Write", (Action<NetworkWriter, TheaterDirectorState>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteString(Text(v.Faction, MaximumFactionLength));
                w.WriteSingle(v.Stance);
                w.WriteByte(v.Hold);
                w.WriteSingle(v.MaxEscrow);
                w.WriteSingle(v.Reserve);
                w.WriteString(Text(v.Setter, MaximumSetterLength));
                w.WriteString(Text(v.AxisKey0, MaximumKeyLength));
                w.WriteString(Text(v.AxisKey1, MaximumKeyLength));
                w.WriteString(Text(v.AxisKey2, MaximumKeyLength));
                w.WriteString(Text(v.AxisKey3, MaximumKeyLength));
                w.WriteSingle(v.AxisWeight0);
                w.WriteSingle(v.AxisWeight1);
                w.WriteSingle(v.AxisWeight2);
                w.WriteSingle(v.AxisWeight3);
                w.WriteByte(v.Posture);
                w.WriteByte(v.EffortDefense);
                w.WriteString(Text(v.DefenseLabel, MaximumLabelLength));
                w.WriteByte(v.ActivePlans);
            }));
            Bind(typeof(Reader<TheaterDirectorState>), "Read", (Func<NetworkReader, TheaterDirectorState>)(r =>
            {
                byte protocol = r.ReadByte();
                var state = new TheaterDirectorState { Protocol = protocol };
                if (protocol != ProtocolVersion) return state;

                state.Faction = r.ReadString();
                state.Stance = r.ReadSingle();
                state.Hold = r.ReadByte();
                state.MaxEscrow = r.ReadSingle();
                state.Reserve = r.ReadSingle();
                state.Setter = r.ReadString();
                state.AxisKey0 = r.ReadString();
                state.AxisKey1 = r.ReadString();
                state.AxisKey2 = r.ReadString();
                state.AxisKey3 = r.ReadString();
                state.AxisWeight0 = r.ReadSingle();
                state.AxisWeight1 = r.ReadSingle();
                state.AxisWeight2 = r.ReadSingle();
                state.AxisWeight3 = r.ReadSingle();
                state.Posture = r.ReadByte();
                state.EffortDefense = r.ReadByte();
                state.DefenseLabel = r.ReadString();
                state.ActivePlans = r.ReadByte();
                return state;
            }));

            Bind(typeof(Writer<TheaterDirectorLog>), "Write", (Action<NetworkWriter, TheaterDirectorLog>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteString(Text(v.Faction, MaximumFactionLength));
                w.WriteString(Text(v.Line0, MaximumLogLineLength));
                w.WriteString(Text(v.Line1, MaximumLogLineLength));
                w.WriteString(Text(v.Line2, MaximumLogLineLength));
                w.WriteString(Text(v.Line3, MaximumLogLineLength));
                w.WriteString(Text(v.Line4, MaximumLogLineLength));
                w.WriteString(Text(v.Line5, MaximumLogLineLength));
                w.WriteString(Text(v.Line6, MaximumLogLineLength));
                w.WriteString(Text(v.Line7, MaximumLogLineLength));
            }));
            Bind(typeof(Reader<TheaterDirectorLog>), "Read", (Func<NetworkReader, TheaterDirectorLog>)(r =>
            {
                byte protocol = r.ReadByte();
                var log = new TheaterDirectorLog { Protocol = protocol };
                if (protocol != ProtocolVersion) return log;

                log.Faction = r.ReadString();
                log.Line0 = r.ReadString();
                log.Line1 = r.ReadString();
                log.Line2 = r.ReadString();
                log.Line3 = r.ReadString();
                log.Line4 = r.ReadString();
                log.Line5 = r.ReadString();
                log.Line6 = r.ReadString();
                log.Line7 = r.ReadString();
                return log;
            }));

            MessagePacker.RegisterMessage<TheaterPriorityQuery>();
            MessagePacker.RegisterMessage<TheaterPriorityState>();
            MessagePacker.RegisterMessage<TheaterOperationState>();
            MessagePacker.RegisterMessage<TheaterInfluenceIntent>();
            MessagePacker.RegisterMessage<TheaterDirectorState>();
            MessagePacker.RegisterMessage<TheaterDirectorLog>();
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(
                property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                Plugin.Logger.LogError(
                    "[TheaterOps] Mirage serializer seam " + holder.Name + "." + property +
                    " is missing; theater priorities cannot replicate on this game build.");
                return;
            }
            target.SetValue(null, value, null);
        }
    }
}
