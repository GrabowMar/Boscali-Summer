using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Nation.Domain
{
    /// <summary>What the host read from the game for one faction this tick. Arrays are shared scratch; only the first <see cref="Rows"/> entries count.</summary>
    internal struct NationWorld
    {
        public int[] Stock, Target;
        public bool[] Aircraft;
        public int Rows;
        public int HeldAirbases;
        /// <summary>Plants the mission itself gave the faction (they never count against the slots).</summary>
        public int InitialPlants;
        public float Funds, Stability;
        public int Escalation;
        /// <summary>Host settings: the base factory price and the base build time.</summary>
        public float FactoryCost, BuildSeconds;
    }

    internal enum NationEventKind : byte { FactoryBuilt, Produced }

    internal struct NationEvent
    {
        public NationEventKind Kind;
        /// <summary>The plant id for <see cref="NationEventKind.Produced"/>.</summary>
        public int A;
        /// <summary>The stock row for <see cref="NationEventKind.Produced"/>.</summary>
        public int B;
    }

    internal sealed class Plant
    {
        public int Id;
        public LineKind Kind;
        public int Row = -1;
        public bool Pinned, Resting;
        /// <summary>Mission time of the next unit; 0 = start the clock on the next tick.</summary>
        public float NextAt;
        public float Interval;
        /// <summary>An enemy SABOTAGE stops the plant until this mission time.</summary>
        public float RaidedUntil;
    }

    /// <summary>
    /// One faction's economy on the host: the two laws and the plants with their lines. Every verb judges itself and returns a
    /// <see cref="NationOutcome"/>. Politics (war support, doctrine, decisions, the cabinet) is the faction's <see cref="PoliticsBook"/>.
    /// </summary>
    internal sealed class NationBook
    {
        public EconomyPolicy Economy = EconomyPolicy.Balanced;
        public PriorityPolicy Priority = PriorityPolicy.Balanced;
        public float EconomyLockUntil, PriorityLockUntil;
        public bool Building;
        public float BuildStart, BuildEnd;
        /// <summary>What politics adds to production right now (SURGE PRODUCTION, LOOK AWAY); 1 = nothing.</summary>
        public float Boost = 1f;
        public readonly List<Plant> Plants = new List<Plant>(NationRules.MaxPlants);
        /// <summary>Set by anything a client would see; the owner clears it when it has sent a snapshot.</summary>
        public bool Dirty = true;

        public Plant Find(int id)
        {
            for (int i = 0; i < Plants.Count; i++) if (Plants[i].Id == id) return Plants[i];
            return null;
        }

        public void AddPlant(int id)
        {
            if (Find(id) != null || Plants.Count >= NationRules.MaxPlants) return;
            Plants.Add(new Plant { Id = id });
            Dirty = true;
        }

        public void RemovePlant(int id)
        {
            for (int i = Plants.Count - 1; i >= 0; i--)
                if (Plants[i].Id == id) { Plants.RemoveAt(i); Dirty = true; }
        }

        public int ConstructionPlants()
        {
            int n = 0;
            for (int i = 0; i < Plants.Count; i++) if (Plants[i].Kind == LineKind.Construction) n++;
            return n;
        }

        public NationOutcome SetEconomy(EconomyPolicy value, float now)
        {
            if (value > EconomyPolicy.War) return NationOutcome.Unknown;
            if (value == Economy) return NationOutcome.Ok;
            if (now < EconomyLockUntil) return NationOutcome.Locked;
            Economy = value;
            EconomyLockUntil = now + NationRules.PolicyLockSeconds;
            Dirty = true;
            return NationOutcome.Ok;
        }

        public NationOutcome SetPriority(PriorityPolicy value, float now)
        {
            if (value > PriorityPolicy.Ground) return NationOutcome.Unknown;
            if (value == Priority) return NationOutcome.Ok;
            if (now < PriorityLockUntil) return NationOutcome.Locked;
            Priority = value;
            PriorityLockUntil = now + NationRules.PolicyLockSeconds;
            Dirty = true;
            return NationOutcome.Ok;
        }

        /// <summary>A pilot's line for one plant; it stays until un-pinned or the plant dies. <paramref name="rows"/> bounds the row.</summary>
        public NationOutcome SetLine(int plantId, LineKind kind, int row, bool aircraft, int rows)
        {
            Plant plant = Find(plantId);
            if (plant == null) return NationOutcome.Unknown;
            if (kind == LineKind.Construction) row = -1;
            else if (row < 0 || row >= rows) return NationOutcome.Unknown;
            else kind = aircraft ? LineKind.Aircraft : LineKind.Vehicle;
            if (plant.Kind != kind || plant.Row != row) plant.NextAt = 0f;
            plant.Kind = kind;
            plant.Row = row;
            plant.Pinned = true;
            Dirty = true;
            return NationOutcome.Ok;
        }

        public NationOutcome Unpin(int plantId)
        {
            Plant plant = Find(plantId);
            if (plant == null) return NationOutcome.Unknown;
            if (plant.Pinned) { plant.Pinned = false; Dirty = true; }
            return NationOutcome.Ok;
        }

        /// <summary>Begins a factory. <paramref name="charged"/> is what the caller takes from the treasury (0 when free or refused).</summary>
        public NationOutcome StartBuild(float now, in NationWorld w, bool free, out float charged)
        {
            charged = 0f;
            if (Building) return NationOutcome.Busy;
            if (Plants.Count >= NationRules.Slots(w.HeldAirbases, w.InitialPlants)) return NationOutcome.NoSlot;
            float cost = w.FactoryCost > 0f ? w.FactoryCost : NationRules.FactoryCost;
            if (!free && w.Funds < cost) return NationOutcome.NoFunds;
            Building = true;
            BuildStart = now;
            BuildEnd = now + NationRules.BuildSeconds(ConstructionPlants(), w.BuildSeconds > 0f ? w.BuildSeconds : NationRules.FactoryBuildSeconds);
            charged = free ? 0f : cost;
            Dirty = true;
            return NationOutcome.Ok;
        }

        /// <summary>Advances the clocks. Produced units are counted into <c>w.Stock</c> as they are emitted, so one tick never overshoots a target.</summary>
        public void Tick(float now, in NationWorld w, List<NationEvent> events)
        {
            if (Building && now >= BuildEnd)
            {
                Building = false;
                events.Add(new NationEvent { Kind = NationEventKind.FactoryBuilt });
                Dirty = true;
            }

            float output = NationRules.Output(Economy, w.Stability) * (Boost > 0f ? Boost : 1f);
            for (int i = 0; i < Plants.Count; i++)
            {
                Plant plant = Plants[i];
                if (plant.Kind != LineKind.Aircraft && plant.Kind != LineKind.Vehicle) { plant.Resting = false; continue; }
                if (plant.Row < 0 || plant.Row >= w.Rows) { plant.Kind = LineKind.Idle; plant.Row = -1; Dirty = true; continue; }
                plant.Interval = NationRules.UnitSeconds(plant.Kind == LineKind.Aircraft) / (output > 0.1f ? output : 0.1f);
                if (now < plant.RaidedUntil) { plant.NextAt = now + plant.Interval; continue; }   // a raided plant restarts its clock when the raid ends
                if (plant.NextAt <= 0f) plant.NextAt = now + plant.Interval;
                int target = w.Target[plant.Row] > 0 ? w.Target[plant.Row] : 1;
                bool resting = w.Stock[plant.Row] >= (plant.Pinned ? target * 2 : target);
                if (resting != plant.Resting) { plant.Resting = resting; Dirty = true; }
                if (resting || now < plant.NextAt) continue;
                w.Stock[plant.Row]++;
                plant.NextAt = now + plant.Interval;
                events.Add(new NationEvent { Kind = NationEventKind.Produced, A = plant.Id, B = plant.Row });
                Dirty = true;
            }
        }
    }

    /// <summary>The staff: the default answer to every economic decision, for AI factions and for humans who never open the page.</summary>
    internal static class AutoStaff
    {
        /// <summary>Runs the staff's pass. True when it wants a factory built now (the owner checks the ground and pays).</summary>
        public static bool Run(NationBook book, in NationWorld w, float now)
        {
            float cost = w.FactoryCost > 0f ? w.FactoryCost : NationRules.FactoryCost;
            float ratio = StockRatio(w);
            book.SetEconomy(ratio < 0.5f ? EconomyPolicy.War : ratio >= 1f && w.Funds < cost ? EconomyPolicy.Civilian : EconomyPolicy.Balanced, now);

            AssignLines(book, w);

            return !book.Building && book.Plants.Count < NationRules.Slots(w.HeldAirbases, w.InitialPlants) &&
                w.Funds >= cost * NationRules.StaffBuildReserve;
        }

        private static float StockRatio(in NationWorld w)
        {
            int stock = 0, target = 0;
            for (int i = 0; i < w.Rows; i++) { stock += w.Stock[i] < w.Target[i] ? w.Stock[i] : w.Target[i]; target += w.Target[i]; }
            return target > 0 ? (float)stock / target : 1f;
        }

        private static readonly int[] assigned = new int[NationRules.MaxStockRows];

        /// <summary>
        /// Points every un-pinned plant at the largest relative shortage, weighted 2:1 by PRIORITY. A plant whose line still has a
        /// shortage keeps it (no thrash); one plant goes to CONSTRUCTION while a factory is being built.
        /// </summary>
        public static void AssignLines(NationBook book, in NationWorld w)
        {
            int rows = w.Rows < assigned.Length ? w.Rows : assigned.Length;
            for (int i = 0; i < rows; i++) assigned[i] = 0;
            bool crew = false;
            foreach (Plant p in book.Plants)
            {
                if (!p.Pinned) continue;
                if (p.Kind == LineKind.Construction) crew = true;
                else if (p.Row >= 0 && p.Row < rows) assigned[p.Row]++;
            }
            foreach (Plant p in book.Plants)
            {
                if (p.Pinned) continue;
                bool keeps = p.Kind == LineKind.Construction ? book.Building && !crew
                    : (p.Kind == LineKind.Aircraft || p.Kind == LineKind.Vehicle) && p.Row >= 0 && p.Row < rows && Need(w, p.Row, book.Priority) > 0f;
                if (!keeps) { p.Kind = LineKind.Idle; continue; }
                if (p.Kind == LineKind.Construction) crew = true; else assigned[p.Row]++;
            }
            foreach (Plant p in book.Plants)
            {
                if (p.Pinned || p.Kind != LineKind.Idle) continue;
                LineKind kind = LineKind.Idle;
                int row = -1;
                if (book.Building && !crew) { kind = LineKind.Construction; crew = true; }
                else
                {
                    float best = 0f;
                    for (int i = 0; i < rows; i++)
                    {
                        float score = Need(w, i, book.Priority);
                        if (score > best) { best = score; row = i; }
                    }
                    if (row >= 0) { kind = w.Aircraft[row] ? LineKind.Aircraft : LineKind.Vehicle; assigned[row]++; }
                }
                if (kind != p.Kind || row != p.Row) { p.NextAt = 0f; book.Dirty = true; }
                p.Kind = kind;
                p.Row = row;
            }
        }

        /// <summary>Relative shortage of a row after the plants already on it, times the priority weight; 0 or less = no plant wanted.</summary>
        private static float Need(in NationWorld w, int row, PriorityPolicy priority)
        {
            int target = w.Target[row];
            if (target <= 0) return 0f;
            float weight = priority == PriorityPolicy.Balanced ? 1f : (priority == PriorityPolicy.Air) == w.Aircraft[row] ? 2f : 1f;
            return (target - w.Stock[row] - assigned[row]) / (float)target * weight;
        }
    }
}
