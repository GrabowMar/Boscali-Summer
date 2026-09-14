using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.HighCommand.Domain
{
    /// <summary>
    /// One faction's fixed staff structure. Generation is deterministic from the faction
    /// seed; promotion moves people between the fixed posts and never edits the tree.
    /// </summary>
    internal sealed class CommandTree
    {
        public const int MaximumSites = 8;
        private static readonly string[] Roles =
        {
            "THEATER COMMANDER", "AIR COMPONENT CMDR", "GROUND COMPONENT CMDR",
            "BASE COMMANDER", "BASE COMMANDER", "BASE COMMANDER",
        };

        private readonly List<CommandSlot> slots = new List<CommandSlot>(CommandTier.MaximumSlots);

        public IReadOnlyList<CommandSlot> Slots => slots;

        public static CommandTree Generate(int seed, IReadOnlyList<string> siteNames)
        {
            var tree = new CommandTree();
            int siteCount = siteNames == null ? 0 : Math.Min(siteNames.Count, MaximumSites);

            for (int i = 0; i < CommandTier.SlotCount; i++)
            {
                int tier = i == 0 ? CommandTier.Theater : i <= 2 ? CommandTier.Component : CommandTier.Base;
                int parent = i == 0 ? -1 : i <= 2 ? 0 : 1 + (i % 2);
                int siteIndex = i <= 1 ? 0 : siteCount == 0 ? -1 : Math.Min(i - 1, siteCount - 1);
                if (i >= 3 && siteCount > 0) siteIndex = (i - 3) % siteCount;

                string siteName = siteCount > 0
                    ? siteNames[Math.Max(0, siteIndex)]
                    : "FIELD HQ";
                var slot = new CommandSlot(i, parent, tier, Roles[i], siteIndex, siteName)
                {
                    Person = CommanderGenerator.Create(unchecked(seed + 101 * (i + 1)), tier),
                    Status = CommanderStatus.Active,
                };
                tree.slots.Add(slot);
            }
            return tree;
        }

        public CommandSlot Find(int id)
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].Id == id) return slots[i];
            return null;
        }

        public float EffectiveWeight(CommandSlot slot)
        {
            if (slot == null || slot.Person == null || slot.Status == CommanderStatus.Kia) return 0f;
            float weight = CommandTier.Weight(slot.Tier) * CommandTraits.WeightMultiplier(slot.Person.Traits);
            if (slot.Status == CommanderStatus.Disrupted) weight *= 0.5f;
            return weight;
        }

        /// <summary>Weight of the whole roster, ignoring who is still alive: the cohesion denominator.</summary>
        public float TotalWeight
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < slots.Count; i++)
                {
                    CommandSlot slot = slots[i];
                    if (slot.Person == null) continue;
                    total += CommandTier.Weight(slot.Tier) * CommandTraits.WeightMultiplier(slot.Person.Traits);
                }
                return total;
            }
        }

        public float LiveWeight
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < slots.Count; i++) total += EffectiveWeight(slots[i]);
                return total;
            }
        }

        /// <summary>Share of the roster still standing, 0..1, plus a temporary boost, clamped.</summary>
        public float Cohesion(float boost)
        {
            float total = TotalWeight;
            float value = total <= 0f ? 0f : LiveWeight / total + boost;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        public int LiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < slots.Count; i++)
                    if (slots[i].Alive) count++;
                return count;
            }
        }

        public int KiaCount => slots.Count - LiveCount;

        public int PoliticalCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    CommandSlot slot = slots[i];
                    if (slot.Alive && slot.Person != null && CommandTraits.Has(slot.Person.Traits, CommandTrait.Political))
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Kill the person in <paramref name="slotId"/> and move the next in line into the
        /// post. Returns the slot that received a freshly assigned person (-1 when the dead
        /// post itself was refilled with a new name).
        /// </summary>
        public int Promote(int slotId, int newSeed)
        {
            CommandSlot dead = Find(slotId);
            if (dead == null || dead.Status == CommanderStatus.Kia) return -1;

            dead.Status = CommanderStatus.Kia;
            dead.MarkedByFaction = 0;

            CommandSlot successor = null;
            float best = 0f;
            for (int i = 0; i < slots.Count; i++)
            {
                CommandSlot candidate = slots[i];
                if (candidate.ParentId != dead.Id || !candidate.Alive || candidate.Person == null) continue;
                float weight = EffectiveWeight(candidate);
                if (successor == null || weight > best) { successor = candidate; best = weight; }
            }

            if (successor == null)
            {
                dead.Person = CommanderGenerator.Create(newSeed, dead.Tier);
                return -1;
            }

            dead.Person = successor.Person;
            successor.Person = CommanderGenerator.Create(unchecked(newSeed + 7919), successor.Tier);
            successor.MarkedByFaction = 0;
            return successor.Id;
        }

        public int CountMarks(int factionToken)
        {
            if (factionToken <= 0) return 0;
            int count = 0;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].MarkedByFaction == factionToken) count++;
            return count;
        }

        public bool Mark(int slotId, int factionToken, int maximumMarks)
        {
            CommandSlot slot = Find(slotId);
            if (slot == null || factionToken <= 0) return false;
            if (slot.MarkedByFaction == factionToken) { slot.MarkedByFaction = 0; return true; }
            if (slot.MarkedByFaction != 0 || CountMarks(factionToken) >= maximumMarks) return false;
            slot.MarkedByFaction = (byte)factionToken;
            return true;
        }

        public void ClearMarks(int factionToken)
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].MarkedByFaction == factionToken) slots[i].MarkedByFaction = 0;
        }
    }
}
