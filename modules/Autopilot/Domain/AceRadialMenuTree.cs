using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using UnityEngine;

namespace BoscaliSummer.Features.Autopilot.Domain
{
    /// <summary>
    /// Manages the hierarchical state, active nodes, hover timing, and branch expansion
    /// of the ACE3 radial menu.
    /// </summary>
    internal sealed class AceRadialMenuTree
    {
        private readonly List<AceRadialAction> rootActions = new List<AceRadialAction>();
        private readonly List<AceRadialAction> activePath = new List<AceRadialAction>();
        private readonly List<AceRadialNodeLayout> currentLayoutNodes = new List<AceRadialNodeLayout>();

        private int hoveredIndex = -1;
        private float hoverStartTime;
        private float branchExpansionStartTime;
        private bool isBranchExpanded;
        private float selectorRotationDeg;

        public IReadOnlyList<AceRadialAction> RootActions => rootActions;
        public IReadOnlyList<AceRadialNodeLayout> CurrentLayoutNodes => currentLayoutNodes;
        public int HoveredIndex => hoveredIndex;
        public float SelectorRotationDeg => selectorRotationDeg;
        public bool IsBranchExpanded => isBranchExpanded;

        public void AddRootAction(AceRadialAction action)
        {
            if (action != null)
            {
                rootActions.Add(action);
            }
        }

        public void ClearRootActions()
        {
            rootActions.Clear();
            ResetNavigation();
        }

        public void ResetNavigation()
        {
            activePath.Clear();
            hoveredIndex = -1;
            hoverStartTime = 0f;
            branchExpansionStartTime = 0f;
            isBranchExpanded = false;
        }

        /// <summary>
        /// Updates the radial tree state, cursor hit-testing, branch auto-expansion, and layout positions.
        /// </summary>
        public void Update(Vector2 cursorPosition, Vector2 menuCenter, Aircraft aircraft, float currentTime, float deltaTime)
        {
            selectorRotationDeg = AceRadialMath.UpdateSelectorRotation(selectorRotationDeg, deltaTime);

            // Rebuild layout nodes based on active hierarchy
            currentLayoutNodes.Clear();

            // 1. Root ring descriptors
            var rootDescriptors = new List<AceRadialActionDescriptor>(rootActions.Count);
            for (int i = 0; i < rootActions.Count; i++)
            {
                AceRadialAction act = rootActions[i];
                bool allowed = act.IsAllowed(aircraft);
                bool hasChildren = act.GetChildren(aircraft).Count > 0;
                rootDescriptors.Add(new AceRadialActionDescriptor(act.Id, act.DisplayName, hasChildren, allowed));
            }

            AceVec2 center = new AceVec2(menuCenter.x, menuCenter.y);
            List<AceRadialNodeLayout> roots = AceRadialMath.CalculateRootLayout(rootDescriptors, center);
            currentLayoutNodes.AddRange(roots);

            // 2. If a branch is active/expanded, layout its children
            if (activePath.Count > 0)
            {
                AceRadialAction parentAction = activePath[activePath.Count - 1];
                int parentRootIdx = rootActions.IndexOf(parentAction);
                if (parentRootIdx >= 0 && parentRootIdx < roots.Count)
                {
                    AceRadialNodeLayout parentLayout = roots[parentRootIdx];
                    List<AceRadialAction> children = parentAction.GetChildren(aircraft);

                    var childDescriptors = new List<AceRadialActionDescriptor>(children.Count);
                    for (int i = 0; i < children.Count; i++)
                    {
                        AceRadialAction ch = children[i];
                        bool allowed = ch.IsAllowed(aircraft);
                        bool hasGrandChildren = ch.GetChildren(aircraft).Count > 0;
                        childDescriptors.Add(new AceRadialActionDescriptor(ch.Id, ch.DisplayName, hasGrandChildren, allowed));
                    }

                    float progress = isBranchExpanded
                        ? Mathf.Clamp01((currentTime - branchExpansionStartTime) / AceRadialMath.ExpansionDurationSec)
                        : 0f;

                    List<AceRadialNodeLayout> branchNodes = AceRadialMath.CalculateBranchLayout(
                        childDescriptors,
                        parentLayout.Position,
                        parentLayout.AngleDeg,
                        branchRadius: AceRadialMath.DefaultBranchRadius,
                        expansionProgress: progress);

                    currentLayoutNodes.AddRange(branchNodes);
                }
            }

            // 3. Hit testing against all currently rendered nodes
            int prevHovered = hoveredIndex;
            AceVec2 cur = new AceVec2(cursorPosition.x, cursorPosition.y);
            hoveredIndex = AceRadialMath.FindClosestNodeIndex(cur, currentLayoutNodes);

            if (hoveredIndex != prevHovered)
            {
                hoverStartTime = currentTime;
            }

            // 4. Branch auto-expansion logic (ACE3 fnc_render.sqf)
            if (hoveredIndex >= 0 && hoveredIndex < currentLayoutNodes.Count)
            {
                AceRadialNodeLayout hoveredNode = currentLayoutNodes[hoveredIndex];
                AceRadialAction matchingAction = FindActionById(hoveredNode.ActionId, aircraft);

                // If hovering a root node with children
                if (hoveredNode.Depth == 0 && hoveredNode.IsBranch && matchingAction != null)
                {
                    if (activePath.Count == 0 || activePath[0] != matchingAction)
                    {
                        if (currentTime - hoverStartTime >= 0.12f)
                        {
                            activePath.Clear();
                            activePath.Add(matchingAction);
                            branchExpansionStartTime = currentTime;
                            isBranchExpanded = true;
                        }
                    }
                }
                // If hovering run-on-hover action
                if (matchingAction != null && matchingAction.RunOnHover && hoveredNode.IsAllowed)
                {
                    matchingAction.Execute(aircraft);
                }
            }
            else
            {
                // If cursor drifted close to center, collapse branch
                float distToCenter = Vector2.Distance(cursorPosition, menuCenter);
                if (distToCenter < 40f && activePath.Count > 0)
                {
                    ResetNavigation();
                }
            }
        }

