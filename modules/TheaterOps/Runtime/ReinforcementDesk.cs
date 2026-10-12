using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Configuration;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>
    /// The one owner of the wave bank, the quote, the purchase, the newest finished sortie and the AI-convoy timer (rulings R4, D3, D4). Three finished
    /// tied sorties earn a wave for a faction with a human pilot; the price and group are in the snapshot before anything is spent; a pool that cannot
    /// cover the price holds the wave and keeps the credits. The engine calls it from exactly three places, all host-only and inside Guard: the
    /// sortie-finished handler, each Review, and the snapshot build.
    /// </summary>
    internal sealed class ReinforcementDesk
    {
        private const float AiConvoySeconds = 120f;

        private sealed class Books
        {
            internal readonly WaveLedger Ledger = new WaveLedger();
            internal readonly FocusHistory History = new FocusHistory();
            internal readonly List<string> Queued = new List<string>(4);
            internal OperationFocus Live;
            internal int Seq, Extra, AiWaves;
            internal TheaterOutcomeView Last;
            internal float LastBoughtAt = -1f, NextAi;
            internal WaveBlock HeldBlock = WaveBlock.None;
            // Proof only: what the bank did, as a trail (credit count after each counted sortie, H a held wave, B a purchase), and whether the price the last view published was the price charged.
            internal string Trail = "";
            internal int LastViewCost = -2, PriceMatches;
        }

        private readonly Dictionary<int, Books> books = new Dictionary<int, Books>(8);
        private TheaterOpsSettings settings;

        /// <summary>False while no ISortieOutcomeSource is registered (dynamic operations off): a human faction's wave then reads Off, never a silent zero.</summary>
        internal bool SortiesAvailable;

        internal void Configure(TheaterOpsSettings config) => settings = config;

        /// <summary>Host. A sortie finished. True when the snapshot changed and the engine should publish.</summary>
        internal bool OnSortieFinished(FactionHQ hq, in SortieOutcome outcome, in OperationFocus live, StaffLog log, float now)
        {
            Books b = Of(hq);
            if (b == null) return false;
            Observe(b, live, now);
            bool tied = outcome.OperationId > 0;
            bool counted = tied && (live.Covers(outcome.OperationId, outcome.AnchorKey) || b.History.Recent(outcome.OperationId, now));
            if (counted)
            {
                bool credited = b.Ledger.Credit();
                if (credited) Mark(b, b.Ledger.Credits.ToString());
                log.Add(credited ? WaveText.Counted(b.Ledger.Credits, outcome.Title, outcome.Pilot) : WaveText.BankFull(outcome.Title));
            }
            else if (tied) log.Add(WaveText.NoCredit);
            b.Last = new TheaterOutcomeView(++b.Seq, Clip(outcome.Title, 40), Clip(outcome.Pilot, 32), outcome.X, outcome.Z, counted, outcome.ByWing, b.Ledger.Credits);
            // The third credit buys in the same frame, so the wave leaves with the sortie that earned it.
            if (counted) TryBuy(hq, b, log, now);
            return true;
        }

        /// <summary>Host, every Review after AdvanceOperation: flushes queued lines, retries an owed wave, runs the AI timer. True when the snapshot changed.</summary>
        internal bool Review(FactionHQ hq, in OperationFocus live, StaffLog log, float now)
        {
            Books b = Of(hq);
            if (b == null) return false;
            Observe(b, live, now);
            bool changed = b.Queued.Count > 0;
            for (int i = 0; i < b.Queued.Count; i++) log.Add(b.Queued[i]);
            b.Queued.Clear();
            if (TryBuy(hq, b, log, now)) changed = true;
            if (!Human(hq) && settings != null && WavePolicy.AiTimerDue(settings.AiStaffConvoys.Value, live.IsLive, now, b.NextAi))
            {
                b.NextAi = now + AiConvoySeconds;
                TheaterLogisticsService logistics = TheaterLogisticsService.Active;
                if (logistics != null)
                {
                    // Twice the price must be in the pool, so an AI faction never drains itself to buy.
                    WaveQuote quote = logistics.Quote(hq, live, false, SortiesAvailable, false, 2f);
                    if (quote.Block == WaveBlock.None && logistics.Buy(hq, quote, 0f, "AI", out _))
                    {
                        b.Extra++; b.AiWaves++; b.LastBoughtAt = now;
                        log.Add(WaveText.AiConvoy(quote.Name, quote.Cost));
                        changed = true;
                    }
                }
            }
            return changed;
        }

        /// <summary>The wave view for this faction as of now; null only when the desk cannot read the faction.</summary>
        internal TheaterWaveView View(FactionHQ hq, float now)
        {
            Books b = Of(hq);
            TheaterLogisticsService logistics = TheaterLogisticsService.Active;
            if (b == null || logistics == null) return null;
            bool human = Human(hq);
            bool off = !human && !(settings != null && settings.AiStaffConvoys.Value);
            WaveQuote q = logistics.Quote(hq, b.Live, human, SortiesAvailable, off, human ? 1f : 2f);
            b.LastViewCost = q.Cost;
            WaveState state = q.Block == WaveBlock.SwitchedOff || q.Block == WaveBlock.NoSorties || q.Block == WaveBlock.MissionForbids ? WaveState.Off
                : human && b.Ledger.Owed && q.Block != WaveBlock.None ? WaveState.Blocked : WaveState.Earning;
            float ago = b.LastBoughtAt < 0f ? -1f : Math.Min(86400f, Math.Max(0f, now - b.LastBoughtAt));
            return new TheaterWaveView(state, q.Block, b.Ledger.Credits, WaveLedger.CreditsPerWave, Clip(q.Name, 32), q.Cost, q.Pool, q.CooldownSeconds,
                Math.Min(10000, b.Ledger.Bought + b.Extra), ago, q.DepotKnown, q.DepotX, q.DepotZ);
        }

        /// <summary>The newest finished sortie, or null.</summary>
        internal TheaterOutcomeView LastOutcome(FactionHQ hq) => hq != null && books.TryGetValue(hq.GetInstanceID(), out Books b) ? b.Last : null;

        /// <summary>An event convoy bought through the pool's one door: it counts as a purchase and its staff line is written on the next Review.</summary>
        internal void NoteConvoy(FactionHQ hq, string line)
        {
            Books b = Of(hq);
            if (b == null) return;
            b.Extra++;
            b.LastBoughtAt = UnityEngine.Time.timeSinceLevelLoad;
            if (b.Queued.Count < 4) b.Queued.Add(line);
        }

        /// <summary>Every state is dropped (scene change, mission change, module off).</summary>
        internal void ResetForScene()
        {
            books.Clear();
            SortiesAvailable = false;
        }

#if !PUBLIC_RELEASE
        /// <summary>Sim readback: the bank and the purchase counters of one faction.</summary>
        internal bool TryRead(FactionHQ hq, out int credits, out int bought, out int outcomeSeq, out int aiWaves, out string trail, out int priceMatches)
        {
            credits = bought = outcomeSeq = aiWaves = priceMatches = 0; trail = "";
            if (hq == null || !books.TryGetValue(hq.GetInstanceID(), out Books b)) return false;
            credits = b.Ledger.Credits; bought = b.Ledger.Bought + b.Extra; outcomeSeq = b.Seq; aiWaves = b.AiWaves; trail = b.Trail; priceMatches = b.PriceMatches;
            return true;
        }

        /// <summary>Sim lever: the host switch AI STAFF CONVOYS, live.</summary>
        internal void SetAiSwitch(bool on)
        {
            if (settings != null) settings.AiStaffConvoys.Value = on;
        }

        /// <summary>Sim lever: the next purchase of an AI-only faction is due now.</summary>
        internal void ArmAi(FactionHQ hq)
        {
            Books b = Of(hq);
            if (b != null) b.NextAi = 0f;
        }
#endif

        // Buys one wave when three credits are owed. A gate that does not pass holds the wave, keeps every credit and says why once per distinct reason.
        private bool TryBuy(FactionHQ hq, Books b, StaffLog log, float now)
        {
            TheaterLogisticsService logistics = TheaterLogisticsService.Active;
            if (logistics == null || !b.Ledger.Owed || !Human(hq)) return false;
            WaveQuote quote = logistics.Quote(hq, b.Live, true, SortiesAvailable, false, 1f);
            if (quote.Block == WaveBlock.None && logistics.Buy(hq, quote, 0f, "WAVE", out _))
            {
                b.Ledger.Spend();
                b.PriceMatches = b.LastViewCost == quote.Cost ? 1 : 0;
                Mark(b, "B");
                b.LastBoughtAt = now;
                b.HeldBlock = WaveBlock.None;
                log.Add(WaveText.Bought(quote.Name, quote.Cost, (int)Math.Min(1000000f, Math.Max(0f, (float)Math.Floor(hq.factionFunds)))));
                return true;
            }
            WaveBlock held = quote.Block == WaveBlock.None ? WaveBlock.NoGroup : quote.Block;
            if (held == b.HeldBlock) return false;
            b.HeldBlock = held;
            Mark(b, "H");
            log.Add(WaveText.Held(held, quote.Cost, quote.Pool));
            return true;
        }

        private static void Mark(Books b, string token)
        {
            if (b.Trail.Length < 64) b.Trail += (b.Trail.Length > 0 ? "," : "") + token;
        }

        private static void Observe(Books b, in OperationFocus live, float now)
        {
            b.Live = live;
            b.History.Observe(live.IsLive ? live.OperationId : 0, now);
        }

        private static bool Human(FactionHQ hq) => hq != null && hq.factionPlayers != null && hq.factionPlayers.Count > 0;

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
