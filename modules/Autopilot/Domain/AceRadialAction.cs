using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Autopilot.Domain
{
    /// <summary>
    /// Port of the ACE3 action model (ace_interact_menu_fnc_createAction).
    /// Represents an actionable or branching node in a hierarchical radial menu.
    /// </summary>
    internal sealed class AceRadialAction
    {
        public string Id { get; }
        public string DisplayName { get; set; }
        public string IconName { get; set; }
        public Func<Aircraft, bool> Condition { get; set; }
        public Action<Aircraft> Statement { get; set; }
        public Func<Aircraft, IEnumerable<AceRadialAction>> DynamicChildren { get; set; }
        public List<AceRadialAction> StaticChildren { get; } = new List<AceRadialAction>();
        public bool RunOnHover { get; set; }
        public bool ShowDisabled { get; set; }
        public object CustomParams { get; set; }

        public AceRadialAction(string id, string displayName, Action<Aircraft> statement = null, Func<Aircraft, bool> condition = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? id;
            Statement = statement;
            Condition = condition;
        }

        public AceRadialAction AddChild(AceRadialAction child)
        {
            if (child != null)
            {
                StaticChildren.Add(child);
            }
            return this;
        }

        public AceRadialAction WithIcon(string icon)
        {
            IconName = icon;
            return this;
        }

        public AceRadialAction WithRunOnHover(bool runOnHover = true)
        {
            RunOnHover = runOnHover;
            return this;
        }

        public AceRadialAction WithShowDisabled(bool showDisabled = true)
        {
            ShowDisabled = showDisabled;
            return this;
        }

        public AceRadialAction WithDynamicChildren(Func<Aircraft, IEnumerable<AceRadialAction>> provider)
        {
            DynamicChildren = provider;
            return this;
        }

        public bool IsAllowed(Aircraft aircraft)
        {
            try
            {
                return Condition == null || Condition(aircraft);
            }
            catch
            {
                return false;
            }
        }

        public void Execute(Aircraft aircraft)
        {
            try
            {
                Statement?.Invoke(aircraft);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AceRadialAction] Action '{Id}' failed: {ex.Message}");
            }
        }

        public List<AceRadialAction> GetChildren(Aircraft aircraft)
        {
            var result = new List<AceRadialAction>(StaticChildren);
            if (DynamicChildren != null)
            {
                try
                {
                    IEnumerable<AceRadialAction> dyn = DynamicChildren(aircraft);
                    if (dyn != null)
                    {
                        result.AddRange(dyn);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AceRadialAction] Dynamic children for '{Id}' failed: {ex.Message}");
                }
            }
            return result;
        }
    }
}
