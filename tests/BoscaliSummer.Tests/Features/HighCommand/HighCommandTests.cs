using System;
using System.Collections.Generic;
using BoscaliSummer.Features.HighCommand.Domain;

namespace BoscaliSummer.Tests.Features.HighCommand
{
    internal static class HighCommandTests
    {
        public static void Run()
        {
            GenerationIsDeterministicAndBounded();
            PromotionMovesTheNextInLine();
            MarksAreBoundedAndToggled();
            CohesionTracksTheLivingRoster();
            EconomyPaysOnlyWhatItShould();
            SnapshotRulesRejectMalformedRows();
        }

        private static readonly string[] Sites = { "ALPHA", "BRAVO", "CHARLIE" };

        private static void GenerationIsDeterministicAndBounded()
        {
            CommandTree first = CommandTree.Generate(4242, Sites);
            CommandTree second = CommandTree.Generate(4242, Sites);
            CommandTree other = CommandTree.Generate(4243, Sites);

            TestAssert.That(first.Slots.Count == CommandTier.SlotCount, "six posts per faction");
            TestAssert.That(first.TotalWeight == second.TotalWeight, "total weight is seed-stable");
            TestAssert.That(first.Cohesion(0f) == 1f, "a full roster starts at full cohesion");
            TestAssert.That(first.Slots[0].Tier == CommandTier.Theater, "slot zero is the theater commander");
            TestAssert.That(first.Slots[3].Tier == CommandTier.Base, "last slots are base commanders");
            TestAssert.That(first.Slots[3].ParentId == 2 || first.Slots[3].ParentId == 1,
                "base posts report to a component commander");

            bool identical = true;
            for (int i = 0; i < first.Slots.Count; i++)
                if (first.Slots[i].Person.Name != second.Slots[i].Person.Name) identical = false;
            TestAssert.That(identical, "names are reproduced from the seed");
            TestAssert.That(other.Slots[0].Person.Seed != first.Slots[0].Person.Seed, "different seed, different staff");

            for (int i = 0; i < first.Slots.Count; i++)
            {
                CommandPerson person = first.Slots[i].Person;
                TestAssert.That(person.Name.Length <= CommanderGenerator.MaxName, "name fits its row");
                TestAssert.That(!string.IsNullOrEmpty(person.Name), "name is generated");
                string bio = CommanderGenerator.Bio(person.Seed, person.Traits);
                TestAssert.That(bio.Length > 0 && bio.Length <= CommanderGenerator.MaxBio, "bio stays bounded");
                TestAssert.That(bio.IndexOf('\n') < 0 && bio.IndexOf('\r') < 0, "bio is a flat string");
                TestAssert.That((person.Traits & ~(CommandTrait)CommandTraits.All) == 0, "traits stay in the catalogue");
                TestAssert.That(CommandTraits.Labels(person.Traits).Length <= 64, "trait labels fit the dossier");
            }

            for (int seed = 0; seed < 64; seed++)
            {
                PortraitDesign a = PortraitDesign.FromSeed(seed);
                PortraitDesign b = PortraitDesign.FromSeed(seed);
                TestAssert.That(a.Face == b.Face && a.Hair == b.Hair && a.Cap == b.Cap, "portrait features are seed-stable");
                TestAssert.That(a.Face < 3 && a.Eyes < 3 && a.Nose < 3 && a.Cap < 3 && a.FacialHair < 4,
                    "portrait features stay inside their palettes");
            }

            var streamA = new SeedStream(7);
            var streamB = new SeedStream(7);
            for (int i = 0; i < 32; i++)
            {
                uint value = streamA.Next();
                TestAssert.That(value == streamB.Next(), "seed stream is reproducible");
            }
            for (int i = 0; i < 256; i++)
            {
                int range = new SeedStream((uint)(i + 1)).Range(7);
                TestAssert.That(range >= 0 && range < 7, "range stays inside bounds");
            }
        }

        private static void PromotionMovesTheNextInLine()
        {
            CommandTree tree = CommandTree.Generate(99, Sites);
            CommandSlot theater = tree.Slots[0];
            CommandSlot air = tree.Slots[1];
            string successor = air.Person.Name;

            int displaced = tree.Promote(theater.Id, 12345);
            TestAssert.That(displaced == air.Id, "a direct report is promoted into the dead post");
            TestAssert.That(theater.Status == CommanderStatus.Kia, "the dead post is marked KIA");
            TestAssert.That(theater.Person.Name == successor, "the successor keeps their identity at the new post");
            TestAssert.That(air.Person.Name != successor, "the vacated post receives a new name");
            TestAssert.That(tree.KiaCount == 1 && tree.LiveCount == 5, "kills and lives stay consistent");
            TestAssert.That(tree.Cohesion(0f) < 1f, "losing a post costs cohesion");
            TestAssert.That(tree.Cohesion(0f) > 0f, "one loss is not a collapse");
            TestAssert.That(tree.Find(theater.Id).MarkedByFaction == 0, "death clears any kill-list mark");
        }

        private static void MarksAreBoundedAndToggled()
        {
            CommandTree tree = CommandTree.Generate(5, Sites);
            TestAssert.That(tree.Mark(0, 1, 2), "first mark accepted");
            TestAssert.That(tree.Mark(1, 1, 2), "second mark accepted");
            TestAssert.That(!tree.Mark(2, 1, 2), "a third mark is rejected");
            TestAssert.That(tree.CountMarks(1) == 2, "mark count respects the ceiling");
            TestAssert.That(tree.Mark(1, 1, 2), "re-marking clears the toggle");
            TestAssert.That(tree.CountMarks(1) == 1, "clearing reduces the count");
            TestAssert.That(tree.Mark(2, 2, 2), "another faction marks independently");
            tree.ClearMarks(2);
            TestAssert.That(tree.CountMarks(2) == 0 && tree.CountMarks(1) == 1, "clearing is per faction");
        }

