using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Networking;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The SPACE network seam. Host: labels, gates and the baseline price the mirror publishes. Every peer: the client mirror of
    /// the local player's own faction (headline always, rows while the feed is open) and the request API the feed (Task 8) calls.
    /// SPACE request ids are their own counter: they can never collide with CALLS request ids.
    /// </summary>
    internal sealed partial class SupportManager
    {
        private const float FeedKeepSeconds = 8f, ResyncSeconds = 1f;

        private readonly SpaceFeedMirror spaceMirror = new SpaceFeedMirror();
        private SpaceNetHost spaceNet;
        private readonly SpaceReplyQueue inProcessReplies = new SpaceReplyQueue();
        // SPACE request ids are monotonic within a session but start at a random 31-bit value, so a restarted or rejoining client
        // can never reuse an id the host still remembers for the same identity.
        private int spaceRequestId = NewSessionSeed(), mirrorFaction;
        private bool spaceFeedWanted;
        private float nextFeedKeep, nextResync;

        private static int NewSessionSeed() => new System.Random(Guid.NewGuid().GetHashCode()).Next(1, int.MaxValue / 2);

        internal SpaceNetHost SpaceNet => spaceNet;
        /// <summary>The local player's faction view as the host last told it (family, uplinks, live MARKs; rows while the feed is open).</summary>
        internal SpaceFeedMirror SpaceMirror => spaceMirror;
        /// <summary>The feed has asked for rows and has not closed (the manager keeps the lease alive and resyncs).</summary>
        internal bool SpaceFeedWanted => spaceFeedWanted;
        /// <summary>A host verdict for a MARK, SEND or CLAIM, including the late push of a queued claim.</summary>
        internal event Action<SpaceReply> SpaceReplied;

        // ---- Host helpers ---------------------------------------------------------------------

        internal int FactionKeyOf(FactionHQ hq) => hq == null ? 0 : hq.GetInstanceID();

        internal Player FindTaskedPlayer(FactionHQ owner, ulong id) => FindPlayer(owner, id);

        /// <summary>A pilot's callsign for the CLAIMED BY words: printable ASCII, bounded, empty when unknown.</summary>
        internal string PlayerLabel(FactionHQ owner, ulong id)
        {
            if (id == SpaceContacts.WatchOfficerId) return "OVERLORD";
            Player player = FindPlayer(owner, id);
            if (player == null) return "";
            try { return SpaceWire.Clean(player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard), SpaceReply.MaxClaimant); }
            catch (Exception) { return ""; }
        }

        /// <summary>Why this viewer cannot claim a TASKED call right now (None when they can), for the feed's gate line.</summary>
        internal TaskedOutcome TaskedGate(Player viewer, out int detail)
        {
            detail = 0;
            if (viewer == null || viewer.HQ == null) return TaskedOutcome.Unavailable;
            return AuthorizeTasked(viewer.HQ, PlayerIdentity.Of(viewer), SupportActionId.Artillery, true, out _, out _, out detail);
        }

        /// <summary>The host-priced STANDARD rod for this viewer; <paramref name="charge"/> is false when the host waives charging.</summary>
        internal int TaskedBaseline(Player viewer, out bool charge)
        {
            charge = !BypassRequirements;
            SupportActionDefinition definition = catalog?.Find(SupportActionId.Artillery);
            return definition == null || viewer == null ? 0 : Math.Max(0, QuoteFor(definition, viewer).Cost);
        }

        // ---- Family for prices and the sky ----------------------------------------------------

        /// <summary>Whether this faction has the given bird (host: its SPACE state; client: the mirror, own faction only).</summary>
        internal bool HasSpaceBird(FactionHQ owner, BirdKind bird)
        {
            if (owner == null) return false;
            if (GameAccess.IsServer()) return TryGetSpaceStateCoarse(owner, out SpaceState state) && state.HasBird(bird);
            return spaceMirror.Known && spaceMirror.State.Active && GameManager.GetLocalPlayer<Player>(out Player local) &&
                local != null && ReferenceEquals(local.HQ, owner) && (byte)bird < SpaceRules.BirdCount && opsMirror.BirdUp(bird);
        }

        /// <summary>
        /// The faction's SPACE family. The host reads its own state; a client reads the mirror (and only for its own faction), so a
        /// client's degraded +40 % quote and the satellite sky match what the host charges and shows.
        /// </summary>
        internal bool TryGetSpaceFamily(FactionHQ owner, out SpaceFamilyState family)
        {
            family = SpaceFamilyState.Dark;
            if (owner == null) return false;
            if (GameAccess.IsServer())
            {
                if (!TryGetSpaceStateCoarse(owner, out SpaceState state)) return false;
                family = state.Family(MissionNow());
                return true;
            }
            if (!spaceMirror.Known || !spaceMirror.State.Active || !GameManager.GetLocalPlayer<Player>(out Player local) ||
                local == null || !ReferenceEquals(local.HQ, owner)) return false;
            family = spaceMirror.State.Family;
            return true;
        }

        // ---- Client receive -------------------------------------------------------------------

        internal void ReceiveSpaceState(SpaceStateData data)
        {
            if (data == null || data.Protocol != SupportNet.ProtocolVersion) return;
            float now = MissionNow();
            if (!spaceMirror.Apply(data, SupportNet.ProtocolVersion, now) && logger != null && data.Generation > spaceMirror.Generation)
                logger.LogWarning("[Support.Space] Ignored a SPACE update the mirror could not place (generation " + data.Generation + ").");
        }

        internal void ReceiveSpaceReply(SpaceReply reply)
        {
            if (reply.Protocol != SupportNet.ProtocolVersion || reply.Kind == SpaceCommandKind.None) return;
            try { SpaceReplied?.Invoke(reply); }
            catch (Exception e) { logger?.LogError(e); }
        }

        /// <summary>
        /// In-process verdict (singleplayer, listen-host): queued and delivered on the next frame, never from inside the request
        /// call, because the requester only learns its request id when the call returns.
        /// </summary>
        internal void QueueSpaceReply(SpaceReply reply)
        {
            if (!inProcessReplies.Enqueue(reply)) logger?.LogWarning("[Support.Space] In-process reply queue full; a verdict was dropped.");
        }

        /// <summary>A fresh client link: nothing from an earlier session may be shown or restored, not even the generation floor.</summary>
        internal void OnSpaceLinked()
        {
            spaceMirror.ResetLink();
            cyberMirror.ResetLink();
            sofMirror.ResetLink();
            opsMirror.ResetLink();
            frontMirror.ResetLink();
            ResetSpaceMirror();
        }

        private void ResetSpaceMirror()
        {
            // Faction and scene resets keep the mirror's generation floor: an in-flight full of the old faction stays refused.
            spaceMirror.Reset();
            cyberMirror.Reset();
            sofMirror.Reset();
            opsMirror.Reset();
            frontMirror.Reset();
            inProcessReplies.Clear();
            spaceFeedWanted = false;
            mirrorFaction = 0;
            nextFeedKeep = nextResync = 0f;
        }

        private void UpdateSpaceMirror()
        {
            while (inProcessReplies.TryDequeue(out SpaceReply queued)) ReceiveSpaceReply(queued);
            if (GameManager.GetLocalPlayer<Player>(out Player local) && local != null && local.HQ != null)
            {
                int key = FactionKeyOf(local.HQ);
                if (key != mirrorFaction)
                {
                    // Faction change: the old faction's rows must never outlive the switch.
                    if (mirrorFaction != 0) { spaceMirror.Reset(); cyberMirror.Reset(); sofMirror.Reset(); opsMirror.Reset(); frontMirror.Reset(); }
                    mirrorFaction = key;
                }
            }
            if (network == null) return;
            CyberFeed.Update(network);
            SofFeed.Update(network);
            OpsFeed.Update(network);
            FrontFeed.Want(true); // readiness gates every perk tile, so the client always wants its faction's fronts
            FrontFeed.Update(network);
            Visuals.OpsFlightVisuals.Tick(opsMirror, MissionNow());
            Visuals.FrontEffectVisuals.Tick(frontMirror, MissionNow());
            float t = Time.unscaledTime;
            if (spaceMirror.NeedsFull && t >= nextResync)
            {
                // A rejected delta: ask for a fresh full. FeedActivity from a closed feed means exactly that to the host.
                nextResync = t + ResyncSeconds;
                if (spaceFeedWanted) nextFeedKeep = t + FeedKeepSeconds;
                network.RequestSpace(new SpaceCommand(SupportNet.ProtocolVersion,
                    spaceFeedWanted ? SpaceCommandKind.OpenFeed : SpaceCommandKind.FeedActivity, 0));
                return;
            }
            if (!spaceFeedWanted) return;
            bool rowsMissing = spaceMirror.Known && spaceMirror.State.Active && !spaceMirror.State.Feed;
            if (rowsMissing && t >= nextResync)
            {
                nextResync = t + ResyncSeconds; nextFeedKeep = t + FeedKeepSeconds;
                network.RequestSpace(new SpaceCommand(SupportNet.ProtocolVersion, SpaceCommandKind.OpenFeed, 0));
            }
            else if (t >= nextFeedKeep)
            {
                nextFeedKeep = t + FeedKeepSeconds;
                network.RequestSpace(new SpaceCommand(SupportNet.ProtocolVersion, SpaceCommandKind.FeedActivity, 0));
            }
        }

        // ---- Client requests (Task 8 calls these) ---------------------------------------------

        private int NextSpaceRequestId() => spaceRequestId = spaceRequestId == int.MaxValue ? 1 : spaceRequestId + 1;

        /// <summary>Asks the host for rows. The host derives faction and entitlement from the sender; there is nothing to assert.</summary>
        internal void SpaceOpenFeed()
        {
            spaceFeedWanted = true;
            nextFeedKeep = Time.unscaledTime + FeedKeepSeconds;
            network?.RequestSpace(new SpaceCommand(SupportNet.ProtocolVersion, SpaceCommandKind.OpenFeed, 0));
        }

        internal void SpaceCloseFeed()
        {
            spaceFeedWanted = false;
            network?.RequestSpace(new SpaceCommand(SupportNet.ProtocolVersion, SpaceCommandKind.CloseFeed, 0));
        }

        /// <summary>Real operator input on an open feed: keeps the lease. Rate limited by the host as well.</summary>
        internal void SpaceFeedActivity()
        {
            if (!spaceFeedWanted) return;
            nextFeedKeep = Time.unscaledTime + FeedKeepSeconds;
            network?.RequestSpace(new SpaceCommand(SupportNet.ProtocolVersion, SpaceCommandKind.FeedActivity, 0));
        }

        /// <summary>MARK + CONFIRM of an opaque faction contact id taken from the mirror. Returns the request id (0 when not sent).</summary>
        internal int SpaceMark(int contactId) =>
            SendSpace(SpaceCommandKind.Mark, contactId, null);

        /// <summary>Posts the listed MARK ids (at most six) as one TASKED call. Returns the request id (0 when not sent).</summary>
        internal int SpaceSend(int[] markIds) =>
            markIds == null || markIds.Length == 0 || markIds.Length > SpaceCommand.MaxIds ? 0 : SendSpace(SpaceCommandKind.SendTasked, 0, markIds);

        /// <summary>Claims a posted TASKED call by its id. No aim, price or priority is sent. Returns the request id (0 when not sent).</summary>
        internal int SpaceClaim(int postId) =>
            SendSpace(SpaceCommandKind.ClaimTasked, postId, null);

        private int SendSpace(SpaceCommandKind kind, int target, int[] ids)
        {
            if (network == null) return 0;
            int id = NextSpaceRequestId();
            return network.RequestSpace(new SpaceCommand(SupportNet.ProtocolVersion, kind, id, target, ids)) ? id : 0;
        }
    }
}
