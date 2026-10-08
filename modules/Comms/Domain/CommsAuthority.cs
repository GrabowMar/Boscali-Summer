using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Comms.Domain
{
    /// <summary>
    /// The host's side of COMMS, with no Unity and no transport in it: a peer's request goes
    /// in with the identity the host resolved for it, and routed messages come out. The
    /// transport only has to turn each <see cref="CommsOutbound"/> into sends.
    ///
    /// <para>Everything a peer could lie about is checked here: the verb, every index, every
    /// coordinate, every string, who may erase what,
    /// and how fast. A refused request earns the sender one quiet notice and nothing else.
    /// Team posts only ever route to the poster's side, which is what keeps a SAM ping or
    /// an attack arrow from being intelligence for the other team.</para>
    /// </summary>
    internal sealed class CommsAuthority
    {
        /// <summary>Most map items one snapshot carries.</summary>
        public const int SnapshotItems = 200;

        /// <summary>
        /// One snapshot per player per this many seconds. A snapshot is the one request that
        /// makes the host send far more than it received, so it skips the token bucket (a busy
        /// drawer must still resync after a side change) and is gated on its own instead.
        /// </summary>
        public const float SyncGapSeconds = 5f;

        /// <summary>Highest altitude a located call may claim, in metres.</summary>
        public const int MaxCallAltitude = 60000;

        private readonly CommsBoard board = new CommsBoard();
        private readonly TokenBucket bucket = new TokenBucket(16f, 2.5f);
        private readonly List<uint> scratchIds = new List<uint>();
        private readonly List<CommsItem> scratchItems = new List<CommsItem>();
        private readonly Dictionary<ulong, float> lastSync = new Dictionary<ulong, float>();
        private readonly List<ulong> staleSync = new List<ulong>();
        private uint nextId = 1;

        public CommsRules Rules = CommsRules.Default;

        public CommsBoard Board => board;

        public void Reset()
        {
            board.Clear();
            bucket.Clear();
            lastSync.Clear();
            nextId = 1;
        }

        // ---- Requests ----------------------------------------------------------------------

        public void Handle(CommsSender sender, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            if (output == null) return;
            if (intent.Op == CommsOp.Sync)
            {
                Sync(sender, now, output);
                return;
            }
            if (!bucket.TryTake(sender.Id, now, Cost(intent.Op)))
            {
                Refuse(sender, "SLOW DOWN — TOO MANY COMMS", output, intent.Op == CommsOp.PollVote ? intent.Target : 0u);
                return;
            }

            string name = CommsText.Name(sender.Name);
            switch (intent.Op)
            {
                case CommsOp.Place: Place(sender, name, intent, now, output); break;
                case CommsOp.Erase: Erase(sender, intent.Target, output); break;
                case CommsOp.ClearMine: ClearMine(sender, output); break;
                case CommsOp.ClearAll: ClearAll(sender, name, output); break;
                case CommsOp.Call: Call(sender, name, intent, now, output); break;
                case CommsOp.PollCreate:
                case CommsOp.PollVote:
                case CommsOp.PollClose:
                case CommsOp.Roll:
                case CommsOp.RpsChallenge:
                case CommsOp.RpsAccept:
                case CommsOp.RpsCancel:
                case CommsOp.HuntStart:
                case CommsOp.HuntGuess:
                    Refuse(sender, "POLLS AND GAMES HAVE BEEN REMOVED", output); break;
                default: Refuse(sender, "UNKNOWN COMMS REQUEST", output); break;
            }
        }

        /// <summary>Expire map items; each client also expires them on its own clock.</summary>
        public void Tick(float now, List<CommsOutbound> output)
        {
            board.Expire(now, null);
        }

        /// <summary>A snapshot, at most once per <see cref="SyncGapSeconds"/> per player; too soon earns a quiet retry.</summary>
        private void Sync(CommsSender sender, float now, List<CommsOutbound> output)
        {
            if (lastSync.TryGetValue(sender.Id, out float last) && now >= last && now - last < SyncGapSeconds)
            {
                output.Add(CommsOutbound.To(sender.Id, new CommsEnvelope
                {
                    Event = CommsEvent.Notice,
                    Flags = CommsFlags.Self | CommsFlags.Retry,
                }));
                return;
            }
            if (lastSync.Count >= MaxSyncSenders)
            {
                staleSync.Clear();
                foreach (KeyValuePair<ulong, float> pair in lastSync)
                    if (!(now - pair.Value < SyncGapSeconds)) staleSync.Add(pair.Key);
                for (int i = 0; i < staleSync.Count; i++) lastSync.Remove(staleSync[i]);
                if (lastSync.Count >= MaxSyncSenders) return;
            }
            lastSync[sender.Id] = now;
            Snapshot(sender, now, output);
        }

        private const int MaxSyncSenders = 256;

        /// <summary>
        /// Everything this viewer may see, addressed to them alone, after a reset. Every
        /// annotation in it is flagged as a replay: the viewer has most likely heard it
        /// before, and a side change or reconnect must not ring every notice again.
        /// </summary>
        public void Snapshot(CommsSender viewer, float now, List<CommsOutbound> output)
        {
            output.Add(CommsOutbound.To(viewer.Id, new CommsEnvelope { Event = CommsEvent.Reset, Flags = CommsFlags.Snapshot }));

            // The newest items the viewer may see, bounded so a join never bursts the whole
            // board down one reliable channel, then sent oldest first so UNDO order survives.
            IReadOnlyList<CommsItem> items = board.Items;
            int first = items.Count;
            for (int visible = 0; first > 0 && visible < SnapshotItems; first--)
                if (CommsBoard.Visible(items[first - 1].Channel, items[first - 1].Faction, viewer.Faction)) visible++;
            for (int i = first; i < items.Count; i++)
                if (CommsBoard.Visible(items[i].Channel, items[i].Faction, viewer.Faction))
                    Replay(viewer, ItemEnvelope(items[i], now), output);
        }

        private static void Replay(CommsSender viewer, CommsEnvelope envelope, List<CommsOutbound> output)
        {
            envelope.Flags |= CommsFlags.Replay;
            output.Add(CommsOutbound.To(viewer.Id, envelope));
        }

        // ---- Map ---------------------------------------------------------------------------

        private void Place(CommsSender sender, string name, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            if (intent.Kind == (byte)CommsItemKind.Sticker)
            {
                Refuse(sender, "STICKERS HAVE BEEN REMOVED", output);
                return;
            }

            if (!ChannelAllowed(sender, intent.Channel, output)) return;

            var kind = (CommsItemKind)intent.Kind;
            string text = "";
            switch (kind)
            {
                case CommsItemKind.Ping:
                    if (!CommsCatalog.ValidPing(intent.Style) || !StrokeCodec.Valid(intent.Points, 1, 1))
                    {
                        Refuse(sender, "BAD PING", output);
                        return;
                    }
                    break;
                case CommsItemKind.Label:
                    text = CommsText.Clean(intent.Text, CommsText.MaxLabel);
                    if (text.Length == 0 || !StrokeCodec.Valid(intent.Points, 1, 1))
                    {
                        Refuse(sender, "TYPE A LABEL FIRST", output);
                        return;
                    }
                    break;
                case CommsItemKind.Stroke:
                    if (!Rules.AllowDrawing)
                    {
                        Refuse(sender, "HOST HAS DRAWING OFF", output);
                        return;
                    }
                    if (!CommsCatalog.ValidPen(intent.Style) || !CommsCatalog.ValidWidth(intent.Size) ||
                        !StrokeCodec.Valid(intent.Points, 2, StrokeCodec.MaxPoints))
                    {
                        Refuse(sender, "BAD STROKE", output);
                        return;
                    }
                    break;
                default:
                    Refuse(sender, "UNKNOWN MAP ITEM", output);
                    return;
            }

            var item = new CommsItem
            {
                Id = NextId(),
                Kind = kind,
                Author = sender.Id,
                AuthorName = name,
                Faction = sender.Faction,
                Channel = intent.Channel,
                Style = intent.Style,
                Size = kind == CommsItemKind.Stroke ? intent.Size : (byte)0,
                Points = (int[])intent.Points.Clone(),
                Text = text,
                Created = now,
                Expires = now + Rules.LifetimeOf(kind),
            };
            Store(item, now, output);
        }

        private void Store(CommsItem item, float now, List<CommsOutbound> output)
        {
            scratchItems.Clear();
            board.Add(item, true, scratchItems);
            output.Add(CommsOutbound.For(item.Channel, item.Faction, ItemEnvelope(item, now)));
            AnnounceRemoved(output);
        }

        /// <summary>
        /// Tell whoever could see them that the items in <see cref="scratchItems"/> are gone:
        /// one message per audience, so a TEAM mark's removal never reaches the other side.
        /// </summary>
        private void AnnounceRemoved(List<CommsOutbound> output)
        {
            while (scratchItems.Count > 0)
            {
                CommsItem first = scratchItems[scratchItems.Count - 1];
                scratchIds.Clear();
                for (int i = scratchItems.Count - 1; i >= 0; i--)
                {
                    if (!scratchItems[i].SameAudience(first)) continue;
                    scratchIds.Add(scratchItems[i].Id);
                    scratchItems.RemoveAt(i);
                }
                output.Add(CommsOutbound.For(first.Channel, first.Faction, RemoveEnvelope(scratchIds)));
            }
        }

        private void Erase(CommsSender sender, uint id, List<CommsOutbound> output)
        {
            CommsItem item = board.Find(id);
            if (item == null) return; // already gone: expired, or erased twice
            if (item.Author != sender.Id && !sender.Moderator)
            {
                Refuse(sender, "ONLY THE AUTHOR OR HOST CAN ERASE THAT", output);
                return;
            }
            board.Remove(id);
            scratchItems.Clear();
            scratchItems.Add(item);
            AnnounceRemoved(output);
        }

        private void ClearMine(CommsSender sender, List<CommsOutbound> output)
        {
            scratchItems.Clear();
            ulong author = sender.Id;
            board.RemoveWhere(item => item.Author == author, scratchItems);
            AnnounceRemoved(output);
        }

        private void ClearAll(CommsSender sender, string name, List<CommsOutbound> output)
        {
            if (!sender.Moderator)
            {
                Refuse(sender, "ONLY THE HOST CAN CLEAR THE MAP", output);
                return;
            }
            board.Clear();
            output.Add(CommsOutbound.Everyone(new CommsEnvelope { Event = CommsEvent.Reset }));
            output.Add(CommsOutbound.Everyone(new CommsEnvelope
            {
                Event = CommsEvent.Feed,
                Kind = (byte)CommsFeedKind.System,
                Author = sender.Id,
                AuthorName = name,
                Text = "CLEARED THE MAP",
            }));
        }

        // ---- Calls -------------------------------------------------------------------------

        private void Call(CommsSender sender, string name, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            if (!CommsCatalog.ValidCall(intent.Style))
            {
                Refuse(sender, "UNKNOWN CALL", output);
                return;
            }
            if (!ChannelAllowed(sender, intent.Channel, output)) return;

            BrevityCall call = CommsCatalog.Calls[intent.Style];
            bool located = call.MarksPosition && StrokeCodec.Valid(intent.Points, 1, 1);
            output.Add(CommsOutbound.For(intent.Channel, sender.Faction, new CommsEnvelope
            {
                Event = CommsEvent.Feed,
                Kind = (byte)CommsFeedKind.Call,
                Style = intent.Style,
                Author = sender.Id,
                AuthorName = name,
                Faction = sender.Faction,
                Channel = intent.Channel,
                Points = located ? (int[])intent.Points.Clone() : null,
            }));

            if (!located) return;
            // The caller's own mark: it names the call and sits at the caller's altitude, so a
            // cockpit marker points at the aircraft rather than at the ground beneath it.
            Store(new CommsItem
            {
                Id = NextId(),
                Kind = CommsItemKind.Ping,
                Author = sender.Id,
                AuthorName = name,
                Faction = sender.Faction,
                Channel = intent.Channel,
                Style = (byte)call.PingAtSelf,
                Size = CommsItem.CallMark,
                Points = (int[])intent.Points.Clone(),
                Text = call.Code,
                Height = Math.Min(intent.Target, (uint)MaxCallAltitude),
                Created = now,
                Expires = now + Rules.LifetimeOf(CommsItemKind.Ping),
            }, now, output);
        }

        // ---- Envelopes ---------------------------------------------------------------------

        internal static CommsEnvelope ItemEnvelope(CommsItem item, float now) => new CommsEnvelope
        {
            Event = CommsEvent.Item,
            Id = item.Id,
            Author = item.Author,
            AuthorName = item.AuthorName,
            Faction = item.Faction,
            Channel = item.Channel,
            Kind = (byte)item.Kind,
            Style = item.Style,
            Size = item.Size,
            Ttl = Math.Max(0f, item.Expires - now),
            Points = item.Points,
            Text = item.Text,
            Values = item.IsCall && !float.IsNaN(item.Height) ? new[] { (int)item.Height } : null,
        };

        private static CommsEnvelope RemoveEnvelope(List<uint> ids) => new CommsEnvelope
        {
            Event = CommsEvent.Remove,
            Ids = ids.ToArray(),
        };

        // ---- Rules -------------------------------------------------------------------------

        private bool ChannelAllowed(CommsSender sender, CommsChannel channel, List<CommsOutbound> output)
        {
            if (channel == CommsChannel.Team) return true;
            if (channel == CommsChannel.All && Rules.AllowAllChannel) return true;
            Refuse(sender, channel == CommsChannel.All ? "HOST HAS THE ALL CHANNEL OFF" : "UNKNOWN CHANNEL", output);
            return false;
        }

        /// <summary>A refusal to the sender alone; <paramref name="about"/> names the poll or game it concerns, if any.</summary>
        private static void Refuse(CommsSender sender, string reason, List<CommsOutbound> output, uint about = 0u) =>
            output.Add(CommsOutbound.To(sender.Id, new CommsEnvelope { Event = CommsEvent.Notice, Text = reason, Id = about }));

        private static float Cost(CommsOp op)
        {
            switch (op)
            {
                case CommsOp.Erase:
                case CommsOp.PollVote:
                case CommsOp.RpsCancel:
                case CommsOp.Sync:
                    return 0.5f;
                case CommsOp.Call:
                case CommsOp.Roll:
                case CommsOp.RpsChallenge:
                    return 2f;
                case CommsOp.PollCreate:
                case CommsOp.HuntStart:
                    return 4f;
                default:
                    return 1f;
            }
        }

        private uint NextId()
        {
            uint id = nextId++;
            if (nextId == 0) nextId = 1;
            return id;
        }
    }
}