        private static void CohesionTracksTheLivingRoster()
        {
            CommandTree tree = CommandTree.Generate(17, Sites);
            float full = tree.Cohesion(0f);
            tree.Promote(0, 1);
            float afterTheater = tree.Cohesion(0f);
            tree.Promote(3, 2);
            tree.Promote(4, 3);
            float degraded = tree.Cohesion(0f);

            TestAssert.That(full == 1f, "full roster is full cohesion");
            TestAssert.That(afterTheater < full, "losing the theater commander is the heaviest loss");
            TestAssert.That(degraded < afterTheater, "more losses lower cohesion further");
            TestAssert.That(tree.Cohesion(0.05f) > degraded, "a commendation boost raises cohesion");
            TestAssert.That(tree.Cohesion(5f) == 1f, "cohesion clamps at one");
            TestAssert.That(tree.Cohesion(-5f) == 0f, "cohesion clamps at zero");

            CommandSlot slot = tree.Slots[0];
            slot.Status = CommanderStatus.Disrupted;
            float disrupted = tree.Cohesion(0f);
            slot.Status = CommanderStatus.Kia;
            TestAssert.That(disrupted > tree.Cohesion(0f), "a disrupted post still counts; a dead one does not");
            TestAssert.That(tree.LiveWeight >= 0f, "live weight never goes negative");
        }

        private static void EconomyPaysOnlyWhatItShould()
        {
            TestAssert.That(CommandEconomy.BountyFunds(CommandTier.Base, false, 50, 1500, 3000, 6000, 1f) == 1500,
                "base bounty");
            TestAssert.That(CommandEconomy.BountyFunds(CommandTier.Theater, false, 50, 1500, 3000, 6000, 1f) == 6000,
                "theater bounty");
            TestAssert.That(CommandEconomy.BountyFunds(CommandTier.Component, true, 50, 1500, 3000, 6000, 1f) == 4500,
                "marked bounty applies the percent");
            TestAssert.That(CommandEconomy.BountyFunds(CommandTier.Base, true, 50, 1500, 3000, 6000, 1.2f) == 2700,
                "veteran trait multiplies the bounty");
            TestAssert.That(CommandEconomy.BountyScore(CommandTier.Theater) == 20, "theater score");

            TestAssert.That(CommandEconomy.Stipend(0, 10f, 1f) == 0, "disabled stipend pays nothing");
            TestAssert.That(CommandEconomy.Stipend(200, 0f, 1f) == 0, "no live staff, no stipend");
            int healthy = CommandEconomy.Stipend(200, 6f, 1f);
            int battered = CommandEconomy.Stipend(200, 6f, 0.2f);
            TestAssert.That(healthy > battered && battered > 0, "cohesion scales the stipend but never zeroes it");
            TestAssert.That(CommandEconomy.Stipend(200, float.NaN, 1f) == 0, "non-finite weight pays nothing");

            TestAssert.That(CommandEconomy.IntervalCommandPoints(0.9f, 0) == 1, "high cohesion earns a point");
            TestAssert.That(CommandEconomy.IntervalCommandPoints(0.4f, 0) == 0, "low cohesion earns none");
            TestAssert.That(CommandEconomy.IntervalCommandPoints(0.4f, 2) == 2, "political officers earn their own");
        }

        private static void SnapshotRulesRejectMalformedRows()
        {
            TestAssert.That(CommandSnapshotRules.ValidHeader(0.5f, 3, 5, 1), "sane header accepted");
            TestAssert.That(!CommandSnapshotRules.ValidHeader(float.NaN, 3, 5, 1), "NaN cohesion rejected");
            TestAssert.That(!CommandSnapshotRules.ValidHeader(1.5f, 3, 5, 1), "cohesion above one rejected");
            TestAssert.That(!CommandSnapshotRules.ValidHeader(0.5f, -1, 5, 1), "negative points rejected");
            TestAssert.That(!CommandSnapshotRules.ValidHeader(0.5f, 3, 999, 1), "impossible staff count rejected");

            TestAssert.That(CommandSnapshotRules.ValidNode(3, 1, 2, 0x3F, 0x07, 12f, 0.25f, 100f, -200f),
                "sane node accepted");
            TestAssert.That(!CommandSnapshotRules.ValidNode(3, 1, 9, 0, 0, 12f, 0.25f, 0f, 0f), "bad tier rejected");
            TestAssert.That(!CommandSnapshotRules.ValidNode(3, 1, 2, 0x80, 0, 12f, 0.25f, 0f, 0f), "unknown flag rejected");
            TestAssert.That(!CommandSnapshotRules.ValidNode(3, 1, 2, 0, 0x08, 12f, 0.25f, 0f, 0f), "unknown action rejected");
            TestAssert.That(!CommandSnapshotRules.ValidNode(3, 1, 2, 0, 0, 12f, 2f, 0f, 0f), "weight above one rejected");
            TestAssert.That(!CommandSnapshotRules.ValidNode(3, 1, 2, 0, 0, 12f, 0.25f, float.PositiveInfinity, 0f),
                "infinite coordinate rejected");
        }
    }
}
