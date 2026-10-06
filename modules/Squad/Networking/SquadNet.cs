using System;
using BoscaliSummer.Modules.Squad.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Net;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Squad.Networking
{
    /// <summary>Revision is the last full snapshot the client applied; zero asks for a full one.</summary>
    [NetworkMessage]
    internal struct SquadQuery { public byte Protocol; public uint Scene, Token, Revision; }

    [NetworkMessage]
    internal struct SquadSnapshot
    {
        public byte Protocol;
        public uint Scene, Token, Event;
        public PilotView Pilot;
        public bool Hunt;
        public int Bonus, Origin, ActiveIndex, HuntId;
        public string Status, Speaker, Chatter;
        public EnemyWingView[] Wings;
        public uint Revision;
        /// <summary>The host's board still matches <see cref="Revision"/>; only the header travels.</summary>
        public bool Unchanged;
    }

    internal sealed class SquadNet : MonoBehaviour
    {
        /// <summary>Version 3 adds the content revision and the header-only unchanged reply.</summary>
        internal const byte ProtocolVersion = 3;
        private const float FastPoll = 1f, IdlePoll = 5f;
        private SquadManager manager;
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<SquadQuery>(ReceiveQuery);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<SquadSnapshot>(ReceiveSnapshot);
        private readonly SenderThrottle nextReply = new SenderThrottle();
        private float nextRegistration, lastQuery = -10f;
        private uint scene, token;
        private bool pending;
        private FactionHQ requestedHq;

        internal void Configure(SquadManager owner) { manager = owner; InstallSerializers(); }
        internal void ResetScene() { ResetClient(); nextReply.Clear(); }

        // Client correlation only. A listen host changing faction must keep the server's reply limits.
        private void ResetClient() { scene++; pending = false; requestedHq = null; lastQuery = -10f; }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (pending && now - lastQuery > 5f)
            { pending = false; manager.ClearLocal("Squad host link unavailable."); }
            if (now < nextRegistration) return;
            nextRegistration = now + 0.5f;
            NetworkManagerNuclearOption network = NetworkManagerNuclearOption.i;
            MessageHandler server = network?.Server?.Active == true ? network.Server.MessageHandler : null;
            MessageHandler client = network?.Client?.Active == true ? network.Client.MessageHandler : null;
            ServerHandlers.Swap(server);
            ClientHandlers.Swap(client);
            nextReply.Prune(now);
        }

        /// <summary>
        /// Polls once a second while a hunt is on or the board is being read, otherwise every
        /// five; an unchanged board is answered with a header only.
        /// </summary>
        internal void Request(bool fast)
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null || player.HQ == null)
            { manager.ClearLocal("Join a faction to receive squad intelligence."); return; }
            if (requestedHq != player.HQ) { ResetClient(); requestedHq = player.HQ; manager.ClearLocal("Receiving squad intelligence."); }
            bool server = GameAccess.IsServer();
            // The host reads its own board in-process, so it keeps the fast cadence for free.
            if (pending || Time.unscaledTime - lastQuery < (fast || server ? FastPoll : IdlePoll)) return;
            lastQuery = Time.unscaledTime;
            if (server)
            {
                SquadSnapshot local = manager.Snapshot(player);
                if (local.Revision != manager.AppliedRevision) manager.Apply(local, PlayerIdentity.Of(player));
                return;
            }
            NetworkClient client = NetworkManagerNuclearOption.i?.Client;
            if (client == null || !client.Active) { manager.ClearLocal("Squad host link unavailable."); return; }
            pending = true;
            client.Send(new SquadQuery { Protocol = ProtocolVersion, Scene = scene, Token = ++token, Revision = manager.AppliedRevision });
        }

        private void ReceiveQuery(INetworkPlayer sender, SquadQuery query)
        {
            if (!GameAccess.IsServer() || !MissionManager.IsRunning || query.Protocol != ProtocolVersion ||
                sender == null || !sender.IsAuthenticated || !sender.TryGetPlayer<Player>(out Player player) || player == null) return;
            ulong id = PlayerIdentity.Of(player); float now = Time.unscaledTime;
            if (!nextReply.Allow(id, now, 0.75f)) return;
            SquadSnapshot snapshot = manager.Snapshot(player);
            if (query.Revision != 0 && query.Revision == snapshot.Revision)
                snapshot = new SquadSnapshot { Protocol = ProtocolVersion, Revision = snapshot.Revision, Unchanged = true };
            snapshot.Scene = query.Scene; snapshot.Token = query.Token; sender.Send(snapshot);
        }

        private void ReceiveSnapshot(INetworkPlayer sender, SquadSnapshot snapshot)
        {
            if (GameAccess.IsServer() || !pending || snapshot.Protocol != ProtocolVersion || snapshot.Scene != scene ||
                snapshot.Token != token || !GameManager.GetLocalPlayer<Player>(out Player local) || local == null || local.HQ != requestedHq) return;
            pending = false;
            // An unchanged reply confirms the board already shown; ClearLocal zeroes the revision, so a
            // cleared board is never "confirmed" and the next query asks for a full snapshot.
            if (!snapshot.Unchanged) manager.Apply(snapshot, PlayerIdentity.Of(local));
        }

        private void OnDestroy()
        { ServerHandlers.Release(); ClientHandlers.Release(); ResetScene(); }

        private static string Text(string text) => NetText.Clip(text, 192);
        private static void WriteText(NetworkWriter w, string text) => w.WriteString(Text(text));
        private static string ReadText(NetworkReader r) => Text(r.ReadString());
        private static int ReadInt(NetworkReader r, int maximum, ref bool valid)
        {
            int value = r.ReadPackedInt32();
            if (value < 0 || value > maximum) valid = false;
            return value;
        }

        /// <summary>
        /// Readers never throw: a throw inside a Mirage handler can drop the connection. A foreign
        /// protocol keeps only its header; data outside its bounds reads as protocol 0, which every
        /// handler ignores.
        /// </summary>
        private static void InstallSerializers()
        {
            MirageSerializers.Strict.Install<SquadQuery>((Action<NetworkWriter, SquadQuery>)((w, v) =>
            { w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token); w.WriteUInt32(v.Revision); }),
            (Func<NetworkReader, SquadQuery>)(r =>
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return new SquadQuery { Protocol = protocol };
                return new SquadQuery { Protocol = protocol, Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32(), Revision = r.ReadUInt32() };
            }));
            MirageSerializers.Strict.Install<SquadSnapshot>((Action<NetworkWriter, SquadSnapshot>)((w, v) =>
            {
                w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Scene); w.WritePackedUInt32(v.Token);
                w.WriteUInt32(v.Revision); w.WriteByte(v.Unchanged ? (byte)1 : (byte)0);
                if (v.Unchanged) return;
                w.WritePackedUInt32(v.Event);
                WriteText(w, v.Pilot.Name); WriteText(w, v.Pilot.Callsign); WriteText(w, v.Pilot.Status); WriteText(w, v.Pilot.Background);
                w.WriteByte(v.Pilot.Respawns ? (byte)1 : (byte)0); w.WritePackedInt32(v.Pilot.Deaths); w.WritePackedInt32(v.Pilot.Generation);
                w.WriteByte(v.Hunt ? (byte)1 : (byte)0); w.WritePackedInt32(v.Bonus); w.WritePackedInt32(v.Origin);
                w.WritePackedInt32(v.ActiveIndex + 1); w.WritePackedInt32(v.HuntId);
                WriteText(w, v.Status); WriteText(w, v.Speaker); WriteText(w, v.Chatter);
                int count = Math.Min(8, v.Wings?.Length ?? 0); w.WriteByte((byte)count);
                for (int i = 0; i < count; i++)
                {
                    EnemyWingView wing = v.Wings[i];
                    WriteText(w, wing.Symbol); WriteText(w, wing.WingName); WriteText(w, wing.AceName);
                    w.WritePackedInt32(wing.Tier); WriteText(w, wing.Skill); WriteText(w, wing.Status);
                    w.WritePackedInt32(wing.MembersAlive); w.WritePackedInt32(wing.MemberCount);
                    WriteText(w, wing.TargetName); w.WritePackedInt32(wing.Returns);
                    w.WritePackedInt32(wing.AbilityMask);
                }
            }),
            (Func<NetworkReader, SquadSnapshot>)(r =>
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return new SquadSnapshot { Protocol = protocol };
                var v = new SquadSnapshot
                {
                    Protocol = protocol, Scene = r.ReadPackedUInt32(), Token = r.ReadPackedUInt32(),
                    Revision = r.ReadUInt32(), Unchanged = r.ReadByte() == 1,
                };
                if (v.Unchanged) return v;
                bool valid = true;
                v.Event = r.ReadPackedUInt32();
                string name = ReadText(r), callsign = ReadText(r), status = ReadText(r), background = ReadText(r);
                bool respawns = r.ReadByte() == 1; int deaths = ReadInt(r, 10000, ref valid), generation = ReadInt(r, 10001, ref valid);
                v.Pilot = new PilotView(name, callsign, status, respawns, deaths, generation, background);
                v.Hunt = r.ReadByte() == 1; v.Bonus = ReadInt(r, 20, ref valid); v.Origin = ReadInt(r, int.MaxValue, ref valid);
                v.ActiveIndex = ReadInt(r, 8, ref valid) - 1; v.HuntId = ReadInt(r, int.MaxValue, ref valid);
                v.Status = ReadText(r); v.Speaker = ReadText(r); v.Chatter = ReadText(r);
                int count = r.ReadByte();
                if (!valid || count > 8 || v.ActiveIndex >= count) return default;
                v.Wings = new EnemyWingView[count];
                for (int i = 0; i < count; i++)
                {
                    string symbol = ReadText(r), wing = ReadText(r), ace = ReadText(r); int tier = ReadInt(r, 5, ref valid);
                    string skill = ReadText(r), state = ReadText(r); int alive = ReadInt(r, 4, ref valid), members = ReadInt(r, 4, ref valid);
                    string target = ReadText(r); int returns = ReadInt(r, 3, ref valid);
                    int abilities = ReadInt(r, 15, ref valid);
                    if (!valid || alive > members) return default;
                    v.Wings[i] = new EnemyWingView(symbol, wing, ace, tier, skill, state, alive, members, target, returns, abilities);
                }
                return v;
            }));
        }
    }
}
