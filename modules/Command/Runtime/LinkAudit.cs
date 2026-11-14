#if !PUBLIC_RELEASE
using System.Collections.Generic;
using NOAvionics;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Sim hook: every link chip built so far must land on a real console and, when it names one, a real tab. Open the rail keys first
    /// (ScrollAutomation.Open) so each console builds and registers its links; then Check tries every target.
    /// </summary>
    internal static class LinkAudit
    {
        /// <summary>Maximises the map and opens one rail key (args "code"), so its console builds and registers its link rows.</summary>
        public static Dictionary<string, object> Open(Dictionary<string, object> args)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            string code = args != null && args.TryGetValue("code", out object v) ? v as string : null;
            bool found = code != null && Presentation.MapUi.MfdRail.Open(code);
            return new Dictionary<string, object> { { "ok", true }, { "code", code ?? "" }, { "found", found ? 1 : 0 } };
        }

        public static Dictionary<string, object> Check(Dictionary<string, object> args)
        {
            var bad = new List<string>();
            foreach (string target in AvConsole.LinkTargets)
            {
                int bar = target.IndexOf('|');
                string key = target.Substring(0, bar), page = target.Substring(bar + 1);
                AvConsole c = AvConsole.Find(key);
                if (c == null) bad.Add(target + " NO CONSOLE");
                else if (page.Length > 0 && !c.ShowPage(page)) bad.Add(target + " NO PAGE");
            }
            return new Dictionary<string, object>
            {
                { "ok", true }, { "targets", AvConsole.LinkTargets.Count }, { "bad", bad.Count }, { "list", string.Join("; ", bad) },
            };
        }
    }
}
#endif
