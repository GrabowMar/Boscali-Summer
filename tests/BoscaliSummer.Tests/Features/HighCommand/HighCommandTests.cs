using System;
using System.Collections.Generic;
using BoscaliSummer.Features.HighCommand.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.HighCommand
{
    internal static class HighCommandTests
    {
        public static void Run()
        {
            GenerationIsDeterministicAndBounded();
            PromotionMovesTheNextInLine();
            CohesionTracksTheLivingRoster();
            BonusesStateWhatACommanderIsWorth();
            EconomyPaysOnlyWhatItShould();
            StaffLogKeepsItsMemoryBounded();
            MapMarkersFollowTheFog();
            SelectionBracketsStayOnTheMarker();
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
                TestAssert.That(CommandTraits.BonusLine(person.Traits).Length <= 128, "the bonus line fits the card");
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
        }

        /// <summary>
        /// The board's whole pitch is that a commander is worth something while alive, so the
        /// bonus line is pinned: every trait states an effect, the line is bounded, and the
        /// multipliers behind the words stay inside the range the page promises.
        /// </summary>
        private static void BonusesStateWhatACommanderIsWorth()
        {
            TestAssert.That(CommandTraits.BonusLine(CommandTrait.None) == "NO STAFF BONUS",
                "a staff with nothing notable says so");

            for (int bit = 0; bit < 5; bit++)
            {
                var trait = (CommandTrait)(1 << bit);
                TestAssert.That(CommandTraits.Label(trait) != "UNKNOWN", "every trait has a name");
                string effect = CommandTraits.Effect(trait);
                TestAssert.That(effect.Length > 0 && effect.Length <= 32, "every trait states a bounded effect");
                string line = CommandTraits.BonusLine(trait);
                TestAssert.That(line.IndexOf(CommandTraits.Label(trait), StringComparison.Ordinal) >= 0 &&
                                line.IndexOf(effect, StringComparison.Ordinal) >= 0,
                    "the line carries both the name and the effect");
            }

            TestAssert.That(CommandTraits.StipendMultiplier(CommandTrait.Logistician) > 1f,
                "a logistician earns more than a plain staff");
            TestAssert.That(CommandTraits.StipendMultiplier(CommandTrait.Veteran) == 1f,
                "a veteran earns nothing extra while alive");
            TestAssert.That(CommandTraits.BountyMultiplier(CommandTrait.Veteran) > 1f,
                "a veteran is worth more dead");
            TestAssert.That(CommandTraits.IntelRadiusMultiplier(CommandTrait.Recluse) < 1f,
                "a recluse's patrols see less");
            TestAssert.That(CommandTraits.WeightMultiplier(CommandTrait.Beloved) >
                            CommandTraits.WeightMultiplier(CommandTrait.Zealot),
                "the beloved leader carries more of the staff than a zealot");
            TestAssert.That(CommandTraits.DisruptionBonus(CommandTrait.Beloved) > 0f,
                "losing a beloved leader hurts longer");
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
            TestAssert.That(CommandEconomy.BountyFunds(CommandTier.Base, 1500, 3000, 6000, 1f) == 1500,
                "base kill pay");
            TestAssert.That(CommandEconomy.BountyFunds(CommandTier.Theater, 1500, 3000, 6000, 1f) == 6000,
                "theater kill pay");
            TestAssert.That(CommandEconomy.BountyFunds(CommandTier.Component, 1500, 3000, 6000, 1.2f) == 3600,
                "a veteran is worth 20% more dead");
            TestAssert.That(CommandEconomy.BountyFunds(CommandTier.Base, 0, 0, 0, 1f) == 0,
                "a disabled economy pays nothing");
            TestAssert.That(CommandEconomy.BountyScore(CommandTier.Theater) == 20, "theater score");

            TestAssert.That(CommandEconomy.Stipend(0, 10f, 1f) == 0, "disabled stipend pays nothing");
            TestAssert.That(CommandEconomy.Stipend(200, 0f, 1f) == 0, "no live staff, no stipend");
            int healthy = CommandEconomy.Stipend(200, 6f, 1f);
            int battered = CommandEconomy.Stipend(200, 6f, 0.2f);
            TestAssert.That(healthy > battered && battered > 0, "cohesion scales the stipend but never zeroes it");
            TestAssert.That(CommandEconomy.Stipend(200, float.NaN, 1f) == 0, "non-finite weight pays nothing");
        }

        private static void StaffLogKeepsItsMemoryBounded()
        {
            var log = new CommandLog();
            TestAssert.That(log.Count == 0, "a fresh log is empty");
            for (int i = 0; i < CommandLog.Capacity + 3; i++)
                log.Append(i, CommanderLogTone.Order, "LINE " + i, i * 10f);

            TestAssert.That(log.Count == CommandLog.Capacity, "the log caps at its capacity");
            TestAssert.That(log[0].Text == "LINE 8", "the newest entry reads first");
            TestAssert.That(log[CommandLog.Capacity - 1].Text == "LINE 3", "the oldest kept entry stays");
            TestAssert.That(log[CommandLog.Capacity].Text == null, "out of range reads as empty");
            TestAssert.That(log[0].Tone == CommanderLogTone.Order, "the tone survives the ring");

            log.Append(-2, CommanderLogTone.Alert, new string('X', 200), 999f);
            TestAssert.That(log[0].Text.Length == CommandLog.MaximumText, "log text is clamped before it is stored");
            TestAssert.That(log[0].TargetId == -2 && log[0].Time == 999f, "faction-wide entries keep their id and time");

            TestAssert.That(CommandLog.VisibleToObserver(50f, 40f),
                "an event before the observer's last sighting is visible");
            TestAssert.That(!CommandLog.VisibleToObserver(50f, 60f),
                "an event after the last sighting is withheld");
            TestAssert.That(!CommandLog.VisibleToObserver(0f, 10f), "no sight means no story");

            log.Clear();
            TestAssert.That(log.Count == 0, "clear empties the ring");
        }

        /// <summary>
        /// The map layer obeys the same fog the roster does: a marker exists only for a post
        /// the faction owns or has confirmed, never for a dead one, and the tier sizes stay
        /// ordered so the chain of command reads on the map.
        /// </summary>
        private static void MapMarkersFollowTheFog()
        {
            TestAssert.That(CommandMarkerPolicy.Show(true, false, false), "your own post is marked");
            TestAssert.That(CommandMarkerPolicy.Show(true, true, false), "a known post is marked");
            TestAssert.That(!CommandMarkerPolicy.Show(false, false, false), "an unseen enemy post is not marked");
            TestAssert.That(!CommandMarkerPolicy.Show(false, true, true), "a dead commander is never marked");
            TestAssert.That(!CommandMarkerPolicy.Show(true, true, true), "your own dead commander is not marked either");

            TestAssert.That(CommandMarkerPolicy.Size(CommandTier.Theater) > CommandMarkerPolicy.Size(CommandTier.Component) &&
                            CommandMarkerPolicy.Size(CommandTier.Component) > CommandMarkerPolicy.Size(CommandTier.Base),
                "the marker shrinks down the hierarchy");

            for (float t = 0f; t < 4f; t += 0.13f)
            {
                float pulse = CommandMarkerPolicy.Pulse(t);
                TestAssert.That(pulse >= 0f && pulse <= 1f, "the alert pulse stays inside its range");
            }
            TestAssert.That(CommandMarkerPolicy.Pulse(float.NaN) == 0f &&
                            CommandMarkerPolicy.Pulse(float.PositiveInfinity) == 0f,
                "a broken clock cannot pulse");
        }

        /// <summary>
        /// The bracket a selected post wears: four corners, each an arm that opens towards
        /// the marker, all eight rules inside the bracket box. A sign slip here puts the
        /// bracket's bottom arms over its top ones and the reticle stops meaning anything.
        /// </summary>
        private static void SelectionBracketsStayOnTheMarker()
        {
            for (int tier = CommandTier.Theater; tier <= CommandTier.Base; tier++)
            {
                float size = CommandMarkerPolicy.Size(tier);
                float bracket = CommandMarkerPolicy.ReticleBracket(size);
                TestAssert.That(bracket > size, "the bracket sits outside the diamond");

                CommandMarkerPolicy.ReticleRule[] rules = CommandMarkerPolicy.Reticle(size);
                TestAssert.That(rules.Length == 8, "a bracket is eight rules");

                int top = 0, bottom = 0, left = 0, right = 0;
                for (int i = 0; i < rules.Length; i++)
                {
                    CommandMarkerPolicy.ReticleRule rule = rules[i];
                    TestAssert.That(rule.Width > 0f && rule.Height > 0f, "no rule is degenerate");

                    // A rule is one arm, not a box: 1px thick on its short side.
                    bool horizontal = rule.Width > 1f;
                    bool vertical = rule.Height > 1f;
                    TestAssert.That(horizontal != vertical, "a rule is one arm, not a box");
                    TestAssert.That((horizontal ? rule.Height : rule.Width) == 1f,
                        "a rule is one pixel thick");
                    TestAssert.That((horizontal ? rule.Width : rule.Height) < bracket * 0.5f,
                        "the arms leave the middle of the bracket open");

                    // Place(Rect) reads x to the right and y downward from the top-left.
                    TestAssert.That(rule.X >= 0f && rule.X + rule.Width <= bracket + 0.001f,
                        "a rule stays inside the bracket's width");
                    TestAssert.That(rule.Y <= 0f && rule.Y - rule.Height >= -bracket - 0.001f,
                        "a rule stays inside the bracket's height");

                    if (horizontal && rule.Y == 0f) top++;
                    if (horizontal && rule.Y == 1f - bracket) bottom++;
                    if (vertical && rule.X == 0f) left++;
                    if (vertical && rule.X == bracket - 1f) right++;
                }
                TestAssert.That(top == 2 && bottom == 2, "the bracket has a top pair and a bottom pair");
                TestAssert.That(left == 2 && right == 2, "the bracket has a left column and a right column");
            }

            TestAssert.That(CommandMarkerPolicy.Reticle(float.NaN).Length == 0 &&
                            CommandMarkerPolicy.Reticle(0f).Length == 0 &&
                            CommandMarkerPolicy.Reticle(float.PositiveInfinity).Length == 0,
                "a size a marker never carries draws no bracket");
        }

        private static void SnapshotRulesRejectMalformedRows()
        {
            TestAssert.That(CommandSnapshotRules.ValidHeader(0.5f, 5, 1), "sane header accepted");
            TestAssert.That(!CommandSnapshotRules.ValidHeader(float.NaN, 5, 1), "NaN cohesion rejected");
            TestAssert.That(!CommandSnapshotRules.ValidHeader(1.5f, 5, 1), "cohesion above one rejected");
            TestAssert.That(!CommandSnapshotRules.ValidHeader(0.5f, 999, 1), "impossible staff count rejected");
            TestAssert.That(!CommandSnapshotRules.ValidHeader(0.5f, -1, 1), "negative staff count rejected");

            TestAssert.That(CommandSnapshotRules.ValidNode(3, 1, 2, 0x3F, 12f, 0.25f, 100f, -200f),
                "every defined node flag is accepted");
            TestAssert.That(!CommandSnapshotRules.ValidNode(3, 1, 9, 0, 12f, 0.25f, 0f, 0f), "bad tier rejected");
            TestAssert.That(!CommandSnapshotRules.ValidNode(3, 1, 2, 0x80, 12f, 0.25f, 0f, 0f), "unknown flag rejected");
            TestAssert.That(!CommandSnapshotRules.ValidNode(3, 1, 2, 0, 12f, 2f, 0f, 0f), "weight above one rejected");
            TestAssert.That(!CommandSnapshotRules.ValidNode(3, 1, 2, 0, 12f, 0.25f, float.PositiveInfinity, 0f),
                "infinite coordinate rejected");

            TestAssert.That(CommandSnapshotRules.ValidLogRow(-1, (byte)CommanderLogTone.Staff, "", 0f),
                "a faction-wide log row is accepted");
            TestAssert.That(CommandSnapshotRules.ValidLogRow(3, (byte)CommanderLogTone.Alert,
                new string('X', CommandSnapshotRules.MaximumLogText), CommandSnapshotRules.MaximumLogAge),
                "a bounded log row is accepted");
            TestAssert.That(!CommandSnapshotRules.ValidLogRow(3, 6, "", 0f), "an unknown log tone is rejected");
            TestAssert.That(!CommandSnapshotRules.ValidLogRow(3, 0, "", float.NaN), "a non-finite log age is rejected");
            TestAssert.That(!CommandSnapshotRules.ValidLogRow(3, 0, "", CommandSnapshotRules.MaximumLogAge + 1f),
                "a stale log age is rejected");
            TestAssert.That(!CommandSnapshotRules.ValidLogRow(3, 0,
                new string('X', CommandSnapshotRules.MaximumLogText + 1), 0f), "overlong log text is rejected");
            TestAssert.That(!CommandSnapshotRules.ValidLogRow(CommandSnapshotRules.MaximumIdentifier, 0, "", 0f),
                "a log row past the id ceiling is rejected");
        }
    }
}
