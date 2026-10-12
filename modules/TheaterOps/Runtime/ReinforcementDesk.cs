using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>
    /// The one owner of the wave bank, the newest finished sortie and (from the buying half) the quote and the purchase. The engine calls it
    /// from exactly three places, all host-only and inside Guard: the sortie-finished handler, each Review, and the snapshot build.
    /// </summary>
    internal sealed class ReinforcementDesk
    {
        private sealed class Books
        {
            internal readonly WaveLedger Ledger = new WaveLedger();
            internal readonly FocusHistory History = new FocusHistory();
            internal int Seq;
            internal TheaterOutcomeView Last;
        }

        private readonly Dictionary<int, Books> books = new Dictionary<int, Books>(8);

        /// <summary>False while no ISortieOutcomeSource is registered (dynamic operations off): a human faction's wave then reads Off, never a silent zero.</summary>
        internal bool SortiesAvailable;

        /// <summary>Host. A sortie finished. True when the snapshot changed and the engine should publish.</summary>
        internal bool OnSortieFinished(FactionHQ hq, in SortieOutcome outcome, in OperationFocus live, StaffLog log, float now)
        {
            Books b = Of(hq);
            if (b == null) return false;
            b.History.Observe(live.IsLive ? live.OperationId : 0, now);
            bool tied = outcome.OperationId > 0;
            bool counted = tied && (live.Covers(outcome.OperationId, outcome.AnchorKey) || b.History.Recent(outcome.OperationId, now));
            if (counted)
                log.Add(b.Ledger.Credit() ? WaveText.Counted(b.Ledger.Credits, outcome.Title, outcome.Pilot) : WaveText.BankFull(outcome.Title));
            else if (tied) log.Add(WaveText.NoCredit);
            b.Last = new TheaterOutcomeView(++b.Seq, Clip(outcome.Title, 40), Clip(outcome.Pilot, 32), outcome.X, outcome.Z, counted, outcome.ByWing, b.Ledger.Credits);
            return true;
        }

        /// <summary>Host, every Review after AdvanceOperation. True when the snapshot changed.</summary>
        internal bool Review(FactionHQ hq, in OperationFocus live, StaffLog log, float now)
        {
            Books b = Of(hq);
            if (b == null) return false;
            b.History.Observe(live.IsLive ? live.OperationId : 0, now);
            return false;
        }

        /// <summary>The newest finished sortie, or null.</summary>
        internal TheaterOutcomeView LastOutcome(FactionHQ hq) => hq != null && books.TryGetValue(hq.GetInstanceID(), out Books b) ? b.Last : null;

        /// <summary>Every state is dropped (scene change, mission change, module off).</summary>
        internal void ResetForScene()
        {
            books.Clear();
            SortiesAvailable = false;
        }

        private Books Of(FactionHQ hq)
        {
            if (hq == null) return null;
            int id = hq.GetInstanceID();
            if (books.TryGetValue(id, out Books b)) return b;
            if (books.Count >= 8) return null;
            b = new Books();
            books.Add(id, b);
            return b;
        }

        private static string Clip(string text, int maximum) =>
            string.IsNullOrEmpty(text) ? string.Empty : text.Length > maximum ? text.Substring(0, maximum) : text;
    }
}
