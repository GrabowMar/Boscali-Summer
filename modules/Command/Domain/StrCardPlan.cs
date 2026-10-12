using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>What a card button does. The map card runs it through the hub, the one command path (Focus is a select that flies the map).</summary>
    internal enum StrCardAct : byte { None, Pick, Accept, SendWing, Recall, Focus }

    /// <summary>One button of the card: its word, its action, the item the action is aimed at and the gate as of the model it was built from.</summary>
    internal struct StrCardButton
    {
        public string Label;
        public StrCardAct Act;
        /// <summary>The item the hub call names: the proposal, the sortie (a post's ticket) or the item to fly to.</summary>
        public StrItemId Target;
        public StrAct Gate;
    }

    /// <summary>
    /// What the click card of the map shows for the selected item, as data (annex 05 P10): the tag and chip, the title, at most four lines, and the one
    /// primary button that commits beside an optional second one. Every word is the model's, so the card and the panel never disagree. A refused
    /// button's reason is also in <see cref="Reason"/>, the card's footer line.
    /// </summary>
    internal sealed class StrCardPlan
    {
        public const int MaxLines = 4;

        public bool Valid;
        public StrItemId Id;
        public string Tag = "", Chip = "", Title = "";
        public StrInkRole Ink;
        public readonly string[] Lines = new string[MaxLines];
        public int LineCount;
        public StrCardButton Primary, Secondary;
        public string Reason = "";

        /// <summary>Fills the plan in place. An item that is not on the board (or no model) builds an empty, invalid card.</summary>
        public static void Build(StrCardPlan into, StrViewModel vm, StrItemId id)
        {
            if (into == null) return;
            into.Valid = false; into.Id = StrItemId.None;
            into.Tag = into.Chip = into.Title = into.Reason = "";
            into.LineCount = 0; into.Primary = default; into.Secondary = default;
            if (vm == null || id.IsNone) return;
            switch (id.Kind)
            {
                case StrItemKind.Effort: into.Effort(vm, id); break;
                case StrItemKind.Proposal: into.Proposal(vm, id); break;
                case StrItemKind.Front: into.Front(vm, id); break;
                case StrItemKind.Sortie: into.Sortie(vm, id); break;
                case StrItemKind.Post: into.Post(vm, id); break;
            }
            if (!into.Valid) return;
            into.Id = id;
            if (into.Primary.Act != StrCardAct.None && !into.Primary.Gate.Allowed) into.Reason = into.Primary.Gate.Text;
            else if (into.Secondary.Act != StrCardAct.None && !into.Secondary.Gate.Allowed) into.Reason = into.Secondary.Gate.Text;
        }

        private void Effort(StrViewModel vm, StrItemId id)
        {
            StrEffortView e = vm.Effort;
            if (!e.Live || e.Id != id) return;
            Start(e.Tag, e.KindText, e.Name, StrInkRole.Effort);
            Line(TheaterWords.Kind(e.Kind), e.PhaseText, e.PickedText);
            Line(e.VehiclesText, e.GroupsText);
            Line(e.MovementText);
            if (e.LockSeconds > 0) Line("LOCK", e.LockText);
            Primary = Focus(vm, id);
        }

        private void Proposal(StrViewModel vm, StrItemId id)
        {
            StrProposalRow p = StrGate.Proposal(vm, id);
            if (p == null) return;
            Start(p.Tag, p.KindText, p.Name, StrInkRole.Effort);
            Line(p.Aim);
            Line("RISK " + p.RiskText, p.WaveText);
            if (vm.Wave.Present) Line(vm.Wave.PoolText);
            Primary = new StrCardButton { Label = vm.Effort.Live ? "REDIRECT HERE" : "MAKE MAIN EFFORT", Act = StrCardAct.Pick, Target = id, Gate = p.Pick };
            Secondary = Focus(vm, id);
        }

        private void Front(StrViewModel vm, StrItemId id)
        {
            StrFrontRow f = null;
            for (int i = 0; i < vm.FrontCount && i < vm.Fronts.Length; i++)
                if (vm.Fronts[i] != null && vm.Fronts[i].Id == id) { f = vm.Fronts[i]; break; }
            if (f == null) return;
            Start(f.Tag, "", f.Name, f.Ink);
            Line(f.TrendText.Length > 0 ? f.PressureText + " " + f.TrendText : f.PressureText);
            Line(f.StatusText);
            Line(f.OfferText);
            Primary = Focus(vm, id);
        }

        private void Sortie(StrViewModel vm, StrItemId id)
        {
            StrSortieRow s = StrGate.Sortie(vm, id);
            if (s == null) return;
            Start(s.Tag, s.ChipText, s.Title, s.Offered ? StrInkRole.Offered : StrInkRole.Effort);
            Line(s.DistanceText, s.RewardText, s.TimeText);
            Line(s.StateText, s.ByText);
            Line(s.WingText);
            var wing = new StrCardButton { Label = s.WingLabel, Act = StrPanelText.WingTasked(s) ? StrCardAct.Recall : StrCardAct.SendWing, Target = id, Gate = s.Wing };
            if (s.Offered)
            {
                Primary = new StrCardButton { Label = "ACCEPT", Act = StrCardAct.Accept, Target = id, Gate = s.Accept };
                Secondary = wing;
            }
            else
            {
                Primary = wing;
                Secondary = Focus(vm, id);
            }
        }

        private void Post(StrViewModel vm, StrItemId id)
        {
            StrPostRow p = null;
            for (int i = 0; i < vm.PostCount && i < vm.Posts.Length; i++)
                if (vm.Posts[i] != null && vm.Posts[i].Id == id) { p = vm.Posts[i]; break; }
            if (p == null) return;
            StrSortieRow ticket = p.Ticket.IsNone ? null : StrGate.Sortie(vm, p.Ticket);
            Start("", ticket != null ? "STRIKE" : "COMMAND POST", p.Name, StrInkRole.Strike);
            Line(p.Role, p.Site);
            Line(p.BountyText, p.Capped ? "CAPPED" : "");
            Line(p.TraitText);
            Line(p.SeenText);
            if (ticket != null && ticket.Offered)
            {
                Primary = new StrCardButton { Label = "ACCEPT", Act = StrCardAct.Accept, Target = ticket.Id, Gate = ticket.Accept };
                Secondary = Focus(vm, id);
            }
            else Primary = Focus(vm, id);
        }

        private static StrCardButton Focus(StrViewModel vm, StrItemId id) =>
            new StrCardButton { Label = "FOCUS", Act = StrCardAct.Focus, Target = id, Gate = StrAnchors.LocateGate(vm, id) };

        private void Start(string tag, string chip, string title, StrInkRole ink)
        {
            Valid = true;
            Tag = tag ?? ""; Chip = chip ?? ""; Title = title ?? ""; Ink = ink;
        }

        // The words of a line joined by the middle dot, skipping any that are empty; a line with nothing to say is not added.
        private void Line(string a, string b = "", string c = "")
        {
            if (LineCount >= MaxLines) return;
            bool ha = !string.IsNullOrEmpty(a), hb = !string.IsNullOrEmpty(b), hc = !string.IsNullOrEmpty(c);
            if (!ha && !hb && !hc) return;
            string line = ha ? a : "";
            if (hb) line = line.Length > 0 ? line + StrWords.Dot + b : b;
            if (hc) line = line.Length > 0 ? line + StrWords.Dot + c : c;
            Lines[LineCount++] = line;
        }
    }
}
