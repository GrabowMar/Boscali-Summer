#if !PUBLIC_RELEASE
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>
    /// Drone link sim hook (tests/ingame/vg-dronelink.json): Step {action:"link", do:...}, driving the same DroneLinkService calls the C-menu,
    /// the UAV MFD and the UAV terminal make, then reading back both ends:
    ///   do=status (default)  count, linked, local mode, host mode of the linked drone (SteerOrders), hands (host HAND orders of this launcher),
    ///                        headingGap (linked drone vs the pilot, degrees), feed, terminal, mfd
    ///   do=link index | next | prev | hand | auto | feed | look | zoom | terminal | close | mfd | unmap
    ///   do=wpt  east, north (metres from the linked drone): WPT for it there
    /// </summary>
    public static partial class VanguardAutomation
    {
        private static Dictionary<string, object> LinkStep(Dictionary<string, object> args, Aircraft lead)
        {
            if (!ModuleServices.TryGet(out IDroneCommand command) || !(command is DroneLinkService link))
                return Failure("VanguardAutomation", "link", "no drone link service");
            switch (Text(args, "do") ?? "status")
            {
                case "link": link.Link((int)Number(args, "index", 0f)); break;
                case "next": link.Cycle(1); break;
                case "prev": link.Cycle(-1); break;
                case "hand": link.ToggleHand(); break;
                case "auto": link.Release(); break;
                case "feed": link.ToggleFeed(); break;
                case "look": link.ToggleLook(); break;
                case "zoom": link.CycleZoom(); break;
                case "terminal": link.OpenTerminal(); break;
                case "close": UnityEngine.Object.FindObjectOfType<DroneTerminalWindow>()?.Close(); break;
                case "mfd":
                    DynamicMap map = SceneSingleton<DynamicMap>.i;
                    if (map != null && !DynamicMap.mapMaximized) map.Maximize();
                    DroneMfdPanel.Active?.OpenForAutomation();
                    break;
                case "unmap":
                    if (DynamicMap.mapMaximized) SceneSingleton<DynamicMap>.i?.Minimize();
                    break;
                case "wpt":
                    DroneView v = link.Linked;
                    if (v == null) return Failure("VanguardAutomation", "link", "no linked drone");
                    link.SendTo(v.Position + new Vector3(Number(args, "east", 0f), 0f, Number(args, "north", 6000f)));
                    break;
                case "status": break;
                default: return Failure("VanguardAutomation", "link", "unknown do");
            }
            return LinkStatus(link, lead);
        }

        private static Dictionary<string, object> LinkStatus(DroneLinkService link, Aircraft lead)
        {
            DroneView v = link.Linked;
            int hostMode = -1, hands = 0;
            if (v != null && SteerOrders.TryGet(v.Missile, out SteerOrders.Order order)) hostMode = (int)order.Mode;
            else if (v != null) hostMode = 0;
            for (int i = 0; i < link.Count; i++)
                if (SteerOrders.TryGet(link[i].Missile, out SteerOrders.Order o) && o.Mode == DroneMode.Hand) hands++;
            float gap = -1f;
            if (v != null && lead != null)
            {
                Vector3 a = v.Missile.transform.forward, b = lead.transform.forward;
                gap = Vector2.Angle(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            }
            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["count"] = link.Count,
                ["linked"] = link.LinkedIndex,
                ["linkedName"] = v != null ? v.Callsign : "",
                ["mode"] = v != null ? (int)v.Mode : -1,
                ["hostMode"] = hostMode,
                ["hands"] = hands,
                ["headingGap"] = gap,
                ["quality"] = v != null ? v.Quality : 0f,
                ["rangeKm"] = v != null ? v.RangeM / 1000f : 0f,
                ["speed"] = v != null && v.Missile != null ? v.Missile.speed : 0f,
                ["lens"] = link.LensWord,
                ["lensEl"] = link.LensElevation,
                ["spiKm"] = link.HasSpi ? link.SpiSlantM / 1000f : -1f,
                ["feed"] = link.Feed != null ? 1 : 0,
                ["feedShown"] = link.FeedShown ? 1 : 0,
                ["terminal"] = link.TerminalOpen ? 1 : 0,
                ["mfd"] = DroneMfdPanel.Active != null && DroneMfdPanel.Active.Showing ? 1 : 0,
            };
        }
    }
}
#endif
