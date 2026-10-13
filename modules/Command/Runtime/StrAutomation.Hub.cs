#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    // The hub's read-back and the map locate (annex 05 P11): the nested view, map, link and check blocks of Read, and Focus.
    public static partial class StrAutomation
    {
        // The last point Focus flew to: the relief eases there over the next frames, so the offset is read later, by Read.
        private static float focusX = float.NaN, focusZ = float.NaN;
        // Distinct operation ids seen since the scene started: proves a redirect without comparing two reads.
        private static readonly HashSet<int> operationIds = new HashSet<int>();

        /// <summary>
        /// Fly the open map to a point: {x, z}, or the place of a board item through the hub, the path a click takes: {kind: effort|proposal|front|sortie,
        /// index} (1-based). The result carries the map block; the offset of a relief locate settles over the next seconds.
        /// </summary>
        public static Dictionary<string, object> Focus(Dictionary<string, object> args)
        {
            float x = Number(args, "x", float.NaN), z = Number(args, "z", float.NaN);
            string kind = Text(args, "kind");
            if (!string.IsNullOrEmpty(kind))
            {
                StrViewHub hub = StrViewHub.Active;
                if (hub == null) return Fail("Focus", "no STR hub in this scene");
                hub.TickNow();
                StrItemId id = Item(hub.Current, kind, (int)Number(args, "index", 1f));
                if (id.IsNone || !StrAnchors.TryGet(hub.Current, id, out x, out z)) return Fail("Focus", "no " + kind + " number " + (int)Number(args, "index", 1f) + " with a place on the board");
                hub.Select(id, true);
            }
            else if (!MapFocus.To(x, z, true)) return Fail("Focus", "the map is closed or the point is not finite");
            focusX = x; focusZ = z;
            var r = new Dictionary<string, object> { { "ok", true }, { "x", x }, { "z", z } };
            r["map"] = MapBlock();
            return r;
        }

        private static StrItemId Item(StrViewModel vm, string kind, int index)
        {
            int i = index - 1;
            switch (kind)
            {
                case "effort": return vm.Effort.Live ? vm.Effort.Id : StrItemId.None;
                case "proposal": return i >= 0 && i < vm.ProposalCount ? vm.Proposals[i].Id : StrItemId.None;
                case "front": return i >= 0 && i < vm.FrontCount ? vm.Fronts[i].Id : StrItemId.None;
                case "sortie": return i >= 0 && i < vm.SortieCount ? vm.Sorties[i].Id : StrItemId.None;
                default: return StrItemId.None;
            }
        }

        private static Dictionary<string, object> MapBlock()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            bool relief = MfdTerrainRelief.IsDrawing;
            return new Dictionary<string, object>
            {
                { "focusOffset", float.IsNaN(focusX) ? -1f : MapFocus.CentreOffset(focusX, focusZ) },
                { "zoom", relief ? MfdTerrainRelief.Zoom : map != null ? map.GetZoomLevel() : -1f },
                { "flat", relief || map == null ? 0 : 1 },
            };
        }

        // The old grid label ("FRONT 3/-2") is not a place name; a board that prints one has lost the place book.
        private static readonly Regex GridLabel = new Regex(@"^\s*FRONT\s*-?\d+\s*/\s*-?\d+\s*$", RegexOptions.IgnoreCase);

        private static void NoteName(string name, StringBuilder names, ref int idLike)
        {
            if (string.IsNullOrEmpty(name) || GridLabel.IsMatch(name)) idLike++;
            if (names.Length > 0) names.Append('|');
            names.Append(name ?? "");
        }

        // view, map and check: what the hub built, the map's pose, and the model's differences from the host's own snapshot (must be 0).
        private static void AddHub(Dictionary<string, object> r)
        {
            StrViewHub hub = StrViewHub.Active;
            r["hub"] = hub != null;
            if (hub == null) return;
            hub.TickNow();
            StrViewModel vm = hub.Current;
            TheaterWarSnapshot snap = ModuleServices.TryGet(out ITheaterWarView war) ? war.Snapshot : null;

            int accepted = 0;
            for (int i = 0; i < vm.SortieCount; i++) if (vm.Sorties[i].Active) accepted++;
            if (vm.Effort.Live) operationIds.Add(vm.Effort.OperationId);
            TheaterForcesView forces = snap?.Operation?.Forces;
            var effort = new Dictionary<string, object>
            {
                { "live", vm.Effort.Live ? 1 : 0 }, { "id", vm.Effort.Live ? vm.Effort.OperationId : -1 }, { "idChanges", operationIds.Count },
                { "kind", vm.Effort.Live ? (int)vm.Effort.Kind : -1 }, { "lockSeconds", vm.Effort.LockSeconds },
                { "pickedByLocal", vm.Effort.PickedByLocal ? 1 : 0 }, { "pickedByStaff", vm.Effort.PickedByStaff ? 1 : 0 },
                { "groups", forces == null || forces.Groups == 255 ? -1 : (int)forces.Groups },
                { "vehicles", forces == null || forces.Vehicles == 255 ? -1 : (int)forces.Vehicles },
            };
            var wave = new Dictionary<string, object>
            {
                { "credits", vm.Wave.Present ? vm.Wave.Credits : -1 }, { "perWave", vm.Wave.Present ? vm.Wave.PerWave : -1 },
                { "cost", vm.Wave.Cost }, { "pool", vm.Wave.Pool }, { "held", vm.Wave.Held ? 1 : 0 }, { "affordable", (int)vm.Wave.Affordable },
            };
            // What the pilot reads, not what the engine sent: the four header tiles (1 = a value, 0 = a dash) and the place names the board prints.
            var tiles = new Dictionary<string, object>();
            for (int i = 0; i < vm.Tiles.Length; i++)
                tiles[(vm.Tiles[i].Key ?? "tile" + i).ToLowerInvariant()] = !string.IsNullOrEmpty(vm.Tiles[i].Value) && vm.Tiles[i].Value != StrWords.Dash ? 1 : 0;
            int idLike = 0;
            var names = new StringBuilder();
            for (int i = 0; i < vm.FrontCount; i++) NoteName(vm.Fronts[i].Name, names, ref idLike);
            for (int i = 0; i < vm.ProposalCount; i++) NoteName(vm.Proposals[i].Name, names, ref idLike);
            r["view"] = new Dictionary<string, object>
            {
                { "tiles", tiles }, { "nameIdLike", idLike }, { "names", names.ToString() },
                { "modeCode", (int)vm.Mode }, { "modeName", vm.Mode.ToString() }, { "stale", vm.Stale ? 1 : 0 }, { "posture", (int)vm.Posture },
                { "ageSeconds", float.IsNaN(vm.AgeSeconds) ? -1f : vm.AgeSeconds }, { "canCommand", vm.CanCommand ? 1 : 0 }, { "revision", vm.Revision },
                { "proposals", vm.ProposalCount }, { "fronts", vm.FrontCount }, { "sorties", vm.SortieCount }, { "sortieAccepted", accepted },
                { "posts", vm.PostCount }, { "effort", effort }, { "wave", wave }, { "alertLevel", vm.AlertLevel },
                { "bannerTitleLen", vm.Banner.Head.Length }, { "bannerDoLen", vm.Banner.Do.Length }, { "footerLen", vm.FooterStatus.Length },
            };
            var tickets = new Dictionary<string, object>();
            int ticketsWithPost = 0, wingTasked = 0;
            for (int i = 0; i < vm.SortieCount; i++)
            {
                StrSortieRow row = vm.Sorties[i];
                if (row.PostId >= 0) ticketsWithPost++;
                if (row.HasWing && (row.WingState == WingSortieState.Tasked || row.WingState == WingSortieState.OnStation)) wingTasked++;
                tickets["t" + (i + 1)] = new Dictionary<string, object>
                {
                    { "contractId", row.ContractId }, { "postId", row.PostId }, { "chip", row.ChipText }, { "offered", row.Offered ? 1 : 0 }, { "active", row.Active ? 1 : 0 },
                    { "hasWing", row.HasWing ? 1 : 0 }, { "wingState", (int)row.WingState }, { "wingLabel", row.WingLabel }, { "wingText", row.WingText },
                    { "wingRefusal", row.WingRefusal }, { "wingAllowed", row.Wing.Allowed ? 1 : 0 }, { "wingGate", row.Wing.Text },
                };
            }
            var postRows = new Dictionary<string, object>();
            int postRowsWithTicket = 0;
            for (int i = 0; i < vm.PostCount; i++)
            {
                StrPostRow row = vm.Posts[i];
                if (row.Ticket.Kind != StrItemKind.None) postRowsWithTicket++;
                postRows["p" + (i + 1)] = new Dictionary<string, object>
                {
                    { "id", row.PostId }, { "name", row.Name }, { "site", row.Site }, { "bounty", row.BountyText }, { "capped", row.Capped ? 1 : 0 }, { "seen", row.SeenText },
                    { "x", row.X }, { "z", row.Z }, { "hasTicket", row.Ticket.Kind != StrItemKind.None ? 1 : 0 },
                };
            }
            r["tickets"] = tickets;
            r["postRows"] = postRows;
            r["ticketCheck"] = new Dictionary<string, object> { { "withPost", ticketsWithPost }, { "postRowsWithTicket", postRowsWithTicket }, { "wingTasked", wingTasked } };
            r["map"] = MapBlock();
            StrUiState ui = hub.Ui;
            r["link"] = new Dictionary<string, object>
            {
                { "selectedKind", (int)ui.Selected.Kind }, { "selectedKey", ui.Selected.Key }, { "hoverKind", (int)ui.Hover.Kind },
            };

            int tied = 0;
            if (ModuleServices.TryGet(out ISecondaryObjectivesView sorties) && sorties.Objectives != null && vm.Mode == StrBoardMode.Operation)
                for (int i = 0; i < sorties.Objectives.Count; i++)
                {
                    SortieLink link = sorties.Objectives[i]?.Link;
                    if (link != null && link.Tied && link.OperationLive) tied++;
                }
            // The lanes the board lists: the first MaxFronts of the snapshot, minus the operation's own front while an operation is on the board (the hero lane carries it, StrViewBuilder).
            int lanes = 0;
            TheaterFrontView[] fronts = snap?.Fronts;
            string opKey = vm.Mode == StrBoardMode.Operation ? snap?.Operation?.TargetKey : null;
            for (int i = 0; fronts != null && i < Math.Min(fronts.Length, StrViewModel.MaxFronts); i++)
                if (fronts[i] != null && (string.IsNullOrEmpty(opKey) || fronts[i].Key != opKey)) lanes++;
            r["check"] = new Dictionary<string, object>
            {
                { "proposalsDelta", Math.Abs(vm.ProposalCount - (snap?.Proposals?.Length ?? 0)) },
                { "frontsDelta", Math.Abs(vm.FrontCount - lanes) },
                { "sortiesDelta", Math.Abs(vm.SortieCount + vm.SortieMore - tied) },
                { "effortDelta", Math.Abs((vm.Effort.Live ? vm.Effort.OperationId : 0) - (snap?.Operation?.Id ?? 0)) },
            };
        }
    }
}
#endif
