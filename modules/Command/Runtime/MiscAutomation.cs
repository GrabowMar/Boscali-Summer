#if !PUBLIC_RELEASE
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using TMPro;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Hook for the unattended MISC check: <c>Open {tab}</c> maximises the map, presses the MISC rail key's bezel button (the click path) and
    /// shows the tab with that label (<c>accept</c> first answers the DEV ACCESS warning with ACCEPT); <c>Read</c> reports where the key sits on the rail, which tabs show, the title and the page text. Dev tooling.
    /// </summary>
    public static class MiscAutomation
    {
        public static Dictionary<string, object> Open(Dictionary<string, object> args)
        {
            MiscMfdPanel panel = MiscMfdPanel.Active;
            if (panel == null) return Failure("MiscAutomation", "Open", "no MISC panel in this scene");
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            MfdRailKey key = Key();
            if (key != null && key.Screen != null && !key.Screen.isActive) key.Source.onClick.Invoke();
            if (Bool(args, "accept")) DevAccess.Accept(); // the player's ACCEPT on the DEV ACCESS page, for the unattended check
            string tab = Text(args, "tab");
            AvConsole console = panel.ConsoleForAutomation;
            int index = tab == null ? -1 : panel.TabIndexForAutomation(tab);
            if (console != null && index >= 0) console.SetPage(index);
            console?.Ticker.TickNow(); // a closed console does not tick: catch its tabs up before reading them
            return Read(null);
        }

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            MiscMfdPanel panel = MiscMfdPanel.Active;
            var result = new Dictionary<string, object> { ["ok"] = panel != null };
            if (panel == null) return result;
            IReadOnlyList<MfdRailKey> keys = MfdRail.Keys;
            int at = -1;
            for (int i = 0; i < keys.Count; i++) if (keys[i].Entry.Code == "MISC") at = i;
            result["railIndex"] = at;
            result["railCount"] = keys.Count;
            result["railLast"] = at >= 0 && at == keys.Count - 1;
            MfdRailKey key = Key();
            result["screenActive"] = key != null && key.Screen != null && key.Screen.isActive;
            result["tabs"] = string.Join(",", panel.ShownTabsForAutomation());
            AvConsole console = panel.ConsoleForAutomation;
            if (console != null)
            {
                result["page"] = console.CurrentPage;
                var text = new List<string>();
                foreach (TMP_Text t in console.Page(console.CurrentPage).Content.GetComponentsInChildren<TMP_Text>(false))
                    if (!string.IsNullOrEmpty(t.text)) text.Add(t.text);
                string joined = string.Join("|", text);
                result["text"] = joined.Length > 900 ? joined.Substring(0, 900) : joined;
                var title = new List<string>();
                foreach (TMP_Text t in console.Root.GetComponentsInChildren<TMP_Text>(false))
                    if (t.text.StartsWith("MISC")) title.Add(t.text);
                result["title"] = string.Join("|", title);
            }
            return result;
        }

        private static MfdRailKey Key()
        {
            foreach (MfdRailKey key in MfdRail.Keys) if (key.Entry.Code == "MISC") return key;
            return null;
        }
    }
}
#endif
