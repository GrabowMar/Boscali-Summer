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
        private float nextPoll, nextWarning;

        public SpaceNetHost(SupportManager manager, SpaceService space, SupportNet net)
        {
            this.manager = manager; this.space = space; this.net = net;
            commands = new SpaceCommandHost(SupportNet.ProtocolVersion, this);
            subs = new SpaceSubscriptions(SupportNet.ProtocolVersion);
        }

        public SpaceCommandHost Commands => commands;
        public SpaceSubscriptions Subscriptions => subs;

        public void ResetForScene()
        {
            commands.ResetForScene();
            subs.Clear();
            nextPoll = 0f;
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
                switch (command.Kind)
                {
                    case SpaceCommandKind.OpenFeed:
                        if (commands.Admit(id, wall) && subs.OpenFeed(id, now)) Poll(player, now);
                        break;
                    case SpaceCommandKind.FeedActivity:
                        // Keeps the lease only. Effort still comes from the ordinary input pulse, never from this message.
                        if (commands.Admit(id, wall)) subs.Touch(id, now);
                        break;
                    case SpaceCommandKind.CloseFeed:
                        if (commands.Admit(id, wall)) { subs.CloseFeed(id); Poll(player, now); }
                        break;
                    default:
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
                for (int i = 0; i < players.Count; i++) Poll(players[i], now);
            }
            subs.EndRound();
        }

        private void Poll(Player player, float now)
        {
            if (player == null || player.HQ == null) return;
            ulong id = PlayerIdentity.Of(player);
            if (id == PlayerIdentity.None) return;
            subs.Keep(id);
            if (!subs.Due(id, now)) return;
            try
            {
                bool feeding = subs.IsFeeding(id, now);
                space.FillFeed(player, feeding, scratch, reveals, marks, posts); // fills a not-Active, rowless view when the faction has no SPACE
                SpaceStateData message = subs.Next(id, manager.FactionKeyOf(player.HQ), scratch, now);
                if (message != null) net.SendSpaceState(player, message);
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
