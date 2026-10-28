using System;
using System.Globalization;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// Pure: why an action is or is not allowed, from the model alone, with the refusal in words that say what to do.
    /// A stale feed disables only its own verbs: the war report gates pick, redirect, posture and replan; the sortie list gates accept and the wing.
    /// Money is never a pick refusal (R4): the pool lives in the wave strip.
    /// </summary>
    internal static class StrGate
    {
        private static readonly StrAct Allowed = new StrAct(StrWhy.None, "");

        /// <summary>arg is the posture value for Posture, unused otherwise.</summary>
        public static StrAct Check(StrViewModel vm, StrActionKind kind, StrItemId id, int arg = 0)
        {
            if (vm == null) return new StrAct(StrWhy.NoBoard, "NO BOARD YET");
            switch (kind)
            {
                case StrActionKind.Pick: return Pick(vm, id);
                case StrActionKind.Replan: return Replan(vm);
                case StrActionKind.Posture: return Posture(vm, arg);
                case StrActionKind.Accept: return Accept(vm, id);
                case StrActionKind.SendWing: return WingGate(vm.IsHost, vm.SortiesLive, Sortie(vm, id), false);
                case StrActionKind.Recall: return WingGate(vm.IsHost, vm.SortiesLive, Sortie(vm, id), true);
                case StrActionKind.BuyConvoy: return Buy(vm, arg);
                case StrActionKind.Donate: return Donate(vm, arg);
                default: return new StrAct(StrWhy.NoBoard, "NOT AN ACTION");
            }
        }

        /// <summary>
        /// The wing button of one sortie ticket. Host only; SEND WING needs the sortie list to be current and the card accepted; RECALL
        /// needs a live tasking and nothing else. row is null when the card is not on the board.
        /// </summary>
        internal static StrAct WingGate(bool isHost, bool sortiesLive, StrSortieRow row, bool recall)
        {
            if (!isHost) return new StrAct(StrWhy.HostOnly, "ONLY THE HOST CAN SEND A WING" + StrWords.Dot + "ASK THE HOST OR FLY IT YOURSELF");
            if (recall)
            {
                bool tasked = row != null && row.HasWing && (row.WingState == WingSortieState.Tasked || row.WingState == WingSortieState.OnStation);
                return tasked ? Allowed : new StrAct(StrWhy.NoWing, "NO WING TASKING TO RECALL");
            }
            if (!sortiesLive) return SortiesStale();
            if (row == null) return new StrAct(StrWhy.NoWing, "THIS SORTIE IS NOT ON THE BOARD");
            if (row.Offered) return new StrAct(StrWhy.WingRefused, "ACCEPT THIS SORTIE FIRST" + StrWords.Dot + "THEN SEND YOUR WING");
            if (!row.Active) return new StrAct(StrWhy.NoWing, "THIS SORTIE IS OVER");
            if (!row.HasWing) return new StrAct(StrWhy.NoWing, "THE WING HAS NO ORDER FOR THIS SORTIE YET" + StrWords.Dot + "RECRUIT A WING FIRST");
            if (!string.IsNullOrEmpty(row.WingRefusal)) return new StrAct(StrWhy.WingRefused, row.WingRefusal);
            return Allowed;
        }

        private static StrAct Pick(StrViewModel vm, StrItemId id)
        {
            if (vm.Mode != StrBoardMode.Operation && vm.Mode != StrBoardMode.Proposals) return NoBoard(vm);
            StrAct war = WarGate(vm);
            if (!war.Allowed) return war;
            if (Locked(vm.Effort, out StrAct locked)) return locked;
            return Proposal(vm, id) != null ? Allowed : Gone();
        }

        /// <summary>
        /// DRAW on the map: the war must be current and taking orders, and no other pilot may hold the lock; there need be no proposal. The arrow's
        /// length is checked on release (StrActions.Draw), the front under the press by the host.
        /// </summary>
        public static StrAct DrawGate(StrViewModel vm)
        {
            if (vm == null) return new StrAct(StrWhy.NoBoard, "NO BOARD YET");
            if (vm.Mode != StrBoardMode.Operation && vm.Mode != StrBoardMode.Proposals && vm.Mode != StrBoardMode.NoOpening) return NoBoard(vm);
            StrAct war = WarGate(vm);
            if (!war.Allowed) return war;
            return Locked(vm.Effort, out StrAct locked) ? locked : Allowed;
        }

        private static bool Locked(StrEffortView e, out StrAct act)
        {
            act = Allowed;
            if (e == null || !(e.LockSeconds > 0) || e.PickedByLocal || e.PickedByStaff) return false;
            act = new StrAct(StrWhy.Locked, "LOCKED" + (string.IsNullOrEmpty(e.PickedBy) ? "" : " BY " + e.PickedBy.ToUpperInvariant()) +
                                            (string.IsNullOrEmpty(e.LockText) ? "" : StrWords.Dot + e.LockText));
            return true;
        }

        private static StrAct Replan(StrViewModel vm)
        {
            if (vm.Mode != StrBoardMode.Operation || !vm.Effort.Live) return new StrAct(StrWhy.NoOperation, "NO OPERATION TO CALL OFF" + StrWords.Dot + "PICK A PROPOSAL FIRST");
            return WarGate(vm);
        }

        private static StrAct Posture(StrViewModel vm, int arg)
        {
            if (vm.Mode != StrBoardMode.Operation && vm.Mode != StrBoardMode.Proposals &&
                vm.Mode != StrBoardMode.Assessing && vm.Mode != StrBoardMode.NoOpening) return NoBoard(vm);
            StrAct war = WarGate(vm);
            if (!war.Allowed) return war;
            if (arg == (int)vm.Posture) return new StrAct(StrWhy.SamePosture, "THE STAFF IS ALREADY " + PostureName(vm.Posture));
            return Allowed;
        }

        private static StrAct Accept(StrViewModel vm, StrItemId id)
        {
            if (!vm.SortiesAvailable) return new StrAct(StrWhy.NoBoard, "NO SORTIE LIST FROM THE HOST YET");
            if (!vm.SortiesLive) return SortiesStale();
            if (vm.SortieActionPending) return new StrAct(StrWhy.Pending, "ACCEPT SENT" + StrWords.Dot + "WAITING FOR THE HOST");
            StrSortieRow row = Sortie(vm, id);
            if (row == null || !row.Offered) return new StrAct(StrWhy.NotOffered, "THIS SORTIE IS NOT OFFERED");
            if (vm.SortieActiveLimit > 0 && vm.SortieActiveCount >= vm.SortieActiveLimit)
                return new StrAct(StrWhy.BoardFull, "YOUR SIDE HOLDS " + vm.SortieActiveCount + " OF " + vm.SortieActiveLimit + " SORTIES" + StrWords.Dot + "FINISH ONE FIRST");
            return Allowed;
        }

        /// <summary>
        /// BUY on the SUPPLY tab, by the rules of the game's own contribute menu: a pilot in a faction, a mission that allows it, the host's cooldown
        /// over and the pilot's own funds covering the price. arg is the group's index in the mission's list. It needs no war report.
        /// </summary>
        private static StrAct Buy(StrViewModel vm, int index)
        {
            StrSupplyView s = vm.Supply;
            if (!s.Known) return NoAccount();
            if (s.Forbidden) return new StrAct(StrWhy.Forbidden, "THIS MISSION FORBIDS BUYING UNITS");
            StrSupplyRow r = SupplyRow(vm, index);
            if (r == null || float.IsNaN(r.Cost)) return new StrAct(StrWhy.Gone, "THAT GROUP IS NOT ON THE MISSION'S LIST");
            if (r.Cooldown < 0) return new StrAct(StrWhy.Pending, "ASKING THE HOST WHETHER THIS GROUP IS AVAILABLE");
            if (r.Cooldown > 0) return new StrAct(StrWhy.Cooldown, "JUST BOUGHT" + StrWords.Dot + "AVAILABLE AGAIN IN " + r.Cooldown + " S");
            if (s.Funds < r.Cost) return new StrAct(StrWhy.NoFunds, "YOU HOLD " + s.FundsText + StrWords.Dot + "THIS GROUP COSTS " + r.CostText);
            return Allowed;
        }

        /// <summary>GIVE on the SUPPLY tab: arg is the share of the pilot's funds in percent. The host takes one donation a minute and drops the rest silently, so the gate says so first.</summary>
        private static StrAct Donate(StrViewModel vm, int percent)
        {
            StrSupplyView s = vm.Supply;
            if (!s.Known) return NoAccount();
            if (percent < 1 || percent > 100) return new StrAct(StrWhy.NoBoard, "NOT A SHARE");
            if (!(s.Funds > 0f)) return new StrAct(StrWhy.NoFunds, "YOU HAVE NO FUNDS TO GIVE");
            if (s.DonateCooldown < 0) return new StrAct(StrWhy.Pending, "ASKING THE HOST WHEN IT TAKES THE NEXT DONATION");
            if (s.DonateCooldown > 0) return new StrAct(StrWhy.Cooldown, "ONE DONATION A MINUTE" + StrWords.Dot + "NEXT IN " + s.DonateCooldown + " S");
            return Allowed;
        }

        /// <summary>The convoy row at this index of the mission's list, or null.</summary>
        internal static StrSupplyRow SupplyRow(StrViewModel vm, int index) =>
            index >= 0 && index < vm.Supply.RowCount && index < vm.Supply.Rows.Length ? vm.Supply.Rows[index] : null;

        private static StrAct NoAccount() => new StrAct(StrWhy.NoBoard, "JOIN A FACTION TO SPEND YOUR FUNDS");

        /// <summary>The proposal row with this id, or null: the one lookup the gate and the runner share.</summary>
        internal static StrProposalRow Proposal(StrViewModel vm, StrItemId id)
        {
            if (id.Kind != StrItemKind.Proposal) return null;
            for (int i = 0; i < vm.ProposalCount && i < vm.Proposals.Length; i++)
                if (vm.Proposals[i] != null && vm.Proposals[i].Id == id) return vm.Proposals[i];
            return null;
        }

        /// <summary>The sortie row with this id, or null.</summary>
        internal static StrSortieRow Sortie(StrViewModel vm, StrItemId id)
        {
            if (id.Kind != StrItemKind.Sortie) return null;
            for (int i = 0; i < vm.SortieCount && i < vm.Sorties.Length; i++)
                if (vm.Sorties[i] != null && vm.Sorties[i].Id == id) return vm.Sorties[i];
            return null;
        }

        private static StrAct NoBoard(StrViewModel vm) =>
            new StrAct(StrWhy.NoBoard, string.IsNullOrEmpty(vm.Banner.Head) ? "NO BOARD YET" : vm.Banner.Head);

        private static StrAct WarStale(StrViewModel vm) => new StrAct(StrWhy.Stale, StrWords.StaleReport(vm.AgeSeconds));

        private static StrAct SortiesStale() => new StrAct(StrWhy.Stale, "SORTIE LIST NOT CURRENT" + StrWords.Dot + "WAIT FOR THE HOST TO REFRESH");

        /// <summary>The war must be current, not waiting on the host and taking orders: the first reason it is not, else allowed.</summary>
        private static StrAct WarGate(StrViewModel vm) =>
            vm.Stale ? WarStale(vm) : vm.WarPending ? Pending() : vm.CanCommand ? Allowed : NotTakingOrders();

        private static StrAct Pending() => new StrAct(StrWhy.Pending, "ORDER SENT" + StrWords.Dot + "WAITING FOR THE HOST TO ANSWER");

        private static StrAct NotTakingOrders() => new StrAct(StrWhy.NoBoard, "THE STAFF IS NOT TAKING ORDERS RIGHT NOW");

        private static StrAct Gone() => new StrAct(StrWhy.Gone, "THAT PROPOSAL IS NO LONGER ON THE BOARD");

        private static string PostureName(TheaterWarPosture posture) =>
            posture == TheaterWarPosture.Cautious ? "CAUTIOUS" : posture == TheaterWarPosture.Bold ? "BOLD" : "STEADY";
    }

    /// <summary>
    /// The runner behind every press: gate, then verb, then a local notice. Explicit presses only (R5): nothing in the staff calls this.
    /// It never edits the model. A refused action reaches no verb. The war verbs answer through the engine's own command result, so
    /// only the accept and the wing answers leave a notice (six seconds on the presentation clock). A first REPLAN press only arms
    /// (Allowed, not Sent); the second one inside four seconds, by the same test the builder uses to draw "CALLS OFF", sends.
    /// </summary>
    internal static class StrActions
    {
        public const float ReplanWindowSeconds = 4f, NoticeSeconds = 6f;

        public static StrAct Run(StrActionKind kind, StrItemId id, int arg, StrViewModel vm, IStrVerbs verbs, ref StrUiState ui, float nowUnscaled)
        {
            StrAct gate = StrGate.Check(vm, kind, id, arg);
            if (!gate.Allowed)
            {
                if (kind == StrActionKind.Replan) ui.ReplanArmedUntil = 0f;   // a board that went stale or ended between the presses does not stay armed
                return gate;
            }
            if (verbs == null) return Declined();

            switch (kind)
            {
                case StrActionKind.Pick:
                {
                    StrProposalRow p = StrGate.Proposal(vm, id);
                    return verbs.Pick(p.ProposalId, p.Revision) ? Sent("ORDER SENT") : Declined();
                }
                case StrActionKind.Replan:
                {
                    if (!float.IsFinite(nowUnscaled)) return Declined();
                    if (!(nowUnscaled < ui.ReplanArmedUntil))
                    {
                        ui.ReplanArmedUntil = nowUnscaled + ReplanWindowSeconds;
                        return new StrAct(StrWhy.None, "PRESS AGAIN TO CALL OFF");
                    }
                    ui.ReplanArmedUntil = 0f;
                    return verbs.Cancel(vm.Effort.OperationId, vm.Effort.Revision) ? Sent("OPERATION CALLED OFF") : Declined();
                }
                case StrActionKind.Posture:
                    if (arg < 0 || arg > (int)TheaterWarPosture.Bold) return new StrAct(StrWhy.NoBoard, "NOT A POSTURE");
                    return verbs.Posture((TheaterWarPosture)arg) ? Sent("ORDER SENT") : Declined();
                case StrActionKind.Accept:
                    verbs.Accept(StrGate.Sortie(vm, id).ContractId);
                    return Noted(ref ui, StrWhy.None, "ACCEPT SENT" + StrWords.Dot + "THE HOST ANSWERS NEXT", nowUnscaled, true);
                case StrActionKind.SendWing:
                {
                    bool ok = verbs.SendWing(StrGate.Sortie(vm, id).ContractId, out string said);
                    return Wing(ref ui, ok, said, "WING ORDER SENT", nowUnscaled);
                }
                case StrActionKind.Recall:
                {
                    bool ok = verbs.Recall(out string said);
                    return Wing(ref ui, ok, said, "WING RECALLED", nowUnscaled);
                }
                case StrActionKind.BuyConvoy:
                {
                    string name = StrGate.SupplyRow(vm, arg).Name;
                    return verbs.BuyConvoy(arg)
                        ? Noted(ref ui, StrWhy.None, "BOUGHT " + name + StrWords.Dot + "A DEPOT DELIVERS IT", nowUnscaled, true)
                        : new StrAct(StrWhy.NoBoard, "THE PURCHASE WAS NOT SENT" + StrWords.Dot + "TRY AGAIN");
                }
                case StrActionKind.Donate:
                {
                    float amount = vm.Supply.Funds * arg / 100f;
                    return verbs.Donate(amount)
                        ? Noted(ref ui, StrWhy.None, "GAVE " + Math.Floor(amount).ToString("N0", CultureInfo.InvariantCulture) + " TO THE FACTION POOL", nowUnscaled, true)
                        : new StrAct(StrWhy.NoBoard, "THE DONATION WAS NOT SENT" + StrWords.Dot + "TRY AGAIN");
                }
                default:
                    return gate;
            }
        }

        /// <summary>The map's DRAW released: the gate, then the arrow's length, then the verb. The host snaps the press to a front and answers.</summary>
        public static StrAct Draw(StrViewModel vm, IStrVerbs verbs, ref StrUiState ui, float sx, float sz, float ox, float oz, float nowUnscaled)
        {
            StrAct gate = StrGate.DrawGate(vm);
            if (!gate.Allowed) return gate;
            float dx = ox - sx, dz = oz - sz, d2 = dx * dx + dz * dz;
            if (!(d2 >= TheaterDrawLimits.MinimumMetres * TheaterDrawLimits.MinimumMetres))
                return new StrAct(StrWhy.BadDraw, "TOO SHORT" + StrWords.Dot + "DRAG AT LEAST " + Km(TheaterDrawLimits.MinimumMetres));
            if (d2 > TheaterDrawLimits.MaximumMetres * TheaterDrawLimits.MaximumMetres)
                return new StrAct(StrWhy.BadDraw, "TOO LONG" + StrWords.Dot + Km(TheaterDrawLimits.MaximumMetres) + " AT MOST");
            if (verbs == null) return Declined();
            return verbs.Draw(sx, sz, ox, oz) ? Noted(ref ui, StrWhy.None, "OFFENSIVE SENT" + StrWords.Dot + "THE HOST ANSWERS NEXT", nowUnscaled, true) : Declined();
        }

        private static string Km(float metres) => (metres / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KM";

        private static StrAct Sent(string text) => new StrAct(StrWhy.None, text, true);

        /// <summary>The view would not take the order (a command not allowed right now): nothing was sent.</summary>
        private static StrAct Declined() => new StrAct(StrWhy.NoBoard, "THE STAFF DID NOT TAKE THE ORDER" + StrWords.Dot + "TRY AGAIN");

        private static StrAct Wing(ref StrUiState ui, bool ok, string said, string whenSent, float now)
        {
            bool blank = string.IsNullOrEmpty(said);
            return Noted(ref ui, ok ? StrWhy.None : StrWhy.WingRefused, blank ? (ok ? whenSent : "THE WING DID NOT TAKE THE ORDER") : said, now, ok);
        }

        private static StrAct Noted(ref StrUiState ui, StrWhy why, string text, float now, bool sent)
        {
            ui.Notice = text;
            ui.NoticeUntil = now + NoticeSeconds;
            return new StrAct(why, text, sent);
        }
    }
}
