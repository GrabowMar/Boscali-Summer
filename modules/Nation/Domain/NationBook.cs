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
        /// <summary>Host settings: seconds one focus takes, the base factory price and the base build time.</summary>
        public float FocusSeconds, FactoryCost, BuildSeconds;
    }

    internal enum NationEventKind : byte { FocusDone, FactoryBuilt, Produced }

    internal struct NationEvent
    {
        public NationEventKind Kind;
        /// <summary>The focus id, or the plant id for <see cref="NationEventKind.Produced"/>.</summary>
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
    }

    /// <summary>
    /// One faction's nation on the host: the two laws, the focus being worked and the plants with their lines. Every verb judges itself
    /// and returns a <see cref="NationOutcome"/>; pilot 0 is the staff, which never takes or respects the pick lock.
    /// </summary>
    internal sealed class NationBook
    {
        public EconomyPolicy Economy = EconomyPolicy.Balanced;
        public PriorityPolicy Priority = PriorityPolicy.Balanced;
        public float EconomyLockUntil, PriorityLockUntil;
        public ushort Done;
        public int Running = -1, Queued = -1;
        public float RunningStart, RunningEnd;
        public int PickHolder;
        public float PickLockUntil;
        public bool Building;
        public float BuildStart, BuildEnd;
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

        private bool LockedOut(int pilot, float now) => pilot != 0 && PickHolder != 0 && PickHolder != pilot && now < PickLockUntil;

        private void Hold(int pilot, float now)
        {
            if (pilot == 0) return;
            PickHolder = pilot;
            PickLockUntil = now + NationRules.PickLockSeconds;
        }

        /// <summary>Starts the focus when nothing runs, else queues it behind the running one (whose completion it may count on).</summary>
        public NationOutcome Focus(int id, int pilot, float now, int stage, float seconds)
        {
            if (!FocusTree.Valid(id)) return NationOutcome.Unknown;
            if (LockedOut(pilot, now)) return NationOutcome.Locked;
            var focus = (FocusId)id;
            if (Running < 0)
            {
                if (!FocusTree.CanStart(Done, focus, stage)) return NationOutcome.NotOpen;
                Start(id, now, seconds);
            }
            else
            {
                ushort planned = (ushort)(Done | (1 << Running));
                if (id == Running || !FocusTree.CanStart(planned, focus, stage)) return NationOutcome.NotOpen;
                Queued = id;
            }
            Hold(pilot, now);
            Dirty = true;
            return NationOutcome.Ok;
        }

        public NationOutcome Unqueue(int pilot, float now)
        {
            if (LockedOut(pilot, now)) return NationOutcome.Locked;
            if (Queued >= 0) { Queued = -1; Dirty = true; }
            Hold(pilot, now);
            return NationOutcome.Ok;
        }

        private void Start(int id, float now, float seconds)
        {
            Running = id;
            RunningStart = now;
            RunningEnd = now + (seconds > 1f ? seconds : NationRules.FocusSeconds);
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
            if (Plants.Count >= NationRules.Slots(w.HeldAirbases, Done, w.InitialPlants)) return NationOutcome.NoSlot;
            float cost = NationRules.Cost(Done, w.FactoryCost > 0f ? w.FactoryCost : NationRules.FactoryCost);
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
            if (Running >= 0 && now >= RunningEnd)
            {
                int finished = Running;
                Done |= (ushort)(1 << finished);
                Running = -1;
                events.Add(new NationEvent { Kind = NationEventKind.FocusDone, A = finished });
                if (Queued >= 0 && FocusTree.CanStart(Done, (FocusId)Queued, w.Escalation)) Start(Queued, now, w.FocusSeconds);
                Queued = -1;
                Dirty = true;
            }
            if (Building && now >= BuildEnd)
            {
                Building = false;
                events.Add(new NationEvent { Kind = NationEventKind.FactoryBuilt });
                Dirty = true;
            }

            float output = NationRules.Output(Economy, w.Stability, Done);
            for (int i = 0; i < Plants.Count; i++)
            {
                Plant plant = Plants[i];
                if (plant.Kind != LineKind.Aircraft && plant.Kind != LineKind.Vehicle) { plant.Resting = false; continue; }
                if (plant.Row < 0 || plant.Row >= w.Rows) { plant.Kind = LineKind.Idle; plant.Row = -1; Dirty = true; continue; }
                plant.Interval = NationRules.UnitSeconds(plant.Kind == LineKind.Aircraft) / (output > 0.1f ? output : 0.1f);
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

    /// <summary>The staff: the default answer to every decision, for AI factions and for humans who never open the page.</summary>
    internal static class AutoStaff
    {
        /// <summary>Runs the staff's pass. True when it wants a factory built now (the owner checks the ground and pays).</summary>
        public static bool Run(NationBook book, in NationWorld w, float now)
        {
            if (book.Running < 0)
            {
                int next = NextFocus(book, w);
                if (next >= 0) book.Focus(next, 0, now, w.Escalation, w.FocusSeconds);
            }

            float cost = NationRules.Cost(book.Done, w.FactoryCost > 0f ? w.FactoryCost : NationRules.FactoryCost);
            float ratio = StockRatio(w);
            book.SetEconomy(ratio < 0.5f ? EconomyPolicy.War : ratio >= 1f && w.Funds < cost ? EconomyPolicy.Civilian : EconomyPolicy.Balanced, now);

            AssignLines(book, w);

            return !book.Building && book.Plants.Count < NationRules.Slots(w.HeldAirbases, book.Done, w.InitialPlants) &&
                w.Funds >= cost * NationRules.StaffBuildReserve;
        }

        private static float StockRatio(in NationWorld w)
        {
            int stock = 0, target = 0;
            for (int i = 0; i < w.Rows; i++) { stock += w.Stock[i] < w.Target[i] ? w.Stock[i] : w.Target[i]; target += w.Target[i]; }
            return target > 0 ? (float)stock / target : 1f;
        }

        /// <summary>The cheapest unmet need: stock low, MILITARY first; every slot used, INDUSTRY first; else STATE first. -1 when nothing is open.</summary>
        public static int NextFocus(NationBook book, in NationWorld w)
        {
            bool low = false;
            for (int i = 0; i < w.Rows; i++) if (w.Stock[i] * 2 < w.Target[i]) { low = true; break; }
            bool full = !book.Building && book.Plants.Count >= NationRules.Slots(w.HeldAirbases, book.Done, w.InitialPlants);
            int first = low ? 1 : full ? 0 : 2;
            for (int k = 0; k < 3; k++)
            {
                int branch = (first + k) % 3;
                for (int tier = 0; tier < FocusTree.Tiers; tier++)
                {
                    var id = (FocusId)(branch * FocusTree.Tiers + tier);
                    if (NationRules.Has(book.Done, id)) continue;
                    if (FocusTree.CanStart(book.Done, id, w.Escalation)) return (int)id;
                    break; // the branch waits (escalation); try the next branch
                }
            }
            return -1;
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
