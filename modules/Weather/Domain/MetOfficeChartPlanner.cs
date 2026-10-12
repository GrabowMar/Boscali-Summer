using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// Places the synoptic chart's labels, with no engine in it so a test can prove it, and only when the chart changed: <see cref="Update"/> hashes the model (<see cref="MetChartLayout.Signature"/>)
    /// and does nothing, allocating nothing, while the hash stands.
    ///
    /// <para>A label is placed in a fixed order (theater, camera, anchor, the six set-pieces), each on the free spot nearest its marker. "Free" is read from two occupancy grids built once per change
    /// (2 px cells, summed so any box is one lookup): HARD (the frame, the markers) and SOFT (the fronts, the storm cells and the camera's heading line), plus the exact boxes and leaders of the labels
    /// already placed. The tiers, best first: the full text on a free spot; the short text ("SC 62%") on a free spot; the full text straddling a front; the short text straddling a front. Each is
    /// within <see cref="MetChartLayout.LeaderMax"/> of its marker, joined to it by a leader when it is more than a marker's width away, and a leader never crosses another label or another leader.
    /// A label that fits none of them takes the least bad spot and says so (<see cref="Forced"/>); a chart with such a label is placed again, with every label preferring its short text, and once more with the
    /// stranded labels first, and the plan that strands fewest is kept.</para>
    /// </summary>
    internal sealed class MetChartPlanner
    {
        /// <summary>The plate under a label: nearly opaque, so what runs behind the letters (a grid line, a range ring, a front the label had to straddle) does not read through them.</summary>
        public const float PlateAlpha = 0.9f;

        private const float Cell = 2f, Step = 2f * Cell;
        private const int Tiers = 5, ForcedTier = 4;

        private struct Cand
        {
            public MetBox Box;
            public float Cost, Sx, Sy, Ex, Ey;
            public int Form;
            public bool Leader;
        }

        private readonly float ox, oy, plot;
        private readonly int n;
        private readonly Func<string, float> measure;
        private readonly byte[] occ;                                   // per cell: bit 1 = hard, bit 2 = soft
        private readonly int[] hardSum, softSum;                       // summed-area tables of the two layers, (n + 1) squared
        private readonly MetBox[] obstacle = new MetBox[MetChartLayout.MaxObstacles];
        private readonly string[] full = new string[MetChartLayout.Labels], brief = new string[MetChartLayout.Labels];
        private readonly Cand[] best = new Cand[Tiers];
        private readonly int[] order = new int[MetChartLayout.Labels];
        private readonly float[] markX = new float[MetChartLayout.Labels], markY = new float[MetChartLayout.Labels];
        private readonly bool[] markBelow = new bool[MetChartLayout.Labels], marked = new bool[MetChartLayout.Labels];
        private readonly string[] kText = new string[MetChartLayout.Labels];
        private readonly MetBox[] kBox = new MetBox[MetChartLayout.Labels];
        private readonly bool[] kLeader = new bool[MetChartLayout.Labels], kStraddle = new bool[MetChartLayout.Labels], kForced = new bool[MetChartLayout.Labels], kShort = new bool[MetChartLayout.Labels];
        private readonly float[] kLx0 = new float[MetChartLayout.Labels], kLy0 = new float[MetChartLayout.Labels], kLx1 = new float[MetChartLayout.Labels], kLy1 = new float[MetChartLayout.Labels];
        private static readonly int[] FullFirst = { 0, 1, 2, 3 }, ShortFirst = { 1, 0, 3, 2 };
        private ulong signature;
        private bool valid;

        // The scan in progress (one label, one form of its text).
        private int cSlot, cForm, cFree;
        private bool cForced;
        private float cMx, cMy, cW, cH, cStart, cAfter, cHi, cPrefY, cX0, cY0, cX1, cY1;

        /// <summary>What the last <see cref="Update"/> decided, per slot: the text to show ("" = no label), its box, and its leader (from the marker's edge to the label's nearest point).</summary>
        public readonly string[] Text = new string[MetChartLayout.Labels];
        public readonly MetBox[] Box = new MetBox[MetChartLayout.Labels];
        public readonly bool[] Leader = new bool[MetChartLayout.Labels], Straddle = new bool[MetChartLayout.Labels], Forced = new bool[MetChartLayout.Labels], Short = new bool[MetChartLayout.Labels];
        public readonly float[] LeaderX0 = new float[MetChartLayout.Labels], LeaderY0 = new float[MetChartLayout.Labels], LeaderX1 = new float[MetChartLayout.Labels], LeaderY1 = new float[MetChartLayout.Labels];

        /// <summary>How many times the labels were placed (not how many times <see cref="Update"/> was called): a test proves an unchanged model costs none.</summary>
        public int Runs { get; private set; }

        /// <param name="ox">Left of the plot, in chart pixels.</param>
        /// <param name="oy">Top of the plot.</param>
        /// <param name="plot">Side of the square plot.</param>
        /// <param name="measure">Width of a label's text in pixels, padding included (TextMeshPro at runtime, an estimate in a test); the widest line of a two-line text.</param>
        public MetChartPlanner(float ox, float oy, float plot, Func<string, float> measure)
        {
            this.ox = ox; this.oy = oy; this.plot = plot; this.measure = measure;
            n = (int)Math.Ceiling(plot / Cell);
            occ = new byte[n * n];
            hardSum = new int[(n + 1) * (n + 1)]; softSum = new int[(n + 1) * (n + 1)];
            for (int i = 0; i < Text.Length; i++) Text[i] = "";
        }

        /// <summary>Forget the plan (a font or theme change moves the text widths): the next <see cref="Update"/> places again.</summary>
        public void Invalidate() => valid = false;

        /// <summary>Re-places the labels when the model's <see cref="MetChartLayout.Signature"/> changed; true when it did (the caller then re-applies <see cref="Text"/> and <see cref="Box"/>), false at no cost.</summary>
        public bool Update(MetOfficeModel m)
        {
            ulong sig = MetChartLayout.Signature(m);
            if (valid && sig == signature) return false;
            signature = sig; valid = true; Runs++;
            Compose(m);
            PlaceAll(m);
            return true;
        }

        // ---- The words ----------------------------------------------------------------------------------------------------------------------

        private void Compose(MetOfficeModel m)
        {
            full[MetChartLayout.Theater] = MetOfficeText.Theater(m.HalfXKm, m.HalfZKm);
            brief[MetChartLayout.Theater] = full[MetChartLayout.Theater].Length > 0 ? "THEATER" : "";
            full[MetChartLayout.Camera] = m.HasCamera ? MetOfficeText.CameraLabel(m.CamHeadingDeg) : "";
            brief[MetChartLayout.Camera] = m.HasCamera ? "CAM" : "";
            float offset = m.HasAnchor && m.HasCamera ? (float)Math.Sqrt((m.AnchorX - m.CamX) * (m.AnchorX - m.CamX) + (m.AnchorZ - m.CamZ) * (m.AnchorZ - m.CamZ)) : 0f;
            full[MetChartLayout.Anchor] = m.HasAnchor ? MetOfficeText.AnchorLabel(offset) : "";
            brief[MetChartLayout.Anchor] = m.HasAnchor ? "ANCHOR" : "";
            for (int i = 0; i < MetOfficeModel.SetCount; i++)
            {
                bool on = m.Sets[i].Present && m.Sets[i].Placed;
                full[MetChartLayout.FirstSet + i] = on ? MetCatalog.SetNames[i] + " " + MetOfficeText.Cover(m.Sets[i].Strength) : "";
                brief[MetChartLayout.FirstSet + i] = on ? MetOfficeText.SetShortLabel(i, m.Sets[i].Strength) : "";
            }
        }

        // ---- The grids ----------------------------------------------------------------------------------------------------------------------

        private void Stamp(MetBox b, byte bit)
        {
            int x0 = Math.Max(0, (int)Math.Floor((b.X - ox) / Cell)), x1 = Math.Min(n, (int)Math.Ceiling((b.Right - ox) / Cell));
            int y0 = Math.Max(0, (int)Math.Floor((b.Y - oy) / Cell)), y1 = Math.Min(n, (int)Math.Ceiling((b.Bottom - oy) / Cell));
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) occ[y * n + x] |= bit;
        }

        private void Sum()
        {
            int w = n + 1;
            for (int y = 0; y < n; y++)
            {
                int hardRow = 0, softRow = 0;
                for (int x = 0; x < n; x++)
                {
                    byte o = occ[y * n + x];
                    hardRow += o & 1; softRow += o >> 1 & 1;
                    hardSum[(y + 1) * w + x + 1] = hardSum[y * w + x + 1] + hardRow;
                    softSum[(y + 1) * w + x + 1] = softSum[y * w + x + 1] + softRow;
                }
            }
        }

        private int Count(int[] sum, int x0, int y0, int x1, int y1)
        {
            int w = n + 1;
            return sum[y1 * w + x1] - sum[y0 * w + x1] - sum[y1 * w + x0] + sum[y0 * w + x0];
        }

        // ---- The placing --------------------------------------------------------------------------------------------------------------------

        private void PlaceAll(MetOfficeModel m)
        {
            Array.Clear(occ, 0, occ.Length);
            int hard;
            int count = MetChartLayout.Obstacles(m, ox, oy, plot, obstacle, out hard);
            for (int i = 0; i < count; i++) Stamp(obstacle[i], (byte)(i < hard ? 1 : 2));
            Sum();
            for (int slot = 0; slot < MetChartLayout.Labels; slot++)
                marked[slot] = full[slot].Length > 0 && MetChartLayout.Marker(m, slot, ox, oy, plot, out markX[slot], out markY[slot], out markBelow[slot]);
            SortSlots(0);
            int bestForced = Pass(false);
            if (bestForced == 0) return;
            // A label with no spot at all: the others took its room. Place again with every label preferring its short text (more of them fit), then with the stranded labels first; keep the plan that strands fewest.
            Keep(true);
            for (int attempt = 0; attempt < 5 && bestForced > 0; attempt++)
            {
                SortSlots(attempt);
                int forced = Pass(true);
                if (forced < bestForced) { bestForced = forced; Keep(true); }
            }
            Keep(false);
        }

        /// <summary>The order of the retries: the slots as they are, then west to east, east to west, north to south and south to north. Labels whose markers crowd one corner do not strand each other in the same order twice.</summary>
        private void SortSlots(int variant)
        {
            for (int k = 0; k < order.Length; k++) order[k] = k;
            if (variant == 0) return;
            for (int k = 1; k < order.Length; k++)
                for (int j = k; j > 0 && Before(order[j], order[j - 1], variant); j--) { int t = order[j]; order[j] = order[j - 1]; order[j - 1] = t; }
        }

        private bool Before(int a, int b, int variant)
        {
            float ka = variant <= 2 ? markX[a] : markY[a], kb = variant <= 2 ? markX[b] : markY[b];
            return variant == 1 || variant == 3 ? ka < kb : ka > kb;
        }

        /// <summary>Saves the plan just made (or puts the saved one back): the retries may strand more labels than the first pass did.</summary>
        private void Keep(bool save)
        {
            Array.Copy(save ? Text : kText, save ? kText : Text, Text.Length); Array.Copy(save ? Box : kBox, save ? kBox : Box, Box.Length);
            Array.Copy(save ? Leader : kLeader, save ? kLeader : Leader, Leader.Length); Array.Copy(save ? Straddle : kStraddle, save ? kStraddle : Straddle, Straddle.Length);
            Array.Copy(save ? Forced : kForced, save ? kForced : Forced, Forced.Length); Array.Copy(save ? Short : kShort, save ? kShort : Short, Short.Length);
            Array.Copy(save ? LeaderX0 : kLx0, save ? kLx0 : LeaderX0, Text.Length); Array.Copy(save ? LeaderY0 : kLy0, save ? kLy0 : LeaderY0, Text.Length);
            Array.Copy(save ? LeaderX1 : kLx1, save ? kLx1 : LeaderX1, Text.Length); Array.Copy(save ? LeaderY1 : kLy1, save ? kLy1 : LeaderY1, Text.Length);
        }

        /// <summary>One pass over the labels in <see cref="order"/>; the number of labels left on a bad spot.</summary>
        private int Pass(bool preferShort)
        {
            for (int slot = 0; slot < MetChartLayout.Labels; slot++) { Text[slot] = ""; Box[slot] = default; Leader[slot] = Straddle[slot] = Forced[slot] = Short[slot] = false; }
            int forced = 0;
            for (int k = 0; k < order.Length; k++)
            {
                int slot = order[k];
                if (!marked[slot]) continue;
                PlaceOne(slot, markX[slot], markY[slot], markBelow[slot], preferShort);
                if (Forced[slot]) forced++;
            }
            return forced;
        }

        private void PlaceOne(int slot, float mx, float my, bool below, bool preferShort)
        {
            for (int t = 0; t < Tiers; t++) best[t].Cost = float.MaxValue;
            bool differs = brief[slot] != full[slot];
            int[] tiers = preferShort && differs ? ShortFirst : FullFirst;
            int firstForm = tiers[0];
            Scan(slot, firstForm, mx, my, below, false);
            int pick = best[tiers[0]].Cost < float.MaxValue ? tiers[0] : -1;
            if (pick < 0)
            {
                if (differs) Scan(slot, 1 - firstForm, mx, my, below, false);
                for (int t = 1; t < tiers.Length && pick < 0; t++) if (best[tiers[t]].Cost < float.MaxValue) pick = tiers[t];
            }
            if (pick < 0)
            {
                Scan(slot, differs ? 1 : 0, mx, my, below, true);
                if (best[ForcedTier].Cost < float.MaxValue) pick = ForcedTier;
            }
            if (pick < 0) return;
            Cand c = best[pick];
            Text[slot] = c.Form == 0 ? full[slot] : brief[slot];
            Short[slot] = c.Form == 1;
            Box[slot] = c.Box;
            Straddle[slot] = pick == 2 || pick == 3;
            Forced[slot] = pick == ForcedTier;
            Leader[slot] = c.Leader;
            LeaderX0[slot] = c.Sx; LeaderY0[slot] = c.Sy; LeaderX1[slot] = c.Ex; LeaderY1[slot] = c.Ey;
        }

        /// <summary>
        /// Walks the spots within the leader's reach of the marker for one form of the text, nearest first (rows and columns of a 4 px lattice outward from the box centred on the marker), and keeps the cheapest
        /// candidate of each tier. Cost is the distance from the marker, a small pull toward the slot's first choice (right of and above its marker; the camera's below), what the label straddles and what its leader runs
        /// through. The walk stops where the distance alone already exceeds the best free spot found, and the winners are nudged by half a lattice step. <paramref name="forced"/> is the last resort: every tier is open,
        /// the hard overlaps and crossings are paid for instead of refused, and the answer goes to tier 4.
        /// </summary>
        private void Scan(int slot, int form, float mx, float my, bool below, bool forced)
        {
            string text = form == 0 ? full[slot] : brief[slot];
            cSlot = slot; cForm = form; cForced = forced; cMx = mx; cMy = my; cW = measure(text); cH = MetChartLayout.Height(text);
            cStart = MetChartLayout.MarkerHalf(slot); cAfter = MetChartLayout.LeaderAfter(slot); cHi = cStart + MetChartLayout.LeaderMax; cPrefY = below ? 1f : -1f;
            cX0 = ox + 1f; cY0 = oy + 1f; cX1 = ox + plot - 1f; cY1 = oy + plot - 1f;
            cFree = forced ? ForcedTier : form;
            int kxMax = (int)Math.Floor((cX1 - cW - cX0) / Step), kyMax = (int)Math.Floor((cY1 - cH - cY0) / Step);
            if (kxMax < 0 || kyMax < 0) return;
            int kx0 = Clamp((int)Math.Round((mx - cW * 0.5f - cX0) / Step), 0, kxMax), ky0 = Clamp((int)Math.Round((my - cH * 0.5f - cY0) / Step), 0, kyMax);
            float hi2 = cHi * cHi;
            for (int dy = 0; dy <= kyMax; dy++)
            {
                bool rowSeen = false;
                for (int sy = 0; sy < 2; sy++)
                {
                    int ky = sy == 0 ? ky0 - dy : ky0 + dy;
                    if (ky < 0 || ky > kyMax || (sy == 1 && dy == 0)) continue;
                    float y = cY0 + ky * Step;
                    float gy = y > my ? y - my : y + cH < my ? my - (y + cH) : 0f;
                    if (gy > cHi || gy >= best[cFree].Cost) continue;
                    rowSeen = true;
                    for (int dx = 0; dx <= kxMax; dx++)
                    {
                        bool colSeen = false;
                        for (int sx = 0; sx < 2; sx++)
                        {
                            int kx = sx == 0 ? kx0 - dx : kx0 + dx;
                            if (kx < 0 || kx > kxMax || (sx == 1 && dx == 0)) continue;
                            float x = cX0 + kx * Step;
                            float gx = x > mx ? x - mx : x + cW < mx ? mx - (x + cW) : 0f;
                            float g2 = gx * gx + gy * gy;
                            if (g2 > hi2) continue;
                            float gap = (float)Math.Sqrt(g2);
                            if (gap >= best[cFree].Cost) continue;
                            colSeen = true;
                            Try(x, y, gap);
                        }
                        if (!colSeen && dx > 0) break;      // gx only grows outward: both columns are past the reach or the best
                    }
                }
                if (!rowSeen && dy > 0) break;
            }
            // Half a lattice step either way around the winner of each tier this form can reach.
            for (int tier = forced ? ForcedTier : form; tier <= (forced ? ForcedTier : form + 2); tier += 2)
            {
                if (best[tier].Cost >= float.MaxValue) continue;
                MetBox b = best[tier].Box;
                for (int d = 0; d < 9; d++)
                {
                    if (d == 4) continue;
                    float nx = b.X + (d % 3 - 1) * Cell, ny = b.Y + (d / 3 - 1) * Cell;
                    if (nx < cX0 || ny < cY0 || nx + cW > cX1 || ny + cH > cY1) continue;
                    float gx = nx > mx ? nx - mx : nx + cW < mx ? mx - (nx + cW) : 0f, gy = ny > my ? ny - my : ny + cH < my ? my - (ny + cH) : 0f, g2 = gx * gx + gy * gy;
                    if (g2 <= hi2) Try(nx, ny, (float)Math.Sqrt(g2));
                }
            }
        }

        /// <summary>One spot for the label being scanned: its tier from the grids, its cost, the exact tests, and a place in <see cref="best"/> when it beats the tier's winner.</summary>
        private void Try(float x, float y, float gap)
        {
            int cx0 = (int)Math.Floor((x - ox) / Cell), cx1 = Math.Min(n, (int)Math.Ceiling((x + cW - ox) / Cell));
            int cy0 = (int)Math.Floor((y - oy) / Cell), cy1 = Math.Min(n, (int)Math.Ceiling((y + cH - oy) / Cell));
            int hardHit = Count(hardSum, cx0, cy0, cx1, cy1);
            if (hardHit > 0 && !cForced) return;
            int soft = Count(softSum, cx0, cy0, cx1, cy1);
            int tier = cForced ? ForcedTier : cForm + (soft > 0 ? 2 : 0);
            if (tier != cFree && best[cFree].Cost < float.MaxValue) return;       // a free spot is already known: a straddling one can no longer win
            float cyMid = y + cH * 0.5f;
            float cost = gap + 0.04f * Math.Abs(cyMid - (cMy + cPrefY * 6f)) + ((cyMid - cMy) * cPrefY < 0f ? 2f : 0f) + (x + cW * 0.5f < cMx ? 2f : 0f) + 0.5f * soft + 6f * hardHit;
            if (cost >= best[tier].Cost) return;
            if (!Accept(new MetBox(x, y, cW, cH), gap, out Cand c, out float penalty)) return;
            cost += penalty;
            if (cost >= best[tier].Cost) return;
            c.Cost = cost; c.Form = cForm;
            best[tier] = c;
        }

        /// <summary>
        /// Exact tests of one spot against the labels and leaders already placed: the box touches none of them, and its leader (a segment from the marker's edge to the box's nearest point, when the gap needs
        /// one) crosses no label and passes at least 2 px from every leader. Also prices what the leader runs through (a front, a ring). Forced: the overlaps are priced, not refused, and nothing else is checked.
        /// </summary>
        private bool Accept(MetBox box, float gap, out Cand c, out float penalty)
        {
            c = default; penalty = 0f;
            c.Box = box;
            if (gap > cAfter)
            {
                float nx = cMx < box.X ? box.X : cMx > box.Right ? box.Right : cMx, ny = cMy < box.Y ? box.Y : cMy > box.Bottom ? box.Bottom : cMy;
                c.Leader = true; c.Sx = cMx + (nx - cMx) / gap * cStart; c.Sy = cMy + (ny - cMy) / gap * cStart; c.Ex = nx; c.Ey = ny;
            }
            var grown = new MetBox(box.X - 1f, box.Y - 1f, box.W + 2f, box.H + 2f);
            for (int j = 0; j < MetChartLayout.Labels; j++)
            {
                if (j == cSlot || Box[j].W <= 0f) continue;
                if (MetChartMath.Overlap(grown, Box[j]) > 0f) { if (!cForced) return false; penalty += 40f; }
                if (cForced) continue;
                if (Leader[j] && MetChartLayout.SegmentHitsBox(LeaderX0[j], LeaderY0[j], LeaderX1[j], LeaderY1[j], box, 1f)) return false;
                if (!c.Leader) continue;
                if (MetChartLayout.SegmentHitsBox(c.Sx, c.Sy, c.Ex, c.Ey, Box[j], 1f)) return false;
                if (Leader[j] && MetChartLayout.SegmentDistance(c.Sx, c.Sy, c.Ex, c.Ey, LeaderX0[j], LeaderY0[j], LeaderX1[j], LeaderY1[j]) < 2f) return false;
            }
            if (c.Leader && !cForced)
            {
                float len = (float)Math.Sqrt((c.Ex - c.Sx) * (c.Ex - c.Sx) + (c.Ey - c.Sy) * (c.Ey - c.Sy));
                int steps = Math.Max(1, (int)(len / 3f));
                for (int s = 1; s < steps; s++)
                {
                    int cx = (int)((c.Sx + (c.Ex - c.Sx) * s / steps - ox) / Cell), cy = (int)((c.Sy + (c.Ey - c.Sy) * s / steps - oy) / Cell);
                    if (cx < 0 || cy < 0 || cx >= n || cy >= n) continue;
                    byte o = occ[cy * n + cx];
                    penalty += (o & 1) != 0 ? 8f : (o & 2) != 0 ? 3f : 0f;
                }
            }
            return true;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
