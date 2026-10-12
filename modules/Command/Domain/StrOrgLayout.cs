using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>One card of the org chart. <see cref="Row"/> is the officer row it shows, or -1 for an empty card (an office nobody holds). Top-left origin, +Y down.</summary>
    internal struct StrOrgCard
    {
        public int Row, Parent, Tier;
        public float X, Y, W, H;
    }

    /// <summary>One connector line: a one unit thick rectangle.</summary>
    internal struct StrOrgSeg
    {
        public float X, Y, W, H;
    }

    /// <summary>
    /// The chain of command as a top-down chart: the theater commander centred on top, each office under the one it answers to, every card the same size.
    /// Each office gets a stretch of the width in proportion to the bases under it and stands centred over that stretch; the connectors are a drop from the
    /// parent, a bus across its children and a drop into each child. An office no card holds is drawn empty in its place; an officer whose parent is
    /// not on the board hangs under the theater commander. Fixed buffers, nothing allocates while building.
    /// </summary>
    internal sealed class StrOrgPlan
    {
        public const int MaxCards = 12, MaxSegs = 4 * MaxCards, NoCard = int.MinValue;

        public readonly StrOrgCard[] Cards = new StrOrgCard[MaxCards];
        public readonly StrOrgSeg[] Segs = new StrOrgSeg[MaxSegs];
        public int CardCount, SegCount;
        public float Height;

        private readonly int[] id = new int[MaxCards], depth = new int[MaxCards], leaves = new int[MaxCards], start = new int[MaxCards];

        public void Build(StrOfficerRow[] rows, int count, float width, float cardH, float gap, float vGap, float maxCardW)
        {
            CardCount = SegCount = 0;
            Height = 0f;
            int n = rows == null ? 0 : Math.Min(count, Math.Min(rows.Length, MaxCards));
            if (n <= 0) return;

            int root = -1;
            for (int i = 0; i < n; i++)
            {
                id[i] = rows[i].Id;
                Cards[i] = new StrOrgCard { Row = i, Parent = -1, Tier = Math.Max(0, Math.Min(2, rows[i].Tier)) };
                if (root < 0 && Cards[i].Tier == 0) root = i;
            }
            CardCount = n;
            if (root < 0) root = Vacant(NoCard, 0);
            if (root < 0) root = 0;   // the buffer is full of officers and none is a theater commander: the first stands on top

            for (int i = 0; i < n; i++)
            {
                if (i == root) continue;
                int want = rows[i].ParentId, at = want < 0 || want == id[i] ? -1 : Find(want);
                if (at < 0 && want >= 0 && want != id[i] && Cards[i].Tier == 2) at = Vacant(want, 1);
                Cards[i].Parent = at < 0 || at == i ? root : at;
            }
            for (int i = CardCount - 1; i >= n; i--) Cards[i].Parent = Cards[i].Tier == 0 ? -1 : root;
            Cards[root].Parent = -1;

            // A parent chain that loops or runs into another dead end is cut: the officer hangs under the root instead.
            for (int i = 0; i < CardCount; i++)
            {
                int cur = i, hops = 0;
                while (cur >= 0 && cur != root && hops++ <= CardCount) cur = Cards[cur].Parent;
                if (cur != root) Cards[i].Parent = root;
            }

            int deepest = 0;
            for (int i = 0; i < CardCount; i++)
            {
                int d = 0, cur = i;
                while (cur != root) { cur = Cards[cur].Parent; d++; }
                depth[i] = d;
                deepest = Math.Max(deepest, d);
            }

            // Leaves under each office, deepest first; a childless office is one leaf.
            for (int i = 0; i < CardCount; i++) leaves[i] = 0;
            for (int d = deepest; d >= 0; d--)
                for (int i = 0; i < CardCount; i++)
                {
                    if (depth[i] != d) continue;
                    if (leaves[i] == 0) leaves[i] = 1;
                    if (i != root) leaves[Cards[i].Parent] += leaves[i];
                }
            // Each office's first leaf, shallowest first: the children of one office stand left to right inside its stretch, in row order.
            start[root] = 0;
            for (int d = 0; d < deepest; d++)
                for (int i = 0; i < CardCount; i++)
                {
                    if (depth[i] != d) continue;
                    int at = start[i];
                    for (int j = 0; j < CardCount; j++)
                        if (j != root && Cards[j].Parent == i) { start[j] = at; at += leaves[j]; }
                }

            int total = leaves[root];
            float slot = (width - (total - 1) * gap) / total, cardW = Math.Min(slot, maxCardW);
            for (int i = 0; i < CardCount; i++)
            {
                float span = leaves[i] * slot + (leaves[i] - 1) * gap;
                Cards[i].W = cardW;
                Cards[i].H = cardH;
                Cards[i].X = start[i] * (slot + gap) + (span - cardW) * 0.5f;
                Cards[i].Y = depth[i] * (cardH + vGap);
            }
            Height = (deepest + 1) * cardH + deepest * vGap;

            for (int p = 0; p < CardCount; p++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int c = 0; c < CardCount; c++)
                {
                    if (c == root || Cards[c].Parent != p) continue;
                    float cx = Cards[c].X + cardW * 0.5f;
                    lo = Math.Min(lo, cx);
                    hi = Math.Max(hi, cx);
                }
                if (lo > hi) continue;
                float px = Cards[p].X + cardW * 0.5f, bottom = Cards[p].Y + cardH, bus = bottom + vGap * 0.5f;
                lo = Math.Min(lo, px);
                hi = Math.Max(hi, px);
                Seg(px - 0.5f, bottom, 1f, vGap * 0.5f);
                Seg(lo - 0.5f, bus - 0.5f, hi - lo + 1f, 1f);
                for (int c = 0; c < CardCount; c++)
                    if (c != root && Cards[c].Parent == p) Seg(Cards[c].X + cardW * 0.5f - 0.5f, bus, 1f, Cards[c].Y - bus);
            }
        }

        private int Find(int officeId)
        {
            for (int i = 0; i < CardCount; i++) if (id[i] == officeId) return i;
            return -1;
        }

        // The empty card for an office nobody holds, or the card already standing for it. Past the buffer the officer hangs under the root instead (-1).
        private int Vacant(int officeId, int tier)
        {
            int at = Find(officeId);
            if (at >= 0) return at;
            if (CardCount >= MaxCards) return -1;
            id[CardCount] = officeId;
            Cards[CardCount] = new StrOrgCard { Row = -1, Parent = -1, Tier = tier };
            return CardCount++;
        }

        private void Seg(float x, float y, float w, float h)
        {
            if (SegCount < MaxSegs) Segs[SegCount++] = new StrOrgSeg { X = x, Y = y, W = w, H = h };
        }
    }
}
