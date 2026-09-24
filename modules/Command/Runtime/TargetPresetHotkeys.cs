using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.MissionEditorScripts;
using NuclearOption.UI;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Runtime
{
    /// <summary>
    /// Applies the three quick-slot target presets from the keyboard while flying. Input
    /// only — the preset library stays scene-independent; a missing selector is a no-op,
    /// and text entry, pause, the leaderboard and the radial wheel all own the keys first.
    /// </summary>
    internal sealed class TargetPresetHotkeys : MonoBehaviour, ISceneService
    {
        private CommandSettings settings;

        public void Configure(CommandSettings config) => settings = config;

        private void Update()
        {
            if (settings == null || SceneSingleton<RadialMenuMain>.i == null ||
                SceneSingleton<TargetListSelector>.i == null ||
                !GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null || aircraft.disabled) return;
            if (InputFieldChecker.InsideInputField || GameplayUI.GameIsPaused ||
                RadialMenuMain.IsInUse() || LeaderboardMenu.IsOpen()) return;

            ApplyKey(settings.TargetPresetKey1.Value, 0);
            ApplyKey(settings.TargetPresetKey2.Value, 1);
            ApplyKey(settings.TargetPresetKey3.Value, 2);
        }

        private static void ApplyKey(KeyCode key, int slot)
        {
            if (key == KeyCode.None || !Input.GetKeyDown(key)) return;
            string name = TargetPresetRuntime.QuickSlotName(slot);
            if (name.Length == 0) return;
            TargetPresetRuntime.TryApplyByName(SceneSingleton<TargetListSelector>.i, name);
        }

        public void ResetForScene()
        {
        }
    }
}
