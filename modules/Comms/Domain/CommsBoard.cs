using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Comms.Domain
{
    /// <summary>What a map item is. Values are wire bytes; append only.</summary>
    internal enum CommsItemKind : byte
    {
        Ping = 0,
        Sticker = 1,
        Stroke = 2,
        Label = 3,
    }

    /// <summary>
    /// One thing on the shared map: a ping, a sticker, a text label or a stroke. The same
    /// record is the host's authority copy and a peer's display copy; only the clock the
    /// expiry is measured on differs, which is why expiry travels as time-to-live.
    /// </summary>
    internal sealed class CommsItem
    {
        public uint Id;
        public CommsItemKind Kind;
        public ulong Author;
        public string AuthorName;
        public int Faction;
        public CommsChannel Channel;

        /// <summary>Ping kind, sticker index or pen ink, by <see cref="Kind"/>.</summary>
        public byte Style;

        /// <summary>Stroke width index; unused elsewhere.</summary>
        public byte Size;

        /// <summary>Quantised interleaved points: one pair for a point item, 2+ for a stroke.</summary>
        public int[] Points;

        /// <summary>A label's words. Empty for every other kind.</summary>
        public string Text;

        public float Created;
        public float Expires;

        public float X => Points != null && Points.Length >= 2 ? StrokeCodec.Restore(Points[0]) : 0f;
        public float Z => Points != null && Points.Length >= 2 ? StrokeCodec.Restore(Points[1]) : 0f;
    }

    /// <summary>
    /// The shared map's items, their per-author budgets and their lifetimes. Pure: the host
    /// feeds it validated posts and its own clock, a peer feeds it what the host routed to it
    /// and the peer's clock. <see cref="Revision"/> moves on every change so the map layer
    /// rebuilds its mesh only when there is something new to draw.
    /// </summary>
    internal sealed class CommsBoard
    {
        /// <summary>Most items one author may hold, by kind. A new one retires that author's oldest.</summary>
        public static int AuthorBudget(CommsItemKind kind)
        {
            switch (kind)
            {
                case CommsItemKind.Ping: return 3;
                case CommsItemKind.Sticker: return 12;
                case CommsItemKind.Label: return 8;
                default: return 40;
            }
        }

        /// <summary>Hard ceiling on the whole board, whoever posted.</summary>
        public const int MaxItems = 320;

        private readonly List<CommsItem> items = new List<CommsItem>(64);

        public int Revision { get; private set; }

        public IReadOnlyList<CommsItem> Items => items;

        public int Count => items.Count;

        public CommsItem Find(uint id)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i].Id == id) return items[i];
            return null;
        }

        /// <summary>
        /// Store an item, replacing one with the same id. When <paramref name="enforceBudget"/>
        /// is set (the host), the author's oldest item of the same kind and then the board's
        /// oldest item are retired to make room, and their ids are reported so the host can
        /// tell every peer.
        /// </summary>
        public void Add(CommsItem item, bool enforceBudget, List<uint> retired)
        {
            if (item == null) return;
            int existing = IndexOf(item.Id);
            if (existing >= 0) items.RemoveAt(existing);

            if (enforceBudget)
            {
                int budget = AuthorBudget(item.Kind);
                while (CountBy(item.Author, item.Kind) >= budget)
                {
                    int oldest = OldestBy(item.Author, item.Kind);
                    if (oldest < 0) break;
                    retired?.Add(items[oldest].Id);
                    items.RemoveAt(oldest);
                }
                while (items.Count >= MaxItems)
                {
                    retired?.Add(items[0].Id);
                    items.RemoveAt(0);
                }
            }

            items.Add(item);
            Revision++;
        }

        public bool Remove(uint id)
        {
            int index = IndexOf(id);
            if (index < 0) return false;
            items.RemoveAt(index);
            Revision++;
            return true;
        }

        /// <summary>Remove every item <paramref name="match"/> accepts; report their ids.</summary>
        public int RemoveWhere(Func<CommsItem, bool> match, List<uint> removed)
        {
            int count = 0;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (!match(items[i])) continue;
                removed?.Add(items[i].Id);
                items.RemoveAt(i);
                count++;
            }
            if (count > 0) Revision++;
            return count;
        }

        /// <summary>Drop what has outlived its time-to-live on this clock.</summary>
        public int Expire(float now, List<uint> removed)
        {
            int count = 0;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].Expires > now) continue;
                removed?.Add(items[i].Id);
                items.RemoveAt(i);
                count++;
            }
            if (count > 0) Revision++;
            return count;
        }

        public void Clear()
        {
            if (items.Count == 0) return;
            items.Clear();
            Revision++;
        }

        /// <summary>The author's most recent item, optionally of one kind: what UNDO takes back.</summary>
        public CommsItem LatestBy(ulong author, CommsItemKind? kind = null)
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                CommsItem item = items[i];
                if (item.Author == author && (!kind.HasValue || item.Kind == kind.Value)) return item;
            }
            return null;
        }

        public int CountBy(ulong author, CommsItemKind kind)
        {
            int count = 0;
            for (int i = 0; i < items.Count; i++)
                if (items[i].Author == author && items[i].Kind == kind) count++;
            return count;
        }

        public int CountOf(CommsItemKind kind)
        {
            int count = 0;
            for (int i = 0; i < items.Count; i++)
                if (items[i].Kind == kind) count++;
            return count;
        }

        /// <summary>
        /// The item nearest a ground point within <paramref name="radius"/> metres, optionally
        /// only one author's: the eraser's pick. Strokes measure to their nearest segment.
        /// </summary>
        public CommsItem Nearest(float x, float z, float radius, ulong? author)
        {
            CommsItem best = null;
            float bestSq = radius * radius;
            for (int i = 0; i < items.Count; i++)
            {
                CommsItem item = items[i];
                if (author.HasValue && item.Author != author.Value) continue;
                float d = DistanceSq(item, x, z);
                if (d > bestSq) continue;
                bestSq = d;
                best = item;
            }
            return best;
        }

        /// <summary>Whether a viewer on <paramref name="viewerFaction"/> may see a post.</summary>
        public static bool Visible(CommsChannel channel, int postFaction, int viewerFaction) =>
            channel == CommsChannel.All || postFaction == viewerFaction;

        internal static float DistanceSq(CommsItem item, float x, float z)
        {
            int[] p = item.Points;
            if (p == null || p.Length < 2) return float.MaxValue;
            if (p.Length < 4)
            {
                float dx = StrokeCodec.Restore(p[0]) - x, dz = StrokeCodec.Restore(p[1]) - z;
                return dx * dx + dz * dz;
            }
            float best = float.MaxValue;
            for (int i = 2; i + 1 < p.Length; i += 2)
            {
                float ax = StrokeCodec.Restore(p[i - 2]), az = StrokeCodec.Restore(p[i - 1]);
                float bx = StrokeCodec.Restore(p[i]), bz = StrokeCodec.Restore(p[i + 1]);
                float sx = bx - ax, sz = bz - az;
                float lengthSq = sx * sx + sz * sz;
                float t = lengthSq > 1e-6f ? ((x - ax) * sx + (z - az) * sz) / lengthSq : 0f;
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                float cx = ax + sx * t - x, cz = az + sz * t - z;
                float d = cx * cx + cz * cz;
                if (d < best) best = d;
            }
            return best;
        }

        private int IndexOf(uint id)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i].Id == id) return i;
            return -1;
        }

        private int OldestBy(ulong author, CommsItemKind kind)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i].Author == author && items[i].Kind == kind) return i;
            return -1;
        }
    }
}
