using System;

namespace NOAvionics
{
    /// <summary>An axis-aligned label box, positioned by its bottom-left corner.</summary>
    public struct AvLabelBox
    {
        public float X, Y, W, H;
    }

    /// <summary>The routed leader line and label placement for one HUD callout.</summary>
    public struct AvLeader
    {
        public bool Placed;
        public int Side;
        public float AnchorX, AnchorY;
        public float ElbowX, ElbowY;
        public float ShelfEndX;
        public AvLabelBox Label;
    }

    /// <summary>
    /// Allocation-free leader-line routing: places up to <see cref="MaxLabels"/> label boxes
    /// around their anchors without overlapping, elbowing out to whichever side of the anchor
    /// has room. Callers own every buffer; nothing here allocates.
    /// </summary>
    public static class AvLeaderRouting
    {
        public const int MaxLabels = 8;

        /// <summary>
        /// Routes <paramref name="count"/> labels (clamped to <see cref="MaxLabels"/> and to the
        /// shortest of the input/output arrays) in order, each anchored at
        /// (<paramref name="ax"/>[i], <paramref name="ay"/>[i]) with box size
        /// (<paramref name="w"/>[i], <paramref name="h"/>[i]).
        ///
        /// The preferred side is <paramref name="sides"/>[i] when non-zero, otherwise the side
        /// that fans the label away from the bounds' centre. Up to 6 attempts alternate between
        /// the preferred side and its opposite, lifting further each pair of attempts, elbowing
        /// out by <paramref name="run"/> and up by <paramref name="rise"/> (plus the lift) before
        /// dropping the label box. An attempt is rejected when its box leaves
        /// [<paramref name="minX"/>, <paramref name="maxX"/>] × [<paramref name="minY"/>, <paramref name="maxY"/>]
        /// or comes within <paramref name="gap"/> of an already-placed label. On success, the
        /// chosen side is written back into <paramref name="sides"/>[i] so a later call reuses it.
        /// Returns how many labels were placed.
        /// </summary>
        public static int Route(int count, float[] ax, float[] ay, float[] w, float[] h, int[] sides,
                                 float minX, float minY, float maxX, float maxY,
                                 float rise, float run, float gap, AvLeader[] result)
        {
            if (ax == null || ay == null || w == null || h == null || sides == null || result == null) return 0;

            int n = count;
            if (n > MaxLabels) n = MaxLabels;
            if (n > ax.Length) n = ax.Length;
            if (n > ay.Length) n = ay.Length;
            if (n > w.Length) n = w.Length;
            if (n > h.Length) n = h.Length;
            if (n > sides.Length) n = sides.Length;
            if (n > result.Length) n = result.Length;
            if (n < 0) n = 0;

            float centerX = (minX + maxX) * 0.5f;
            int placedCount = 0;

            for (int i = 0; i < n; i++)
            {
                float anchorX = ax[i], anchorY = ay[i];
                float boxW = w[i], boxH = h[i];
                int pref = sides[i] != 0 ? Math.Sign(sides[i]) : (anchorX < centerX ? -1 : 1);

                AvLeader leader = default(AvLeader);
                leader.AnchorX = anchorX;
                leader.AnchorY = anchorY;

                for (int attempt = 0; attempt < 6; attempt++)
                {
                    int side = (attempt % 2 == 0) ? pref : -pref;
                    float lift = (attempt / 2) * (boxH + gap);

                    float elbowX = anchorX + side * run;
                    float elbowY = anchorY + rise + lift;

                    float boxX = side > 0 ? elbowX : elbowX - boxW;
                    float boxY = elbowY + 1f;

                    if (boxX < minX || boxX + boxW > maxX || boxY < minY || boxY + boxH > maxY)
                        continue;

                    bool overlaps = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (!result[j].Placed) continue;
                        AvLabelBox other = result[j].Label;
                        if (Overlaps(boxX, boxY, boxW, boxH, other.X, other.Y, other.W, other.H, gap))
                        {
                            overlaps = true;
                            break;
                        }
                    }
                    if (overlaps) continue;

                    leader.Placed = true;
                    leader.Side = side;
                    leader.ElbowX = elbowX;
                    leader.ElbowY = elbowY;
                    leader.ShelfEndX = side > 0 ? elbowX + boxW : elbowX - boxW;
                    leader.Label = new AvLabelBox { X = boxX, Y = boxY, W = boxW, H = boxH };

                    sides[i] = side;
                    break;
                }

                result[i] = leader;
                if (leader.Placed) placedCount++;
            }

            return placedCount;
        }

        private static bool Overlaps(float ax0, float ay0, float aw, float ah, float bx0, float by0, float bw, float bh, float gap)
        {
            if (ax0 + aw + gap <= bx0) return false;
            if (bx0 + bw + gap <= ax0) return false;
            if (ay0 + ah + gap <= by0) return false;
            if (by0 + bh + gap <= ay0) return false;
            return true;
        }
    }
}
