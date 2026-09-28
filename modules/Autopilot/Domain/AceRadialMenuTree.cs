using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Autopilot.Domain
{
    /// <summary>One laid-out option for the current frame.</summary>
    internal readonly struct AceRadialNodeLayout
    {
        public AceRadialNodeLayout(AceRadialNode node, AceVec2 position, int level, bool onPath,
            bool currentLevel, float scale)
        {
            Node = node;
            Position = position;
            Level = level;
            OnPath = onPath;
            CurrentLevel = currentLevel;
            Scale = scale;
        }

        public AceRadialNode Node { get; }
        public AceVec2 Position { get; }
        /// <summary>0 is the centre node; its options are level 1.</summary>
        public int Level { get; }
        /// <summary>This option is part of the expanded breadcrumb.</summary>
        public bool OnPath { get; }
        /// <summary>This option is a child of the deepest expanded node (the live choice).</summary>
        public bool CurrentLevel { get; }
        public float Scale { get; }
    }

    /// <summary>
    /// Navigation state of the ACE3-style interaction menu (fnc_render / fnc_renderMenu /
    /// fnc_keyUp). The centre node is always expanded. Resting the cursor on a branch for
    /// <see cref="DwellSec"/> makes it the deepest expanded node: its children fan out, the
    /// breadcrumb and its siblings stay visible, and resting on a shallower option (or the
    /// centre) collapses back to that level. Releasing the key or clicking runs the hovered leaf after
    /// re-checking its conditions. Pure: time and cursor come in as arguments.
    /// </summary>
    internal sealed class AceRadialMenuTree
    {
        public const float DwellSec = 1f / 6f;
        public const float ExpandSec = 0.125f;
        public const float RecollectSec = 1f;
        /// <summary>Options collected per pass (the whole reachable tree).</summary>
        public const int MaxNodes = 128;
        /// <summary>Options laid out and drawn at once (centre + open breadcrumb levels).</summary>
        public const int MaxVisible = 40;
        public const int MaxDepth = 4;

        private readonly List<AceRadialNodeLayout> layout = new List<AceRadialNodeLayout>(MaxVisible + 1);
        private readonly List<AceVec2> points = new List<AceVec2>(MaxVisible + 1);
        private AceRadialAction rootAction;
        private AceRadialNode root;
        private float collectedAt;
        private string activePath;
        private string hoveredPath;
        private float hoverSince;
        private float pathChangedAt;
        private float expandStart;
        private bool animate;
        private int hoveredIndex = -1;

        public IReadOnlyList<AceRadialNodeLayout> Layout => layout;
        public int HoveredIndex => hoveredIndex;
        public bool HasOptions => root != null && root.Children.Count > 0;

        /// <summary>The hovered option, or null over empty space or the centre node.</summary>
        public AceRadialNode Hovered =>
            hoveredIndex > 0 && hoveredIndex < layout.Count ? layout[hoveredIndex].Node : null;

        public void Open(AceRadialAction menuRoot, float now)
        {
            rootAction = menuRoot;
            Collect(now);
            activePath = root?.Path;
            hoveredPath = null;
            hoveredIndex = -1;
            hoverSince = now;
            pathChangedAt = now;
            expandStart = now;
            animate = true;
        }

        public void Close()
        {
            rootAction = null;
            root = null;
            activePath = null;
            hoveredPath = null;
            hoveredIndex = -1;
            layout.Clear();
            points.Clear();
        }

        /// <param name="unit">Pixels per 1080p pixel, so the menu keeps its size on any screen.</param>
        public void Tick(AceVec2 cursor, AceVec2 center, float unit, float now)
        {
            if (rootAction == null) return;
            if (now - collectedAt >= RecollectSec)
            {
                Collect(now);
                if (activePath != null && Find(root, activePath) == null) SetActive(root?.Path, now);
            }

            BuildLayout(center, unit, now);
            hoveredIndex = AceRadialMath.FindClosest(cursor, points, AceRadialMath.HoverRadiusPx * unit);
            string hovered = hoveredIndex >= 0 ? layout[hoveredIndex].Node.Path : null;
            if (hovered != hoveredPath)
            {
                hoveredPath = hovered;
                hoverSince = now;
            }

            if (hovered == null || now - hoverSince < DwellSec || now - pathChangedAt < DwellSec) return;
            // A branch expands; a leaf collapses anything deeper than its own level.
            string target = layout[hoveredIndex].Node.IsBranch ? hovered : Parent(hovered);
            if (target != null && target != activePath) SetActive(target, now);
        }

        private static string Parent(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : null;
        }

        /// <summary>Click on a branch: expand it now instead of waiting for the dwell.</summary>
        public bool ExpandHovered(float now)
        {
            AceRadialNode node = Hovered;
            if (node == null || !node.IsBranch) return false;
            SetActive(node.Path, now);
            return true;
        }

        /// <summary>
        /// The hovered leaf's action if it may run right now (conditions re-checked, as ACE
        /// does on key release); null for a branch, a disabled option or empty space.
        /// </summary>
        public AceRadialAction HoveredRunnable()
        {
            AceRadialNode node = Hovered;
            if (node == null || node.IsBranch || !node.Enabled || node.Action.Run == null) return null;
            AceRadialAction action = node.Action;
            return action.IsVisible() && action.IsEnabled() ? action : null;
        }

        private void Collect(float now)
        {
            root = AceRadialNode.Collect(rootAction, MaxNodes, MaxDepth);
            collectedAt = now;
        }

        private void SetActive(string path, float now)
        {
            if (path == activePath) return;
            // ACE skips the pop-out when stepping back up to an ancestor.
            animate = activePath == null || !IsAncestorOrSelf(path, activePath);
            activePath = path;
            pathChangedAt = now;
            expandStart = now;
        }

        private void BuildLayout(AceVec2 center, float unit, float now)
        {
            layout.Clear();
            points.Clear();
            if (root == null) return;
            float progress = animate ? (now - expandStart) / ExpandSec : 1f;
            Place(root, center, 0f, AceRadialMath.RootSpanDeg, 0, false, 1f, unit, progress);
        }

        private void Place(AceRadialNode node, AceVec2 position, float angle, float maxSpan, int level,
            bool currentLevel, float scale, float unit, float progress)
        {
            if (layout.Count > MaxVisible) return;
            bool onPath = activePath != null && IsAncestorOrSelf(node.Path, activePath);
            layout.Add(new AceRadialNodeLayout(node, position, level, onPath, currentLevel, scale));
            points.Add(position);
            if (!onPath || node.Children.Count == 0) return;

            bool newest = node.Path == activePath;
            float childScale = newest ? AceRadialMath.ExpandScale(progress) : 1f;
            float[] angles = AceRadialMath.FanAngles(node.Children.Count, angle, maxSpan, out float interval);
            float radius = AceRadialMath.BaseRadiusPx * unit * AceRadialMath.RadiusFactor(interval) * childScale;
            for (int i = 0; i < angles.Length; i++)
            {
                AceVec2 at = position + (AceVec2.FromAngle(angles[i]) * radius);
                Place(node.Children[i], at, angles[i], AceRadialMath.SubLevelSpanDeg, level + 1,
                    newest, childScale, unit, progress);
            }
        }

        private static bool IsAncestorOrSelf(string candidate, string path) =>
            candidate == path || path.StartsWith(candidate + "/", StringComparison.Ordinal);

        private static AceRadialNode Find(AceRadialNode node, string path)
        {
            if (node == null) return null;
            if (node.Path == path) return node;
            if (!IsAncestorOrSelf(node.Path, path)) return null;
            for (int i = 0; i < node.Children.Count; i++)
            {
                AceRadialNode found = Find(node.Children[i], path);
                if (found != null) return found;
            }
            return null;
        }
    }
}
