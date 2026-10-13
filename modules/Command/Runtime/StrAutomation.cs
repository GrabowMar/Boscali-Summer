#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Hooks for the unattended in-game STR proof (annex 05 P11). Arguments and results are dictionaries of numbers and strings; results are nested, no
    /// key contains a dot, and an unknown is -1 (never NaN: the sim writes NaN as null). <c>Press</c> goes around the EventSystem but runs the same gate
    /// and the same hub call a click does, by control id (<c>StrControlIds</c>); the map overlay's <c>Probe</c> and <c>ProbeAt</c> belong to the overlay slice.
    /// Dev tooling: nothing here runs unless called.
    /// </summary>
    public static partial class StrAutomation
    {
        private static readonly string[] PostureIds = { "posture.cautious", "posture.steady", "posture.bold" };

        /// <summary>Maximise the map, select the STR screen and show a page: <c>{page: "board"|"sitrep"|"info"}</c> or the legacy <c>{tab: 0..2}</c>.</summary>
        public static Dictionary<string, object> Open(Dictionary<string, object> args)
        {
            StrMfdPanel panel = StrMfdPanel.Active;
            if (panel == null) return Fail("Open", "no STR panel in this scene");
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            panel.OpenForAutomation(PageOf(args));
            return Read(null);
        }

        private static int PageOf(Dictionary<string, object> args)
        {
            switch (Text(args, "page"))
            {
                case "board": return 0;
                case "sitrep": return 1;
                case "info": return 2;
                default: return Mathf.Clamp((int)Number(args, "tab", 0f), 0, 2);
            }
        }

        /// <summary>
        /// <c>{id}</c>: the same code path as a click, by control id. Returns ok (the gate allowed and the verb was taken), pressed (a verb went out), reasonCode
        /// (StrWhy), reason and reasonLen. Mutates the game, so it runs in a nomodkit scenario only.
        /// </summary>
        public static Dictionary<string, object> Press(Dictionary<string, object> args)
        {
            if (!InScenario("Press", out Dictionary<string, object> refusal)) return refusal;
            return PressId("Press", Text(args, "id"));
        }

        /// <summary><c>{id}</c> of a board item ("map.tag.prop.1", "front.2", "sortie.1", "effort", "post.1"): hover it through the hub. Client state only, guarded with Press.</summary>
        public static Dictionary<string, object> Hover(Dictionary<string, object> args) => PointAt("Hover", args, false);

        /// <summary><c>{id}</c> of a board item: select it through the hub (without flying the map); <c>link.selectedKind</c> proves it.</summary>
        public static Dictionary<string, object> Select(Dictionary<string, object> args) => PointAt("Select", args, true);

        /// <summary>
        /// <c>{verb, index, arg}</c>: an alias of Press for the README scenario and the trailer. pick and accept and wing take the 1-based ticket, replan arms
        /// and confirms in one call, posture takes arg 0..2 (or cycles to the next), redirect takes the index-th front lane that has an offer.
        /// </summary>
        public static Dictionary<string, object> Verb(Dictionary<string, object> args)
        {
            if (!InScenario("Verb", out Dictionary<string, object> refusal)) return refusal;
            StrViewHub hub = StrViewHub.Active;
            if (hub == null) return Fail("Verb", "no STR hub in this scene");
            hub.TickNow();
            StrViewModel vm = hub.Current;
            int index = (int)Number(args, "index", 1f);
            string verb = Text(args, "verb") ?? "";
            string id;
            switch (verb)
            {
                case "pick": id = StrControlIds.Prop(index, "pick"); break;
                case "accept": id = StrControlIds.Sortie(index, "accept"); break;
                case "wing": id = StrControlIds.Sortie(index, "wing"); break;
                case "replan": id = StrControlIds.Replan; break;
                case "posture":
                    int p = args != null && args.ContainsKey("arg") ? Mathf.Clamp((int)Number(args, "arg", 0f), 0, 2) : ((int)vm.Posture + 1) % 3;
                    id = PostureIds[p];
                    break;
                case "redirect":
                    int lane = StrControlIds.RedirectLane(vm, index);
                    if (lane == 0) return Fail("Verb", "no front lane number " + index + " that the staff offers");
                    id = StrControlIds.Front(lane, "redirect");
                    break;
                default: return Fail("Verb", "unknown verb '" + verb + "'");
            }
            Dictionary<string, object> r = PressId("Verb", id);
            // The first REPLAN press only arms; a script that names the verb means the call-off, so it confirms inside the four seconds.
            if (verb == "replan" && Convert.ToInt32(r["ok"]) == 1 && Convert.ToInt32(r["pressed"]) == 0) r = PressId("Verb", id);
            r["id"] = id;
            return r;
        }

        /// <summary>
        /// <c>{act (accept|wing), chip (default STRIKE)}</c>: presses the first ticket with that family chip the verb applies to (accept needs an offered one, wing an accepted one)
        /// through the panel's own control, so the gate, the verbs and the wing desk are the production path. Returns the ticket's index and the press result.
        /// </summary>
        public static Dictionary<string, object> Ticket(Dictionary<string, object> args)
        {
            if (!InScenario("Ticket", out Dictionary<string, object> refusal)) return refusal;
            StrViewHub hub = StrViewHub.Active;
            if (hub == null) return Fail("Ticket", "no STR hub in this scene");
            hub.TickNow();
            StrViewModel vm = hub.Current;
            string act = Text(args, "act") ?? "wing";
            string chip = Text(args, "chip") ?? "STRIKE";
            for (int i = 0; i < vm.SortieCount; i++)
            {
                StrSortieRow row = vm.Sorties[i];
                if (row.ChipText != chip) continue;
                if (act == "accept" ? !row.Offered : !row.Active) continue;
                Dictionary<string, object> r = PressId("Ticket", StrControlIds.Sortie(i + 1, act));
                r["index"] = i + 1; r["contractId"] = row.ContractId;
                return r;
            }
            return new Dictionary<string, object> { { "ok", 0 }, { "pressed", 0 }, { "index", 0 }, { "reason", "no matching ticket" }, { "reasonLen", 18 } };
        }

        /// <summary><c>{on}</c>: flip Command/MapRelief3D for the flat-map run. Mutates the client config, so it runs in a scenario only.</summary>
        public static Dictionary<string, object> Relief(Dictionary<string, object> args)
        {
            if (!InScenario("Relief", out Dictionary<string, object> refusal)) return refusal;
            var entry = Plugin.Settings?.Command?.MapRelief3D;
            if (entry == null) return Fail("Relief", "no MapRelief3D setting");
            entry.Value = Bool(args, "on");
            return new Dictionary<string, object> { { "ok", true }, { "relief", entry.Value ? 1 : 0 } };
        }

        private static Dictionary<string, object> PressId(string hook, string id)
        {
            // The overlay's own controls: the layer toggles and the card's buttons (they live on the map, not on the panel).
            if (id != null && (id.StartsWith("layer.") || id.StartsWith("map.card.")))
            {
                StrMapOverlay overlay = StrMapOverlay.Active;
                if (overlay == null) return Fail(hook, "no STR overlay in this scene");
                StrViewHub.Active?.TickNow();
                overlay.RefreshNow();
                return Result(overlay.Controls.Press(id));
            }
            StrMfdPanel panel = StrMfdPanel.Active;
            if (panel == null || panel.Console == null) return Fail(hook, "no STR screen is built yet (open the map first)");
            // The ids belong to the model the panel last bound: bring both up to date before pressing, so a press never meets a stale id.
            StrViewHub.Active?.TickNow();
            panel.BindNow();
            return Result(panel.Controls.Press(id));
        }

        private static Dictionary<string, object> Result(StrControls.PressResult r) => new Dictionary<string, object>
        {
            { "ok", r.Ok ? 1 : 0 }, { "pressed", r.Act.Sent ? 1 : 0 }, { "reasonCode", (int)r.Act.Why },
            { "reason", r.Act.Text }, { "reasonLen", r.Act.Text.Length },
        };

        private static Dictionary<string, object> PointAt(string hook, Dictionary<string, object> args, bool select)
        {
            if (!InScenario(hook, out Dictionary<string, object> refusal)) return refusal;
            StrViewHub hub = StrViewHub.Active;
            if (hub == null) return Fail(hook, "no STR hub in this scene");
            hub.TickNow();
            string id = Text(args, "id");
            if (!StrControlIds.TryItem(hub.Current, id, out StrItemId item)) return Fail(hook, "no board item named '" + id + "'");
            if (select) hub.Select(item, false); else hub.Hover(item);
            StrUiState ui = hub.Ui;
            return new Dictionary<string, object>
            {
                { "ok", true }, { "selectedKind", (int)ui.Selected.Kind }, { "selectedKey", ui.Selected.Key }, { "hoverKind", (int)ui.Hover.Kind },
            };
        }

        private static bool InScenario(string hook, out Dictionary<string, object> refusal)
        {
            refusal = null;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))) return true;
            refusal = Fail(hook, "requires a nomodkit scenario");
            return false;
        }

        /// <summary>The panel, the hub's view of the model, the map's pose, the link state and the model's differences from the host's own snapshot (all 0).</summary>
        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            StrMfdPanel panel = StrMfdPanel.Active;
            var r = new Dictionary<string, object>
            {
                { "panel", new Dictionary<string, object>
                    {
                        { "present", panel != null ? 1 : 0 },
                        { "screenActive", panel != null && panel.ScreenActiveForAutomation ? 1 : 0 },
                        { "page", panel != null ? panel.PageForAutomation : -1 },
                    } },
            };
            AddHub(r);
            AddOverlay(r);
            r["ok"] = true;   // a readout; the blocks above say what is missing
            return r;
        }

        private static Dictionary<string, object> Fail(string hook, string error)
        {
            Debug.LogWarning("[StrAutomation] " + hook + ": " + error);
            return new Dictionary<string, object> { { "ok", false }, { "error", error } };
        }
    }
}
#endif
