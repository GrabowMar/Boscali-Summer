using System.Collections.Generic;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Core.Diagnostics
{
    /// <summary>Sim-only: apply a SET PERF preset (0 QUALITY, 1 BALANCED, 2 FAST) the way the PERF strip does, and report
    /// every row a preset drives with its value now.</summary>
    public static class ClientPresetAutomation
    {
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            ClientSettingsBoard board = ClientSettingsBoard.Live;
            if (board == null) return new Dictionary<string, object> { ["ok"] = false };
            if (args != null && args.TryGetValue("preset", out object p)) board.ApplyPreset(System.Convert.ToInt32(p));
            // "off": comma list of module ids to switch off live (the mission's Boscali Systems selection); "" turns all back on
            if (args != null && args.TryGetValue("off", out object off))
                BoscaliSummer.Core.Game.MissionProfile.SetDisabled(BoscaliSummer.Core.Game.MissionProfile.Parse(off as string ?? ""));
            var result = new Dictionary<string, object> { ["ok"] = true };
            for (int i = 0; i < 3; i++) result["matches" + i] = board.MatchesPreset(i);
            foreach (ClientSetting row in board.Rows)
                if (row is ClientSettingToggle t && t.Presets != null) result[row.Label] = t.Value;
                else if (row is ClientSettingChoice c && c.Presets != null) result[row.Label] = c.Entry.BoxedValue.ToString();
            return result;
        }
    }
}
