using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.DynamicOperations.Runtime
{
    /// <summary>
    /// The board's cards as MIS jobs: a card tied to the live operation is a SORTIE, any other a CONTRACT. ACCEPT and SELECT (this pilot's HUD
    /// pick) go through the manager's own verbs; RELEASE takes the card off this pilot's HUD. ABORT stays in the card's detail, behind its confirm.
    /// </summary>
    internal sealed class OperationJobs : IOperationJobs
    {
        private readonly OperationsManager manager;

        internal OperationJobs(OperationsManager owner) => manager = owner;

        public void CopyJobs(List<JobRow> into)
        {
            IReadOnlyList<SecondaryObjectiveView> cards = manager.Objectives;
            bool fresh = manager.IsFresh, idle = fresh && !manager.IsActionPending;
            int active = 0;
            for (int i = 0; i < cards.Count; i++) if (cards[i] != null && cards[i].IsActive) active++;
            bool room = manager.ActiveLimit <= 0 || active < manager.ActiveLimit;
            for (int i = 0; i < cards.Count; i++)
            {
                SecondaryObjectiveView c = cards[i];
                if (c == null || !c.IsActive && !c.IsOffered) continue;
                SortieLink link = c.Link;
                bool sortie = link != null && link.Tied && link.OperationLive, mine = c.IsActive && c.Id == manager.SelectedForHud;
                bool linkSite = link != null && link.SiteKnown;
                string clock = c.SecondsRemaining > 0f && float.IsFinite(c.SecondsRemaining) ? AvNum.Clock((int)c.SecondsRemaining) : "";
                into.Add(new JobRow
                {
                    Kind = sortie ? JobKind.Sortie : JobKind.Contract, Id = c.Id, Title = c.Title ?? "",
                    Detail = Family(c.Tasking?.Family ?? ObjectiveFamily.Unknown) + (sortie ? " · " + link.Place : string.IsNullOrEmpty(c.Target) ? "" : " · " + c.Target),
                    State = !fresh ? "STALE" : c.IsOffered ? "OFFER " + clock : "ACCEPTED" + (clock.Length > 0 ? " · " + clock : ""), // who took it: the open card's LEAD line
                    Act = c.IsOffered ? "ACCEPT" : mine ? "" : "SELECT",
                    ActLive = idle && c.Tasking != null && (!c.IsOffered || room && clock.Length > 0),
                    Mine = mine, Pay = c.Money, Currency = "ALLOCATION",
                    HasSite = linkSite || c.HasMarker, X = linkSite ? link.X : c.X, Z = linkSite ? link.Z : c.Z,
                });
            }
        }

        public void Act(int id)
        {
            SecondaryObjectiveView c = Find(id);
            if (c == null || !manager.IsFresh || manager.IsActionPending) return;
            if (c.IsOffered) manager.RequestAccept(id);
            else if (c.IsActive) manager.SelectForHud(id);
        }

        public void Release(int id)
        {
            if (manager.SelectedForHud == id) manager.SelectForHud(-1);
        }

        private SecondaryObjectiveView Find(int id)
        {
            IReadOnlyList<SecondaryObjectiveView> cards = manager.Objectives;
            for (int i = 0; i < cards.Count; i++) if (cards[i] != null && cards[i].Id == id) return cards[i];
            return null;
        }

        private static string Family(ObjectiveFamily family) =>
            family == ObjectiveFamily.AirCover ? "AIR COVER" : family == ObjectiveFamily.Unknown ? "SIDE JOB" : family.ToString().ToUpperInvariant();
    }
}
