using System;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Exclude non-squadron encyclopedia placeholders such as the April Fools UFO from Supply and
    /// Loadout.</summary>
    internal static class AirframeCatalogPolicy
    {
        public static bool IsHiddenFromPanels(string unitName, string code = null, string jsonKey = null) =>
            IsQuestionMarkPlaceholder(unitName) || IsQuestionMarkPlaceholder(code) || IsQuestionMarkPlaceholder(jsonKey)
            || string.Equals(jsonKey, "UFO", StringComparison.OrdinalIgnoreCase);

        public static bool IsQuestionMarkPlaceholder(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++)
                if (value[i] != '?') return false;
            return true;
        }
    }
}
