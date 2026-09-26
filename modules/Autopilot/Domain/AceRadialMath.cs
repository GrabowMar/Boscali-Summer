using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Autopilot.Domain
{
    /// <summary>
    /// Pure 2D vector for deterministic radial layout math without requiring UnityEngine.
    /// </summary>
    internal readonly struct AceVec2 : IEquatable<AceVec2>
    {
        public float X { get; }
        public float Y { get; }

        public AceVec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static AceVec2 Zero => new AceVec2(0f, 0f);

        public static AceVec2 operator +(AceVec2 a, AceVec2 b) => new AceVec2(a.X + b.X, a.Y + b.Y);
        public static AceVec2 operator -(AceVec2 a, AceVec2 b) => new AceVec2(a.X - b.X, a.Y - b.Y);
        public static AceVec2 operator *(AceVec2 a, float scalar) => new AceVec2(a.X * scalar, a.Y * scalar);

        public static float Distance(AceVec2 a, AceVec2 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt((dx * dx) + (dy * dy));
        }

        public bool Equals(AceVec2 other) =>
            Math.Abs(X - other.X) < 0.0001f && Math.Abs(Y - other.Y) < 0.0001f;

        public override bool Equals(object obj) => obj is AceVec2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X:F1}, {Y:F1})";
    }

    /// <summary>
    /// Layout node metadata for an action in the radial tree.
    /// </summary>
    internal readonly struct AceRadialNodeLayout
    {
        public string ActionId { get; }
        public string DisplayName { get; }
        public AceVec2 Position { get; }
        public float AngleDeg { get; }
        public int Depth { get; }
        public bool IsBranch { get; }
        public bool IsAllowed { get; }
        public float Scale { get; }

        public AceRadialNodeLayout(
            string actionId,
            string displayName,
            AceVec2 position,
            float angleDeg,
            int depth,
            bool isBranch,
            bool isAllowed,
            float scale = 1f)
        {
            ActionId = actionId;
            DisplayName = displayName;
            Position = position;
            AngleDeg = angleDeg;
            Depth = depth;
            IsBranch = isBranch;
            IsAllowed = isAllowed;
            Scale = scale;
        }
    }

    /// <summary>
    /// Pure mathematical calculations for the ACE3 radial interaction menu.
    /// Handles radial geometry, branch fanning, angular distribution, hover hit-testing,
    /// and reticle rotation matching ace_interact_menu_fnc_renderMenu.
    /// </summary>
    internal static class AceRadialMath
    {
        public const float DefaultRootRadius = 140f;
        public const float DefaultBranchRadius = 110f;
        public const float DefaultMaxAngleSpanDeg = 140f;
        public const float DefaultAngleIntervalDeg = 50f;
        public const float SelectorRotationSpeedDegPerSec = 270f;
        public const float DefaultHoverThreshold = 55f;
        public const float ExpansionDurationSec = 0.15f;
        private const float Deg2Rad = (float)(Math.PI / 180.0);
        private const float Rad2Deg = (float)(180.0 / Math.PI);

        /// <summary>
        /// Computes layout positions for a root level ring.
        /// </summary>
        public static List<AceRadialNodeLayout> CalculateRootLayout(
            IReadOnlyList<AceRadialActionDescriptor> actions,
            AceVec2 center,
            float radius = DefaultRootRadius,
            float startAngleDeg = 90f)
        {
            var results = new List<AceRadialNodeLayout>();
            if (actions == null || actions.Count == 0) return results;

            int count = actions.Count;
            float step = 360f / count;

            for (int i = 0; i < count; i++)
            {
                AceRadialActionDescriptor act = actions[i];
                float angle = startAngleDeg - (i * step);
                float rad = angle * Deg2Rad;
                AceVec2 pos = center + new AceVec2((float)Math.Cos(rad) * radius, (float)Math.Sin(rad) * radius);

                results.Add(new AceRadialNodeLayout(
                    act.Id,
                    act.DisplayName,
                    pos,
                    angle,
                    depth: 0,
                    isBranch: act.HasChildren,
                    isAllowed: act.IsAllowed,
                    scale: 1f));
            }

            return results;
        }

        /// <summary>
        /// Computes layout positions for child actions fanning outward from a parent node in an arc.
        /// Replicates ACE3's branch fanning logic from fnc_renderMenu.sqf.
        /// </summary>
        public static List<AceRadialNodeLayout> CalculateBranchLayout(
            IReadOnlyList<AceRadialActionDescriptor> children,
            AceVec2 parentPosition,
            float parentAngleDeg,
            float branchRadius = DefaultBranchRadius,
            float maxAngleSpanDeg = DefaultMaxAngleSpanDeg,
            float expansionProgress = 1f)
        {
            var results = new List<AceRadialNodeLayout>();
            if (children == null || children.Count == 0) return results;

            int numChildren = children.Count;
            float angleSpan = Math.Min(maxAngleSpanDeg, DefaultAngleIntervalDeg * (numChildren - 1));
            if (angleSpan >= 305f) angleSpan = 360f;

            float interval;
            if (angleSpan < 360f)
            {
                interval = numChildren > 1 ? angleSpan / (numChildren - 1) : DefaultAngleIntervalDeg;
            }
            else
            {
                interval = angleSpan / numChildren;
            }

            float currentAngle = parentAngleDeg - (angleSpan * 0.5f);
            float scale = CalculateExpansionScale(expansionProgress);
            float currentRadius = branchRadius * scale;

            for (int i = 0; i < numChildren; i++)
            {
                AceRadialActionDescriptor child = children[i];
                float rad = currentAngle * Deg2Rad;
                AceVec2 pos = parentPosition + new AceVec2((float)Math.Cos(rad) * currentRadius, (float)Math.Sin(rad) * currentRadius);

                results.Add(new AceRadialNodeLayout(
                    child.Id,
                    child.DisplayName,
                    pos,
                    currentAngle,
                    depth: 1,
                    isBranch: child.HasChildren,
                    isAllowed: child.IsAllowed,
                    scale: scale));

                currentAngle += interval;
            }

            return results;
        }

        /// <summary>
        /// Updates the rotating selector bracket angle, matching ACE3's (270 * delta) mod 360.
        /// </summary>
        public static float UpdateSelectorRotation(float currentAngleDeg, float deltaTimeSec)
        {
            if (deltaTimeSec <= 0f) return currentAngleDeg;
            float updated = currentAngleDeg + (SelectorRotationSpeedDegPerSec * deltaTimeSec);
            return Repeat(updated, 360f);
        }

        /// <summary>
        /// Calculates smooth expansion scale [0.3 .. 1.0] from a normalized progress [0 .. 1].
        /// Matches ACE3's 0.3 + 0.7 * animProgress formula.
        /// </summary>
        public static float CalculateExpansionScale(float normalizedProgress)
        {
            float clamped = Clamp01(normalizedProgress);
            return 0.3f + (0.7f * clamped);
        }

        /// <summary>
        /// Finds the closest node to the cursor position within the selection threshold.
        /// Matches ACE3's 2D distance comparison in fnc_render.sqf.
        /// </summary>
        public static int FindClosestNodeIndex(
            AceVec2 cursorPosition,
            IReadOnlyList<AceRadialNodeLayout> nodes,
            float thresholdDistance = DefaultHoverThreshold)
        {
            if (nodes == null || nodes.Count == 0) return -1;

            int closestIndex = -1;
            float closestDist = thresholdDistance;

            for (int i = 0; i < nodes.Count; i++)
            {
                float dist = AceVec2.Distance(cursorPosition, nodes[i].Position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }

        public static float CursorToAngleDeg(AceVec2 cursorOffset)
        {
            float angle = (float)Math.Atan2(cursorOffset.Y, cursorOffset.X) * Rad2Deg;
            return Repeat(angle, 360f);
        }

        private static float Clamp01(float val)
        {
            if (val < 0f) return 0f;
            if (val > 1f) return 1f;
            return val;
        }

        private static float Repeat(float t, float length)
        {
            return (float)(t - (Math.Floor(t / length) * length));
        }
    }

    /// <summary>
    /// Pure description of an action for layout calculation.
    /// </summary>
    internal readonly struct AceRadialActionDescriptor
    {
        public string Id { get; }
        public string DisplayName { get; }
        public bool HasChildren { get; }
        public bool IsAllowed { get; }

        public AceRadialActionDescriptor(string id, string displayName, bool hasChildren = false, bool isAllowed = true)
        {
            Id = id;
            DisplayName = displayName;
            HasChildren = hasChildren;
            IsAllowed = isAllowed;
        }
    }
}
