using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The game's pause key, held off while a WMC text field has the keyboard or a map order is armed (spec bezel v2 §6: Esc
    /// cancels those instead of pausing the game), through one <see cref="KeyHold"/> so the two never restore it under each other.
    /// The key itself goes through Core's <c>PauseKey</c> under one owner, shared with the map's own tools; while held it is
    /// asserted every frame (the game turns it back on when a menu closes).</summary>
    internal static class PauseKeyHold
    {
        private const string Owner = "wing.wmc";
        private static readonly KeyHold hold = new KeyHold();

        public static void Set(int owner, bool on, bool now = false)
        {
            hold.Set(owner, on, Time.frameCount, now);
            Apply();
        }

        /// <summary>The panel went away (a mission ended, a reset): every holder lets go now, so the pause key is never left off.</summary>
        public static void ReleaseAll()
        {
            hold.Clear();
            Apply();
        }

        /// <summary>Every frame (WmcPanel.Tick).</summary>
        public static void Tick() => Apply();

        // The saving and putting back is Core's shared PauseKey, so WMC and the map's own tools never restore it under each other.
        private static void Apply() => Core.Ui.PauseKey.Hold(Owner, hold.Held(Time.frameCount));
    }
}
