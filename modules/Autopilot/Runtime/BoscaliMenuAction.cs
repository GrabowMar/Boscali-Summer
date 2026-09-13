using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Features.Autopilot.Runtime
{
    /// <summary>Delegate-backed native radial slice. Appearance is copied from a stock action so
    /// the wedge renders identically; the prefixes in BoscaliMenuActionPatches dispatch behaviour
    /// because the native methods are non-virtual.</summary>
    internal sealed class BoscaliMenuAction : RadialMenuAction
    {
        private static readonly FieldInfo ActionTypeField =
            AccessTools.Field(typeof(RadialMenuAction), "actionType");
        private static readonly FieldInfo IconSpriteField =
            AccessTools.Field(typeof(RadialMenuAction), "iconSprite");
        private static readonly FieldInfo BackgroundSpriteField =
            AccessTools.Field(typeof(RadialMenuAction), "backgroundSprite");
        private static readonly FieldInfo BackgroundInactiveField =
            AccessTools.Field(typeof(RadialMenuAction), "backgroundColorInactive");
        private static readonly FieldInfo BackgroundActiveField =
            AccessTools.Field(typeof(RadialMenuAction), "backgroundColorActive");

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

        public bool IsAllowed(Aircraft candidate) => allowed == null || allowed(candidate);

        public void Invoke(Aircraft candidate)
        {
            Action<Aircraft> callback = triggered;
            if (callback != null) callback(candidate);
        }

        public void CopyAppearanceFrom(RadialMenuAction template)
        {
            if (template == null || template is BoscaliMenuAction) return;
            ActionTypeField?.SetValue(this, ActionTypeField.GetValue(template));
            IconSpriteField?.SetValue(this, IconSpriteField.GetValue(template));
            BackgroundSpriteField?.SetValue(this, BackgroundSpriteField.GetValue(template));
            BackgroundInactiveField?.SetValue(this, BackgroundInactiveField.GetValue(template));
            BackgroundActiveField?.SetValue(this, BackgroundActiveField.GetValue(template));
            Caution = template.Caution;
        }
    }
}
