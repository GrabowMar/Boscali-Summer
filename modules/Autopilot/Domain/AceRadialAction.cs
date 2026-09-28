using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Autopilot.Domain
{
    /// <summary>
    /// One entry of the ACE3-style interaction menu (ace_interact_menu_fnc_createAction,
    /// reduced to what a cockpit needs): a label, an optional statement, a visibility
    /// condition, an enabled condition, and static or on-demand children. Closures capture
    /// whatever game state they need, so the tree itself stays pure and testable.
    /// </summary>
    internal sealed class AceRadialAction
    {
        private readonly List<AceRadialAction> children = new List<AceRadialAction>();
        private Func<IEnumerable<AceRadialAction>> dynamicChildren;

        public AceRadialAction(string id, string label, Action run = null,
            Func<bool> visible = null, Func<bool> enabled = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            Id = id.Replace('/', '_');
            Label = label ?? id;
            Run = run;
            Visible = visible;
            Enabled = enabled;
        }

        public string Id { get; }
        public string Label { get; }
        public Action Run { get; }
        public Func<bool> Visible { get; }
        public Func<bool> Enabled { get; }
        public AceIcon Icon { get; private set; }
        public Func<AceRadialStatus> Status { get; private set; }

        public AceRadialAction WithIcon(AceIcon icon)
        {
            Icon = icon;
            return this;
        }

        /// <summary>Live state line under the label, read each time the menu collects.</summary>
        public AceRadialAction WithStatus(Func<AceRadialStatus> status)
        {
            Status = status;
            return this;
        }

        public AceRadialAction Add(AceRadialAction child)
        {
            if (child != null) children.Add(child);
            return this;
        }

        /// <summary>Children resolved each time the menu collects (ACE insertChildren).</summary>
        public AceRadialAction WithChildren(Func<IEnumerable<AceRadialAction>> provider)
        {
            dynamicChildren = provider;
            return this;
        }

        internal bool IsVisible() => Ask(Visible);
        internal bool IsEnabled() => Ask(Enabled);

        internal AceRadialStatus ReadStatus()
        {
            if (Status == null) return AceRadialStatus.None;
            try { return Status(); }
            catch (Exception) { return AceRadialStatus.None; }
        }

        internal IEnumerable<AceRadialAction> Children()
        {
            for (int i = 0; i < children.Count; i++) yield return children[i];
            IEnumerable<AceRadialAction> extra = null;
            try { extra = dynamicChildren?.Invoke(); }
            catch (Exception) { extra = null; }
            if (extra == null) yield break;
            foreach (AceRadialAction child in extra)
                if (child != null) yield return child;
        }

        private static bool Ask(Func<bool> condition)
        {
            if (condition == null) return true;
            try { return condition(); }
            catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// Condition-filtered snapshot of an action tree (ACE collectActiveActionTree): hidden
    /// actions are dropped, branches left with no statement and no children are pruned, and
    /// the whole snapshot is capped in node count and depth.
    /// </summary>
    internal sealed class AceRadialNode
    {
        private AceRadialNode(AceRadialAction action, string path, bool enabled)
        {
            Action = action;
            Path = path;
            Enabled = enabled;
            Status = action.ReadStatus();
        }

        public AceRadialAction Action { get; }
        /// <summary>Slash-joined id chain from the root; stable across re-collection.</summary>
        public string Path { get; }
        public bool Enabled { get; }
        /// <summary>State line as read at collection (refreshed with the tree, about once a second).</summary>
        public AceRadialStatus Status { get; }
        public List<AceRadialNode> Children { get; } = new List<AceRadialNode>();
        public bool IsBranch => Children.Count > 0;

        public static AceRadialNode Collect(AceRadialAction root, int maxNodes, int maxDepth)
        {
            if (root == null) return null;
            int budget = Math.Max(0, maxNodes);
            var node = new AceRadialNode(root, root.Id, enabled: true);
            Fill(node, 1, maxDepth, ref budget);
            return node;
        }

        private static void Fill(AceRadialNode parent, int depth, int maxDepth, ref int budget)
        {
            if (depth > maxDepth) return;
            foreach (AceRadialAction action in parent.Action.Children())
            {
                if (budget <= 0) return;
                if (!action.IsVisible()) continue;
                var child = new AceRadialNode(action, parent.Path + "/" + action.Id, action.IsEnabled());
                budget--;
                Fill(child, depth + 1, maxDepth, ref budget);
                if (child.Action.Run == null && child.Children.Count == 0)
                {
                    budget++;
                    continue;
                }
                parent.Children.Add(child);
            }
        }
    }
}
