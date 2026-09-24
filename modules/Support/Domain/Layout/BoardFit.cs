using System;

namespace BoscaliSummer.Features.Support.Domain.Layout
{
    /// <summary>A north-up map frame. World +Z is screen −Y. One metres-per-pixel keeps the aspect.</summary>
    internal struct BoardFrame
    {
        public float CentreX;
        public float CentreZ;
        public float MetresPerPixel;
    }

    internal static class BoardFit
    {
        private const int MaximumPoints = 64;

        public static BoardFrame Fit(float[] xs, float[] zs, int count, float viewW, float viewH,
            float padding, float minSpan, float trimPercent)
        {
            var frame = new BoardFrame { MetresPerPixel = 1f };
            if (xs == null || zs == null || count <= 0 || !(viewW > 1f) || !(viewH > 1f))
                return frame;
            count = Math.Min(count, Math.Min(xs.Length, zs.Length));
            count = Math.Min(count, MaximumPoints);
            Span<int> order = stackalloc int[MaximumPoints];
            int drop = (int)Math.Floor(count * Math.Max(0f, trimPercent) / 100f);
            if (drop < 0 || drop * 2 >= count) drop = 0;
            int hi = count - 1 - drop;
            Sort(xs, order, count);
            float minX = xs[order[drop]];
            float maxX = xs[order[hi]];
            Sort(zs, order, count);
            float minZ = zs[order[drop]];
            float maxZ = zs[order[hi]];
            float spanX = Math.Max(maxX - minX, minSpan > 0f ? minSpan : 0f);
            float spanZ = Math.Max(maxZ - minZ, minSpan > 0f ? minSpan : 0f);
            float innerW = Math.Max(1f, viewW - padding * 2f);
            float innerH = Math.Max(1f, viewH - padding * 2f);
            frame.CentreX = (minX + maxX) * 0.5f;
            frame.CentreZ = (minZ + maxZ) * 0.5f;
            frame.MetresPerPixel = Math.Max(spanX / innerW, spanZ / innerH);
            if (!(frame.MetresPerPixel > 0f)) frame.MetresPerPixel = 1f;
            return frame;
        }

        public static void Project(BoardFrame frame, float worldX, float worldZ, float viewW, float viewH,
            out float screenX, out float screenY)
        {
            float scale = frame.MetresPerPixel > 0f ? frame.MetresPerPixel : 1f;
            screenX = (worldX - frame.CentreX) / scale + viewW * 0.5f;
            screenY = (frame.CentreZ - worldZ) / scale + viewH * 0.5f;
        }

        public static void Unproject(BoardFrame frame, float screenX, float screenY, float viewW, float viewH,
            out float worldX, out float worldZ)
        {
            float scale = frame.MetresPerPixel > 0f ? frame.MetresPerPixel : 1f;
            worldX = frame.CentreX + (screenX - viewW * 0.5f) * scale;
            worldZ = frame.CentreZ - (screenY - viewH * 0.5f) * scale;
        }

        public static BoardFrame Zoom(BoardFrame frame, float factor, float pivotX, float pivotY,
            float viewW, float viewH, float minMetresPerPixel, float maxMetresPerPixel)
        {
            if (!(factor > 0f) || float.IsNaN(factor)) factor = 1f;
            Unproject(frame, pivotX, pivotY, viewW, viewH, out float worldX, out float worldZ);
            float scale = (frame.MetresPerPixel > 0f ? frame.MetresPerPixel : 1f) / factor;
            if (scale < minMetresPerPixel) scale = minMetresPerPixel;
            if (scale > maxMetresPerPixel) scale = maxMetresPerPixel;
            frame.MetresPerPixel = scale;
            frame.CentreX = worldX - (pivotX - viewW * 0.5f) * scale;
            frame.CentreZ = worldZ + (pivotY - viewH * 0.5f) * scale;
            return frame;
        }

        /// <summary>Screen-pixel drag. The map square ±mapHalf always keeps a sliver inside the view.</summary>
        public static BoardFrame Pan(BoardFrame frame, float dx, float dy, float viewW, float viewH, float mapHalf)
        {
            float scale = frame.MetresPerPixel > 0f ? frame.MetresPerPixel : 1f;
            frame.CentreX -= dx * scale;
            frame.CentreZ += dy * scale;
            float halfW = Math.Abs(viewW) * 0.5f * scale;
            float halfH = Math.Abs(viewH) * 0.5f * scale;
            float keep = mapHalf > 1f ? 1f : 0f;
            frame.CentreX = ClampCentre(frame.CentreX, halfW, mapHalf, keep);
            frame.CentreZ = ClampCentre(frame.CentreZ, halfH, mapHalf, keep);
            return frame;
        }

        private static float ClampCentre(float centre, float halfView, float mapHalf, float keep)
        {
            float min = -mapHalf + keep - halfView;
            float max = mapHalf - keep + halfView;
            if (min > max) return 0f;
            if (centre < min) return min;
            if (centre > max) return max;
            return centre;
        }

        private static void Sort(float[] values, Span<int> order, int count)
        {
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = 1; i < count; i++)
            {
                int key = order[i];
                float value = values[key];
                int j = i - 1;
                while (j >= 0 && values[order[j]] > value)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = key;
            }
        }
    }
}
