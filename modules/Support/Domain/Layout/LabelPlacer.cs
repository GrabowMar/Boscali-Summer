using System;

namespace BoscaliSummer.Features.Support.Domain.Layout
{
    internal readonly struct LabelRequest
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;
        public readonly int Priority;
        public readonly float MarkerRadius;

        public LabelRequest(float x, float y, float width, float height, int priority, float markerRadius)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Priority = priority;
            MarkerRadius = markerRadius;
        }
    }

    internal struct PlacedLabel
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public bool Leader;
        public bool Visible;

        /// <summary>The request this label belongs to (results come back in priority order).</summary>
        public int Index;
    }

    /// <summary>
    /// Greedy labels, highest priority first, at most 32. Candidates are below, right, above, left,
    /// four diagonals, then the same eight at 1.8×. Anything but the first candidate draws a leader.
    /// </summary>
    internal static class LabelPlacer
    {
        public const int Maximum = 32;

        public static int Place(LabelRequest[] requests, int count, Box bounds, PlacedLabel[] into) =>
            Place(requests, count, bounds, into, null, 0);

        /// <summary>As <see cref="Place(LabelRequest[], int, Box, PlacedLabel[])"/>, keeping every label off the obstacles (panels over the map).</summary>
        public static int Place(LabelRequest[] requests, int count, Box bounds, PlacedLabel[] into, Box[] obstacles,
            int obstacleCount)
        {
            if (obstacles == null) obstacleCount = 0;
            obstacleCount = Math.Min(obstacleCount, obstacles?.Length ?? 0);
            if (requests == null || into == null || count <= 0 || into.Length == 0) return 0;
            count = Math.Min(count, requests.Length);
            int pool = Math.Min(count, 64);
            Span<int> order = stackalloc int[64];
            for (int i = 0; i < pool; i++) order[i] = i;
            for (int i = 1; i < pool; i++)
            {
                int key = order[i];
                int j = i - 1;
                while (j >= 0 && Less(requests, order[j], key))
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = key;
            }
            int considered = Math.Min(pool, Maximum);
            int placed = 0;
            for (int n = 0; n < considered && placed < into.Length; n++)
            {
                LabelRequest request = requests[order[n]];
                if (!Try(request, requests, order, considered, bounds, into, placed, obstacles, obstacleCount,
                        out PlacedLabel label)) continue;
                label.Index = order[n];
                into[placed++] = label;
            }
            return placed;
        }

        private static bool Less(LabelRequest[] requests, int index, int key) =>
            requests[index].Priority < requests[key].Priority ||
            (requests[index].Priority == requests[key].Priority && index > key);

        private static bool Try(LabelRequest request, LabelRequest[] requests, Span<int> order, int considered,
            Box bounds, PlacedLabel[] placed, int placedCount, Box[] obstacles, int obstacleCount, out PlacedLabel label)
        {
            label = default;
            for (int candidate = 0; candidate < 16; candidate++)
            {
                Slot(candidate, request, out float x, out float y);
                var rect = new Box(x, y, request.Width, request.Height);
                if (rect.X < bounds.X || rect.Y < bounds.Y || rect.Right > bounds.Right || rect.Bottom > bounds.Bottom)
                    continue;
                if (Hits(rect, placed, placedCount) || HitsMarker(rect, requests, order, considered) ||
                    HitsObstacle(rect, obstacles, obstacleCount)) continue;
                label = new PlacedLabel
                {
                    X = x,
                    Y = y,
                    Width = request.Width,
                    Height = request.Height,
                    Leader = candidate > 0,
                    Visible = true
                };
                return true;
            }
            return false;
        }

        private static void Slot(int candidate, LabelRequest request, out float x, out float y)
        {
            int ring = candidate / 8;
            int slot = candidate % 8;
            float gap = request.MarkerRadius + 2f;
            float ax = request.X;
            float ay = request.Y;
            float w = request.Width;
            float h = request.Height;
            switch (slot)
            {
                case 0: x = ax - w * 0.5f; y = ay + gap; break;
                case 1: x = ax + gap; y = ay - h * 0.5f; break;
                case 2: x = ax - w * 0.5f; y = ay - gap - h; break;
                case 3: x = ax - gap - w; y = ay - h * 0.5f; break;
                case 4: x = ax + gap; y = ay + gap; break;
                case 5: x = ax + gap; y = ay - gap - h; break;
                case 6: x = ax - gap - w; y = ay - gap - h; break;
                default: x = ax - gap - w; y = ay + gap; break;
            }
            if (ring == 0) return;
            float cx = x + w * 0.5f;
            float cy = y + h * 0.5f;
            x = ax + (cx - ax) * 1.8f - w * 0.5f;
            y = ay + (cy - ay) * 1.8f - h * 0.5f;
        }

        private static bool Hits(Box rect, PlacedLabel[] placed, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var other = new Box(placed[i].X, placed[i].Y, placed[i].Width, placed[i].Height);
                if (rect.Intersects(other)) return true;
            }
            return false;
        }

        private static bool HitsObstacle(Box rect, Box[] obstacles, int count)
        {
            for (int i = 0; i < count; i++)
                if (rect.Intersects(obstacles[i])) return true;
            return false;
        }

        private static bool HitsMarker(Box rect, LabelRequest[] requests, Span<int> order, int considered)
        {
            for (int i = 0; i < considered; i++)
            {
                LabelRequest marker = requests[order[i]];
                float radius = marker.MarkerRadius;
                var box = new Box(marker.X - radius, marker.Y - radius, radius * 2f, radius * 2f);
                if (rect.Intersects(box)) return true;
            }
            return false;
        }
    }
}
