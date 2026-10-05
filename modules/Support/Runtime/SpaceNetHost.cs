using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Networking;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Host side of the faction-only SPACE mirror. Every command (remote sender, listen-host and singleplayer alike) arrives with
    /// the <see cref="Player"/> the transport authenticated; its faction and identity are read from that player, never from the
    /// message. Replies and state go only to members of the faction that owns the data, and every faction member hears the
    /// small headline (family, uplinks, live MARKs) so client prices and the sky match the host without opening the feed.
    /// </summary>
    internal sealed class SpaceNetHost : ISpaceCommandPorts
    {
        private const float PollWallSeconds = .25f;

        private readonly SupportManager manager;
        private readonly SpaceService space;
        private readonly SupportNet net;
        private readonly SpaceCommandHost commands;
        private readonly SpaceSubscriptions subs;
        private readonly SpaceFeedState scratch = new SpaceFeedState();
        private readonly List<SpaceContact> reveals = new List<SpaceContact>(SpaceContacts.MaxReveals);
        private readonly List<SpaceMark> marks = new List<SpaceMark>(SpaceContacts.MaxMarks);
        private readonly List<TaskedPostInfo> posts = new List<TaskedPostInfo>(TaskedBoard.MaxCalls);
        private Player current;
        private FactionHQ resolving;
        private readonly List<Player> roster = new List<Player>(SpaceContacts.MaxPlayers);
        private float nextPoll, nextWarning;

        public SpaceNetHost(SupportManager manager, SpaceService space, SupportNet net)
        {
            this.manager = manager; this.space = space; this.net = net;
            commands = new SpaceCommandHost(SupportNet.ProtocolVersion, this);
            subs = new SpaceSubscriptions(SupportNet.ProtocolVersion);
            Salt = NewSalt();
        }

        /// <summary>64 bits from the operating system CSPRNG.</summary>
        private static ulong NewSalt()
        {
            var bytes = new byte[8];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        /// <summary>A per-mission secret that never leaves the host: it salts the noise in the probable class shown before a verdict.</summary>
        public ulong Salt { get; private set; }

        public SpaceCommandHost Commands => commands;
        public SpaceSubscriptions Subscriptions => subs;

        public void ResetForScene()
        {
            commands.ResetForScene();
            subs.Clear();
            nextPoll = 0f;
            Salt = NewSalt();
        }

        // ---- Commands ---------------------------------------------------------------------------

        /// <summary>The one entry for a SPACE command, from the wire or in-process. Host only.</summary>
        public void Receive(Player player, in SpaceCommand command)
        {
            if (!GameAccess.IsServer() || player == null || player.HQ == null || command.Protocol != SupportNet.ProtocolVersion ||
                command.Kind == SpaceCommandKind.None) return;
            ulong id = PlayerIdentity.Of(player);
            if (id == PlayerIdentity.None) return;
            float wall = Time.unscaledTime;
            float now = SupportManager.MissionNow();
            current = player; resolving = null;
            try
            {
                // A fresh join (a new link, or a restarted client whose request ids start over) never inherits an old identity's
                // receipts: they were answers to a different session.
                if (subs.Ensure(id)) commands.Cache.Forget(id);
                switch (command.Kind)
                {
                    case SpaceCommandKind.OpenFeed:
                        // The feed lease and the poll throttle run on wall time; the message timestamp is mission time.
                        if (commands.Admit(id, wall) && subs.OpenFeed(id, wall)) Poll(player, now, wall);
                        break;
                    case SpaceCommandKind.FeedActivity:
                        // Keeps the lease only. Effort still comes from the ordinary input pulse, never from this message.
                        if (commands.Admit(id, wall)) subs.Touch(id, wall);
                        break;
                    case SpaceCommandKind.CloseFeed:
                        if (commands.Admit(id, wall)) { subs.CloseFeed(id); Poll(player, now, wall); }
                        break;
                    default:
                        // MARK, SEND and CLAIM are the SPACE domain verbs: a human doing one is working SPACE, so OVERLORD steps back.
                        space?.NoteHumanSpaceVerb(player);
                        if (commands.Handle(id, command, space != null ? space.Generation : 0, wall, out SpaceReply reply))
                            net.SendSpaceReply(player, reply);
                        break;
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[Support.Space] Command " + command.Kind + " failed: " + e.Message); }
            finally { current = null; }
        }

        /// <summary>A queued claim reached its verdict: tell its pilot (cached for replays) with the holder's callsign for losers.</summary>
        public void OnTaskedResolved(FactionHQ owner, ulong playerId, int requestId, TaskedResult result)
        {
            Player player = manager.FindTaskedPlayer(owner, playerId);
            resolving = owner;
            try
            {
                SpaceReply reply = commands.Resolved(playerId, space != null ? space.Generation : 0, requestId, result);
                if (player != null) net.SendSpaceReply(player, reply);
            }
            finally { resolving = null; }
        }

        // ---- Mirror push ------------------------------------------------------------------------

        public void Tick()
        {
            float wall = Time.unscaledTime;
            if (wall < nextPoll) return;
            nextPoll = wall + PollWallSeconds;
            float now = SupportManager.MissionNow();
            if (!SpaceRules.MissionTime(now)) return;
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return;
            subs.BeginRound();
            foreach (FactionHQ hq in hqs)
            {
                List<Player> players = hq != null ? hq.GetPlayers(false) : null;
                if (players == null) continue;
                // GetPlayers hands out a shared list that nested calls refill: poll a private copy.
                roster.Clear();
                for (int i = 0; i < players.Count && roster.Count < roster.Capacity; i++) roster.Add(players[i]);
                for (int i = 0; i < roster.Count; i++) Poll(roster[i], now, wall);
            }
            subs.EndRound();
        }

        private void Poll(Player player, float now, float wall)
        {
            if (player == null || player.HQ == null) return;
            ulong id = PlayerIdentity.Of(player);
            if (id == PlayerIdentity.None) return;
            if (subs.Ensure(id)) commands.Cache.Forget(id); // first sight of this identity this session
            subs.Keep(id);
            if (!subs.Due(id, wall)) return;
            try
            {
                bool feeding = subs.IsFeeding(id, wall);
                space.FillFeed(player, feeding, scratch, reveals, marks, posts); // fills a not-Active, rowless view when the faction has no SPACE
                SpaceStateData message = subs.Next(id, manager.FactionKeyOf(player.HQ), scratch, now, wall);
                // A message that did not fit one writer buffer carries its TASKED rows in a follow-up, sent right after.
                for (SpaceStateData part = message; part != null; part = part.Follow) net.SendSpaceState(player, part);
            }
            catch (Exception e)
            {
                // One member with a dead connection must not stop the mirror for everyone else.
                if (Time.unscaledTime >= nextWarning)
                {
                    nextWarning = Time.unscaledTime + 10f;
                    Plugin.Logger?.LogWarning("[Support.Space] Mirror send failed: " + e.Message);
                }
            }
        }

        // ---- ISpaceCommandPorts (host decisions; the sender is `current`) -----------------------

        public float Now => SupportManager.MissionNow();

        public MarkVerdict Mark(ulong player, int contactId) =>
            Is(player) ? manager.ConfirmSpaceMark(current, contactId) : MarkVerdict.NoContact;

        public TaskedResult Send(ulong player, int requestId, int[] markIds) =>
            Is(player) ? manager.SendTasked(current, markIds, requestId) : new TaskedResult(TaskedOutcome.Unavailable, 0, requestId);

        // No favourite flag: the host holds no knowledge of a pilot's CALLS favourites (they live in the client's own settings),
        // so a client can never assert priority. Arbitration is receipt order inside the 200 ms window.
        public TaskedResult Claim(ulong player, int requestId, int postId) =>
            Is(player) ? manager.ClaimTasked(current, postId, requestId, false) : new TaskedResult(TaskedOutcome.Unavailable, postId, requestId);

        public bool TryTaskedResult(ulong player, int requestId, out TaskedResult result)
        {
            result = default;
            return Is(player) && manager.TryTaskedResult(current, requestId, out result);
        }

        public string Claimant(int callId)
        {
            FactionHQ owner = resolving != null ? resolving : current?.HQ;
            return owner != null && space.TryHolder(owner, callId, out ulong pilot) ? manager.PlayerLabel(owner, pilot) : "";
        }

        private bool Is(ulong player) => current != null && PlayerIdentity.Of(current) == player;
    }
}
