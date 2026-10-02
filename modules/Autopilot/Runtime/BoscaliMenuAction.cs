using System;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    internal sealed class BoscaliMenuAction : RadialMenuAction
    {
        private Func<Aircraft, bool> allowed;
        private Action<Aircraft> triggered;

        public static BoscaliMenuAction Create(string label, Action<Aircraft> trigger, Func<Aircraft, bool> allowed = null)
        {
            BoscaliMenuAction action = ScriptableObject.CreateInstance<BoscaliMenuAction>();
            action.name = label;
            action.DisplayName = label;
            action.triggered = trigger;
            action.allowed = allowed;
            return action;
        }

        /// <summary>Re-point a pooled wedge at a contributed page entry without reallocating it.</summary>
        public void Configure(string label, Action<Aircraft> trigger, Func<Aircraft, bool> isAllowed = null)
        {
            name = label;
            DisplayName = label;
            triggered = trigger;
            allowed = isAllowed;
        }

        public bool IsAllowed(Aircraft candidate) => allowed == null || allowed(candidate);

        public void Invoke(Aircraft candidate)
        {
            Action<Aircraft> callback = triggered;
            if (callback != null) callback(candidate);
        }

        /// <summary>Overrides just the icon glyph, leaving the wedge background/colour/layout native.</summary>
        public void SetIcon(Sprite icon) => GameAccess.SetRadialIconSprite(this, icon);

        public void CopyAppearanceFrom(RadialMenuAction template)
        {
            if (template == null || template is BoscaliMenuAction) return;
            GameAccess.SetRadialActionType(this, GameAccess.GetRadialActionType(template));
            GameAccess.CopyRadialAppearance(this, template);
            Caution = template.Caution;
        }
    }
}
