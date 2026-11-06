#if !PUBLIC_RELEASE
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using TMPro;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Hook for the unattended MIS check: <c>Open {page}</c> maximises the map, presses the MIS rail key (the click path) and
    /// shows that page; <c>Pick {index}</c> clicks a job-board slip; <c>Read</c> reports the page and its text. Dev tooling.
    /// </summary>
    public static class MisAutomation
    {
        public static Dictionary<string, object> Open(Dictionary<string, object> args)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            MfdRailKey key = null;
            foreach (MfdRailKey k in MfdRail.Keys) if (k.Entry.Code == "MIS") key = k;
            if (key == null) return Failure("MisAutomation", "Open", "no MIS rail key");
            if (key.Screen != null && !key.Screen.isActive) key.Source.onClick.Invoke();
            AvConsole console = VanillaMfdRebuild.ConsoleForAutomation(VanillaMfdPanelId.Mis);
            if (console == null) return Failure("MisAutomation", "Open", "MIS console not bound yet");
            console.SetPage((int)Number(args, "page", 2));
            console.Ticker.TickNow();
            return Read(null);
        }

        public static Dictionary<string, object> Pick(Dictionary<string, object> args)
        {
            bool ok = VanillaMfdRebuild.PickContractForAutomation((int)Number(args, "index", 0));
            VanillaMfdRebuild.ConsoleForAutomation(VanillaMfdPanelId.Mis)?.Ticker.TickNow();
            Dictionary<string, object> result = Read(null);
            result["picked"] = ok;
            return result;
        }

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            AvConsole console = VanillaMfdRebuild.ConsoleForAutomation(VanillaMfdPanelId.Mis);
            var result = new Dictionary<string, object> { ["ok"] = console != null };
            if (console == null) return result;
            result["page"] = console.CurrentPage;
            var text = new List<string>();
            foreach (TMP_Text t in console.Page(console.CurrentPage).Content.GetComponentsInChildren<TMP_Text>(false))
                if (!string.IsNullOrEmpty(t.text)) text.Add(t.text);
            string joined = string.Join("|", text);
            result["text"] = joined.Length > 1500 ? joined.Substring(0, 1500) : joined;
            return result;
        }
    }
}
#endif