        private AceRadialAction FindActionById(string id, Aircraft aircraft)
        {
            for (int i = 0; i < rootActions.Count; i++)
            {
                if (rootActions[i].Id == id) return rootActions[i];
                List<AceRadialAction> children = rootActions[i].GetChildren(aircraft);
                for (int j = 0; j < children.Count; j++)
                {
                    if (children[j].Id == id) return children[j];
                }
            }
            return null;
        }

        /// <summary>
        /// Executes the currently hovered action upon key release or mouse click.
        /// </summary>
        public bool TriggerSelected(Aircraft aircraft)
        {
            if (hoveredIndex < 0 || hoveredIndex >= currentLayoutNodes.Count)
            {
                return false;
            }

            AceRadialNodeLayout selected = currentLayoutNodes[hoveredIndex];
            if (!selected.IsAllowed)
            {
                return false;
            }

            AceRadialAction action = FindActionById(selected.ActionId, aircraft);
            if (action == null) return false;

            // If it's a branch node that hasn't expanded yet, expand it
            if (selected.IsBranch)
            {
                if (activePath.Count == 0 || activePath[0] != action)
                {
                    activePath.Clear();
                    activePath.Add(action);
                    branchExpansionStartTime = Time.unscaledTime;
                    isBranchExpanded = true;
                    return false;
                }
            }

            // Execute the action statement
            action.Execute(aircraft);
            return true;
        }

        /// <summary>
        /// Adapts an IRadialMenuPage into an AceRadialAction branch node.
        /// </summary>
        public static AceRadialAction FromRadialPage(IRadialMenuPage page)
        {
            if (page == null) return null;

            string title = page.Title ?? "Page";
            var branch = new AceRadialAction("page." + title.ToLowerInvariant().Replace(" ", "_"), title);

            branch.WithDynamicChildren(aircraft =>
            {
                var children = new List<AceRadialAction>();
                int count = page.EntryCount;
                for (int i = 0; i < count; i++)
                {
                    int index = i;
                    string label = page.EntryLabel(index);
                    if (string.IsNullOrEmpty(label)) continue;

                    children.Add(new AceRadialAction(
                        $"page.{title}.entry_{index}",
                        label,
                        _ => page.InvokeEntry(index),
                        _ => page.EntryAllowed(index)
                    ));
                }
                return children;
            });

            return branch;
        }
    }
}
