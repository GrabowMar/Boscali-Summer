using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>What the map overlay draws for a model: world-space shapes, tag specs and the legend, built in place (annex 05 P4).</summary>
    internal static class StrOverlayTests
    {
        private const StrLayers All = StrLayers.All;

        public static void Run()
        {
            TheRichBoardDrawsEachItemOnceInDrawOrder();
            EveryShapeCarriesItsItemAndItsInk();
            RingsUseTheSnapshotRadiusAndGlyphsUseNone();
            TheAxisStartsAtTheNearestOwnMarkOutsideTheRing();
            ThePulseMarchesOnlyWhileAdvancing();
            TheWaveRouteNeedsAKnownDepot();
            AnItemWithoutAPositionAddsNothing();
            TheSamePlanComesFromTheSameModel();
            EachLayerOwnsExactlyItsItems();
            ASelectionAddsExactlyOneRing();
            AHoverOnlyWarmsTheTag();
            TheLegendNamesExactlyTheShapesPresent();
            TagWordsAreTheModelsWords();
            AFullBoardStaysInsideEveryCap();
            EveryFixtureYieldsFinitePlansInsideTheCaps();
            AMalformedModelNeverThrows();
            ARebuildAllocatesNothing();
        }

        // ---- fixtures ---------------------------------------------------------------------------------------------------------

        private static StrViewModel Model(StrUiState ui = default)
        {
            StrFeeds f = StrFixtures.Feeds("rich");
            var vm = new StrViewModel();
            StrViewBuilder.Build(vm, in f, in ui, new StrOrdinals("P"), new StrOrdinals("S"), StrFixtures.Now);
            return vm;
        }

        /// <summary>The fullest board the builder can make in Operation mode: 8 fronts (one is the hero lane), 3 proposals, 5 tied sorties, 6 posts, 8 marks, a depot.</summary>
        private static StrViewModel Full(StrUiState ui = default)
        {
            StrFeeds f = StrFixtures.Feeds("rich");
            var fronts = new List<TheaterFrontView> { StrFixtures.Front("@1,-2", "KESTREL CROSSING", 2400f, -1800f, 0.62f, 0.12f, TheaterFrontStatus.InContact) };
            for (int i = 0; i < 8; i++)
                fronts.Add(StrFixtures.Front("@" + (10 + i) + ",0", "FRONT " + i, 1000f * i, 3000f + 150f * i, 0.1f * i, 0.04f, TheaterFrontStatus.UnderPressure));
            f.War.Fronts = fronts.ToArray();
            var marks = new TheaterForceMark[8];
            for (int i = 0; i < 8; i++) marks[i] = new TheaterForceMark { X = 2400f - 400f * (i + 1), Z = -1800f + 250f * (i + 1), Stage = 3, Alive = 4 };
            f.War.Operation.Forces.Marks = marks;
            var cards = new SecondaryObjectiveView[5];
            for (int i = 0; i < 5; i++)
                cards[i] = StrFixtures.Card(40 + i, "SORTIE " + i, ObjectiveFamily.Strike, offered: i % 2 == 0, active: i % 2 == 1, secondsRemaining: 300f + i, x: 1500f + 700f * i, z: -2500f + 900f * i);
            f.Sorties = cards;
            var posts = new StrPostIn[6];
            for (int i = 0; i < 6; i++) posts[i] = StrFixtures.Post(20 + i, "OFFICER " + i, 1000 + 100 * i, 30f * i, 800f * i, 4000f - 300f * i);
            f.Posts = posts; f.PostCount = 6;
            f = StrFixtures.Stamped(f);
            var vm = new StrViewModel();
            StrViewBuilder.Build(vm, in f, in ui, new StrOrdinals("P"), new StrOrdinals("S"), StrFixtures.Now);
            return vm;
        }

        private static StrOverlayPlan Plan(StrViewModel vm, StrLayers layers = All)
        {
            var plan = new StrOverlayPlan();
            StrOverlayPlan.Build(plan, vm, layers);
            return plan;
        }

        private static int Count(StrOverlayPlan p, StrShape shape)
        {
            int n = 0;
            for (int i = 0; i < p.PrimCount; i++) if (p.Prims[i].Shape == shape) n++;
            return n;
        }

        private static int CountTags(StrOverlayPlan p, StrTagKind kind)
        {
            int n = 0;
            for (int i = 0; i < p.TagCount; i++) if (p.Tags[i].Kind == kind) n++;
            return n;
        }

        private static int IndexOf(StrOverlayPlan p, StrShape shape, StrItemKind kind)
        {
            for (int i = 0; i < p.PrimCount; i++) if (p.Prims[i].Shape == shape && p.Prims[i].Id.Kind == kind) return i;
            return -1;
        }

        private static StrTagSpec TagOf(StrOverlayPlan p, StrItemId id)
        {
            for (int i = 0; i < p.TagCount; i++) if (p.Tags[i].Id == id) return p.Tags[i];
            throw new InvalidOperationException("no tag for " + id.Kind + " " + id.Key);
        }

        private static StrPrim PrimOf(StrOverlayPlan p, StrItemId id, bool selection = false)
        {
            for (int i = 0; i < p.PrimCount; i++) if (p.Prims[i].Id == id && p.Prims[i].Selected == selection) return p.Prims[i];
            throw new InvalidOperationException("no prim for " + id.Kind + " " + id.Key);
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static string Labels(StrOverlayPlan p)
        {
            var parts = new string[p.LegendCount];
            for (int i = 0; i < p.LegendCount; i++) parts[i] = p.Legend[i].Label;
            return string.Join("|", parts);
        }

        // ---- the plan ---------------------------------------------------------------------------------------------------------

        private static void TheRichBoardDrawsEachItemOnceInDrawOrder()
        {
            StrViewModel vm = Model();
            TestAssert.That(vm.Mode == StrBoardMode.Operation && vm.FrontCount == 4 && vm.ProposalCount == 3 && vm.SortieCount == 3 && vm.PostCount == 2, "the rich board is the one this test counts");
            StrOverlayPlan p = Plan(vm);

            TestAssert.That(Count(p, StrShape.Cross) == 4, "a cross at each of the four listed fronts, the operation's own front is the effort");
            TestAssert.That(Count(p, StrShape.DashedRing) == 6, "dashed rings: the objective, three proposals, two offered sortie zones: " + Count(p, StrShape.DashedRing));
            TestAssert.That(Count(p, StrShape.Ring) == 1, "one solid ring: the accepted sortie's zone");
            TestAssert.That(Count(p, StrShape.DashedArrow) == 1 && Count(p, StrShape.Box) == 3 && Count(p, StrShape.DashedLine) == 1, "one axis, a box per group, one wave route");
            TestAssert.That(Count(p, StrShape.Diamond) == 2 && Count(p, StrShape.DiamondSolid) == 1 && Count(p, StrShape.Hex) == 2, "hollow diamonds for offered, a solid one for accepted, a hex per post");
            TestAssert.That(p.PrimCount == 21, "twenty-one shapes in all, got " + p.PrimCount);
            TestAssert.That(CountTags(p, StrTagKind.Front) == 4 && CountTags(p, StrTagKind.Effort) == 1 && CountTags(p, StrTagKind.Proposal) == 3 &&
                            CountTags(p, StrTagKind.Sortie) == 3 && CountTags(p, StrTagKind.Post) == 2 && p.TagCount == 13, "thirteen tags, one per item");

            // Draw order of the annex: crosses, objective, axis, groups, wave route, proposals, sorties, posts.
            int cross = IndexOf(p, StrShape.Cross, StrItemKind.Front), objective = IndexOf(p, StrShape.DashedRing, StrItemKind.Effort);
            int axis = IndexOf(p, StrShape.DashedArrow, StrItemKind.Effort), box = IndexOf(p, StrShape.Box, StrItemKind.Effort);
            int wave = IndexOf(p, StrShape.DashedLine, StrItemKind.Effort), proposal = IndexOf(p, StrShape.DashedRing, StrItemKind.Proposal);
            int diamond = IndexOf(p, StrShape.Diamond, StrItemKind.Sortie), hex = IndexOf(p, StrShape.Hex, StrItemKind.Post);
            TestAssert.That(cross == 0 && cross < objective && objective < axis && axis < box && box < wave && wave < proposal && proposal < diamond && diamond < hex,
                "the shapes come in the annex's draw order: " + cross + " " + objective + " " + axis + " " + box + " " + wave + " " + proposal + " " + diamond + " " + hex);
            TestAssert.That(p.Tags[0].Kind == StrTagKind.Effort, "the effort tag is placed first, so it is never displaced");
        }

        private static void EveryShapeCarriesItsItemAndItsInk()
        {
            StrViewModel vm = Model();
            StrOverlayPlan p = Plan(vm);
            for (int i = 0; i < p.PrimCount; i++)
            {
                StrPrim s = p.Prims[i];
                switch (s.Shape)
                {
                    case StrShape.Cross: TestAssert.That(s.Id.Kind == StrItemKind.Front, "a cross names its front"); break;
                    case StrShape.Box: TestAssert.That(s.Id == vm.Effort.Id && s.Ink == StrInkRole.Own, "a group box is the effort's, ink Own: only our own groups are drawn"); break;
                    case StrShape.DashedLine: TestAssert.That(s.Id == vm.Effort.Id && s.Ink == StrInkRole.Wave, "the wave route is the effort's, ink Wave"); break;
                    case StrShape.DashedArrow: TestAssert.That(s.Id == vm.Effort.Id && s.Ink == StrInkRole.Effort, "the axis is the effort's, ink Effort"); break;
                    case StrShape.Diamond: TestAssert.That(s.Id.Kind == StrItemKind.Sortie && s.Ink == StrInkRole.Offered, "an offered sortie is ink Offered"); break;
                    case StrShape.DiamondSolid: TestAssert.That(s.Id.Kind == StrItemKind.Sortie && s.Ink == StrInkRole.Effort, "an accepted sortie is ink Effort"); break;
                    case StrShape.Hex: TestAssert.That(s.Id.Kind == StrItemKind.Post && s.Ink == StrInkRole.Strike, "a post is a hex in ink Strike"); break;
                }
            }
            for (int k = 0; k < vm.FrontCount; k++)
                TestAssert.That(PrimOf(p, vm.Fronts[k].Id).Ink == vm.Fronts[k].Ink && PrimOf(p, vm.Fronts[k].Id).X == vm.Fronts[k].X, "front cross " + k + " sits on the row's anchor in the row's ink");
            StrPrim obj = p.Prims[IndexOf(p, StrShape.DashedRing, StrItemKind.Effort)];
            TestAssert.That(obj.Id == vm.Effort.Id && obj.Ink == StrInkRole.Effort && obj.X == vm.Effort.X && obj.Z == vm.Effort.Z, "the objective ring is at the fix, ink Effort");
            for (int k = 0; k < vm.ProposalCount; k++)
                TestAssert.That(PrimOf(p, vm.Proposals[k].Id).Ink == StrInkRole.Muted, "while an operation runs the proposals are muted ghosts");
        }

        private static void RingsUseTheSnapshotRadiusAndGlyphsUseNone()
        {
            StrViewModel vm = Model();
            StrOverlayPlan p = Plan(vm);
            TestAssert.That(p.Prims[IndexOf(p, StrShape.DashedRing, StrItemKind.Effort)].RadiusMetres == vm.Effort.Radius && vm.Effort.Radius == 1200f, "the objective ring has the snapshot's radius, not a constant");
            for (int k = 0; k < vm.ProposalCount; k++) TestAssert.That(PrimOf(p, vm.Proposals[k].Id).RadiusMetres == vm.Proposals[k].Radius, "a proposal ring has the proposal's radius");
            for (int k = 0; k < vm.SortieCount; k++)
            {
                StrSortieRow s = vm.Sorties[k];
                bool found = false;
                for (int i = 0; i < p.PrimCount; i++)
                    if (p.Prims[i].Id == s.Id && (p.Prims[i].Shape == StrShape.Ring || p.Prims[i].Shape == StrShape.DashedRing))
                    {
                        found = true;
                        TestAssert.That(p.Prims[i].RadiusMetres == s.Radius && s.Radius == 800f, "a sortie zone has the card's radius");
                        TestAssert.That((p.Prims[i].Shape == StrShape.Ring) == s.Active, "a zone is dashed while offered and solid once accepted");
                    }
                TestAssert.That(found, "every sortie with a site and a radius has a zone");
            }
            for (int i = 0; i < p.PrimCount; i++)
            {
                StrShape sh = p.Prims[i].Shape;
                if (sh == StrShape.Cross || sh == StrShape.Box || sh == StrShape.Diamond || sh == StrShape.DiamondSolid || sh == StrShape.Hex)
                    TestAssert.That(p.Prims[i].RadiusMetres == 0f, sh + " is a fixed-size glyph and carries no metres");
            }

            // A radius of zero is no ring at all, never a ring of the glyph size.
            vm.Effort.Radius = 0f; vm.Proposals[0].Radius = 0f; vm.Sorties[0].Radius = 0f;
            StrOverlayPlan z = Plan(vm);
            TestAssert.That(Count(z, StrShape.DashedRing) + Count(z, StrShape.Ring) == Count(p, StrShape.DashedRing) + Count(p, StrShape.Ring) - 3, "a zero radius draws no ring (the objective, one proposal, one sortie)");
            TestAssert.That(z.TagCount == p.TagCount, "but its tag stays");
        }

        private static void TheAxisStartsAtTheNearestOwnMarkOutsideTheRing()
        {
            StrViewModel vm = Model();
            TestAssert.That(vm.Effort.MarkCount == 3 && vm.Effort.X == 2400f && vm.Effort.Z == -1800f && vm.Effort.Radius == 1200f, "the rich operation has three marks round a 1200 m ring");
            // Marks, nearest the fix first: G1 is inside the ring, G2 is the nearest one outside it, G3 is further out.
            StrGroupRow g2 = vm.Effort.Marks[1];
            StrOverlayPlan p = Plan(vm);
            StrPrim axis = p.Prims[IndexOf(p, StrShape.DashedArrow, StrItemKind.Effort)];
            TestAssert.That(axis.X == g2.X && axis.Z == g2.Z && axis.X2 == vm.Effort.X && axis.Z2 == vm.Effort.Z, "the axis runs from the nearest group outside the ring to the fix");

            // Every group inside the ring: the operation's origin.
            vm.Effort.Marks[1].X = vm.Effort.X + 100f; vm.Effort.Marks[1].Z = vm.Effort.Z;
            vm.Effort.Marks[2].X = vm.Effort.X; vm.Effort.Marks[2].Z = vm.Effort.Z - 200f;
            StrOverlayPlan q = Plan(vm);
            axis = q.Prims[IndexOf(q, StrShape.DashedArrow, StrItemKind.Effort)];
            TestAssert.That(axis.X == vm.Effort.OriginX && axis.Z == vm.Effort.OriginZ, "with every group inside the ring the axis starts at the origin");

            // A group exactly on the ring is not outside it.
            vm.Effort.Marks[1].X = vm.Effort.X + vm.Effort.Radius; vm.Effort.Marks[1].Z = vm.Effort.Z;
            q = Plan(vm);
            axis = q.Prims[IndexOf(q, StrShape.DashedArrow, StrItemKind.Effort)];
            TestAssert.That(axis.X == vm.Effort.OriginX, "a group on the ring is not outside it");

            // A mark with no position is skipped, not used.
            vm.Effort.Marks[1].X = float.NaN;
            vm.Effort.Marks[0].X = vm.Effort.X + 5000f; vm.Effort.Marks[0].Z = vm.Effort.Z;
            q = Plan(vm);
            axis = q.Prims[IndexOf(q, StrShape.DashedArrow, StrItemKind.Effort)];
            TestAssert.That(axis.X == vm.Effort.X + 5000f, "an unplaced group is skipped and a placed one outside the ring is used");

            // No usable group and no origin: no axis, rather than an arrow from nowhere.
            vm.Effort.MarkCount = 0; vm.Effort.OriginX = float.NaN;
            StrOverlayPlan none = Plan(vm);
            TestAssert.That(Count(none, StrShape.DashedArrow) == 0 && Count(none, StrShape.Box) == 0, "no groups and no origin draw no axis");
            // An origin on the fix is no axis either.
            vm.Effort.OriginX = vm.Effort.X; vm.Effort.OriginZ = vm.Effort.Z;
            TestAssert.That(Count(Plan(vm), StrShape.DashedArrow) == 0, "an origin on the fix has no length to draw");
        }

        private static void ThePulseMarchesOnlyWhileAdvancing()
        {
            StrViewModel vm = Model();
            StrOverlayPlan p = Plan(vm);
            TestAssert.That(vm.Effort.Advancing, "the rich operation is advancing");
            int pulses = 0;
            for (int i = 0; i < p.PrimCount; i++)
                if (p.Prims[i].Pulse) { pulses++; TestAssert.That(p.Prims[i].Shape == StrShape.DashedArrow && p.Prims[i].Id == vm.Effort.Id, "only the axis pulses"); }
            TestAssert.That(pulses == 1 && p.EstimatePulseQuads() > 0, "one pulsing shape and a pulse mesh to hold it");

            vm.Effort.Advancing = false;
            StrOverlayPlan still = Plan(vm);
            TestAssert.That(Count(still, StrShape.DashedArrow) == 1 && still.EstimatePulseQuads() == 0, "a stopped operation keeps its axis but nothing pulses");
            for (int i = 0; i < still.PrimCount; i++) TestAssert.That(!still.Prims[i].Pulse, "no pulse while not advancing");
            TestAssert.That(still.EstimateInkQuads() > p.EstimateInkQuads(), "the still axis is paid for in the ink mesh, the pulsing one in the pulse mesh");
        }

        private static void TheWaveRouteNeedsAKnownDepot()
        {
            StrViewModel vm = Model();
            StrOverlayPlan p = Plan(vm);
            StrPrim line = p.Prims[IndexOf(p, StrShape.DashedLine, StrItemKind.Effort)];
            TestAssert.That(line.X == vm.Wave.DepotX && line.Z == vm.Wave.DepotZ && line.X2 == vm.Effort.X && line.Z2 == vm.Effort.Z, "the route runs from the depot to the fix");

            vm.Wave.DepotKnown = false;
            TestAssert.That(Count(Plan(vm), StrShape.DashedLine) == 0, "no known depot, no route");
            vm.Wave.DepotKnown = true; vm.Wave.DepotX = float.NaN;
            TestAssert.That(Count(Plan(vm), StrShape.DashedLine) == 0, "a depot with no position, no route");
            vm.Wave.DepotX = 700f; vm.Wave.Present = false;
            TestAssert.That(Count(Plan(vm), StrShape.DashedLine) == 0, "no wave desk, no route");
            vm.Wave.Present = true; vm.Effort.Live = false;
            TestAssert.That(Count(Plan(vm), StrShape.DashedLine) == 0, "no live operation, no route");
        }

        private static void AnItemWithoutAPositionAddsNothing()
        {
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                StrViewModel baseVm = Model();
                StrOverlayPlan b = Plan(baseVm);

                StrViewModel v = Model(); v.Fronts[0].X = bad;
                StrOverlayPlan p = Plan(v);
                TestAssert.That(p.PrimCount == b.PrimCount - 1 && p.TagCount == b.TagCount - 1, "a front at " + bad + " loses its cross and its tag");

                v = Model(); v.Effort.Z = bad;
                p = Plan(v);
                TestAssert.That(p.PrimCount == b.PrimCount - 6 && p.TagCount == b.TagCount - 1 && CountTags(p, StrTagKind.Effort) == 0,
                    "an effort at " + bad + " loses its ring, axis, three groups, route and tag: " + p.PrimCount + "/" + b.PrimCount);
                TestAssert.That(CountTags(p, StrTagKind.Proposal) == 3, "the proposals do not depend on the effort's position");

                v = Model(); v.Effort.Marks[0].Z = bad;
                p = Plan(v);
                TestAssert.That(p.PrimCount == b.PrimCount - 1 && Count(p, StrShape.Box) == 2, "a group at " + bad + " loses its box");

                v = Model(); v.Proposals[1].X = bad;
                p = Plan(v);
                TestAssert.That(p.PrimCount == b.PrimCount - 1 && p.TagCount == b.TagCount - 1, "a proposal at " + bad + " loses its ring and its tag");

                v = Model(); v.Sorties[0].X = bad;
                p = Plan(v);
                TestAssert.That(p.PrimCount == b.PrimCount - 2 && p.TagCount == b.TagCount - 1, "a sortie at " + bad + " loses its diamond, its zone and its tag");

                v = Model(); v.Posts[0].Z = bad;
                p = Plan(v);
                TestAssert.That(p.PrimCount == b.PrimCount - 1 && p.TagCount == b.TagCount - 1, "a post at " + bad + " loses its hex and its tag");

                v = Model(); v.Wave.DepotZ = bad;
                TestAssert.That(Plan(v).PrimCount == b.PrimCount - 1, "a depot at " + bad + " loses the route");
            }

            // Nothing is placed anywhere when nothing has a position, and nothing lands on the map origin.
            StrViewModel all = Model();
            for (int i = 0; i < all.FrontCount; i++) all.Fronts[i].X = float.NaN;
            all.Effort.X = float.NaN;
            for (int i = 0; i < all.ProposalCount; i++) all.Proposals[i].Z = float.NaN;
            for (int i = 0; i < all.SortieCount; i++) all.Sorties[i].X = float.NaN;
            for (int i = 0; i < all.PostCount; i++) all.Posts[i].X = float.NaN;
            StrOverlayPlan empty = Plan(all);
            TestAssert.That(empty.PrimCount == 0 && empty.TagCount == 0 && empty.LegendCount == 0, "a model with no positions draws nothing at all");
            TestAssert.That(empty.EstimateInkQuads() == 0 && empty.EstimatePulseQuads() == 0, "and costs nothing");
        }

        private static void TheSamePlanComesFromTheSameModel()
        {
            StrViewModel vm = Full(new StrUiState { Selected = new StrItemId(StrItemKind.Proposal, 23) });
            StrOverlayPlan a = Plan(vm), b = Plan(vm);
            TestAssert.That(Same(a, b), "the same model builds the same plan twice");

            // A plan that held something else is cleared first, not added to.
            var reused = new StrOverlayPlan();
            StrOverlayPlan.Build(reused, Model(), All);
            StrOverlayPlan.Build(reused, vm, All);
            TestAssert.That(Same(a, reused), "building into a used plan leaves nothing of the old one");
            StrOverlayPlan.Build(reused, vm, StrLayers.None);
            TestAssert.That(reused.PrimCount == 0 && reused.TagCount == 0 && reused.LegendCount == 0 && reused.EstimateInkQuads() == 0, "no layer on builds an empty plan");
            StrOverlayPlan.Build(reused, null, All);
            TestAssert.That(reused.PrimCount == 0 && reused.TagCount == 0, "no model builds an empty plan");
        }

        private static bool Same(StrOverlayPlan a, StrOverlayPlan b)
        {
            if (a.PrimCount != b.PrimCount || a.TagCount != b.TagCount || a.LegendCount != b.LegendCount) return false;
            if (a.EstimateInkQuads() != b.EstimateInkQuads() || a.EstimatePulseQuads() != b.EstimatePulseQuads()) return false;
            for (int i = 0; i < a.PrimCount; i++)
            {
                StrPrim x = a.Prims[i], y = b.Prims[i];
                if (x.Shape != y.Shape || x.Ink != y.Ink || x.Id != y.Id || x.X != y.X || x.Z != y.Z || x.X2 != y.X2 || x.Z2 != y.Z2 || x.RadiusMetres != y.RadiusMetres || x.Pulse != y.Pulse || x.Selected != y.Selected) return false;
            }
            for (int i = 0; i < a.TagCount; i++)
            {
                StrTagSpec x = a.Tags[i], y = b.Tags[i];
                if (x.Id != y.Id || x.Kind != y.Kind || x.X != y.X || x.Z != y.Z || x.Text != y.Text || x.Sub != y.Sub || x.Ink != y.Ink ||
                    !x.Value.Equals(y.Value) || !x.Trend.Equals(y.Trend) || x.Ghost != y.Ghost || x.Hot != y.Hot || x.Selected != y.Selected) return false;
            }
            for (int i = 0; i < a.LegendCount; i++)
                if (a.Legend[i].Label != b.Legend[i].Label || a.Legend[i].Shape != b.Legend[i].Shape || a.Legend[i].Ink != b.Legend[i].Ink) return false;
            return true;
        }

        private static void EachLayerOwnsExactlyItsItems()
        {
            StrViewModel vm = Model(new StrUiState { Selected = new StrItemId(StrItemKind.Proposal, 23) });
            StrOverlayPlan all = Plan(vm);
            int[] prims = new int[8], tags = new int[8];
            for (int i = 0; i < all.PrimCount; i++) prims[(int)StrOverlayPlan.LayerOf(all.Prims[i].Id.Kind)]++;
            for (int i = 0; i < all.TagCount; i++) tags[(int)StrOverlayPlan.LayerOf(all.Tags[i].Id.Kind)]++;
            TestAssert.That(prims[0] == 0 && tags[0] == 0, "every shape and tag belongs to a layer");
            TestAssert.That(prims[(int)StrLayers.Fronts] == 4 && prims[(int)StrLayers.Operation] > 0 && prims[(int)StrLayers.Sorties] > 0, "the three layers all have shapes on the rich board");

            TestAssert.That(StrOverlayPlan.LayerOf(StrItemKind.Front) == StrLayers.Fronts && StrOverlayPlan.LayerOf(StrItemKind.Effort) == StrLayers.Operation &&
                            StrOverlayPlan.LayerOf(StrItemKind.Proposal) == StrLayers.Operation && StrOverlayPlan.LayerOf(StrItemKind.Sortie) == StrLayers.Sorties &&
                            StrOverlayPlan.LayerOf(StrItemKind.Post) == StrLayers.Sorties && StrOverlayPlan.LayerOf(StrItemKind.None) == StrLayers.None, "an item's layer follows from its kind");

            for (int mask = 0; mask < 8; mask++)
            {
                var layers = (StrLayers)mask;
                StrOverlayPlan p = Plan(vm, layers);
                int wantPrims = 0, wantTags = 0;
                for (int bit = 1; bit <= 4; bit <<= 1)
                    if ((mask & bit) != 0) { wantPrims += prims[bit]; wantTags += tags[bit]; }
                TestAssert.That(p.PrimCount == wantPrims && p.TagCount == wantTags, layers + " draws exactly its own " + wantPrims + " shapes and " + wantTags + " tags, got " + p.PrimCount + "/" + p.TagCount);
                for (int i = 0; i < p.PrimCount; i++) TestAssert.That((StrOverlayPlan.LayerOf(p.Prims[i].Id.Kind) & layers) != 0, layers + " drew a shape of another layer");
                for (int i = 0; i < p.TagCount; i++) TestAssert.That((StrOverlayPlan.LayerOf(p.Tags[i].Id.Kind) & layers) != 0, layers + " drew a tag of another layer");
            }
        }

        private static void ASelectionAddsExactlyOneRing()
        {
            StrViewModel plain = Model();
            StrOverlayPlan b = Plan(plain);
            StrItemId prop = new StrItemId(StrItemKind.Proposal, 23);

            StrViewModel vm = Model(new StrUiState { Selected = prop });
            StrOverlayPlan p = Plan(vm);
            TestAssert.That(vm.SelectedId == prop, "the builder resolved the selection");
            TestAssert.That(p.PrimCount == b.PrimCount + 1 && p.TagCount == b.TagCount, "selecting a proposal adds exactly one shape and no tag");
            int rings = 0;
            for (int i = 0; i < p.PrimCount; i++) if (p.Prims[i].Selected) rings++;
            StrPrim hi = PrimOf(p, prop, true);
            StrProposalRow row = vm.Proposals[1];
            TestAssert.That(rings == 1 && hi.Shape == StrShape.Ring && hi.Ink == StrInkRole.Effort && hi.X == row.X && hi.Z == row.Z && hi.RadiusMetres == row.Radius, "that shape is one highlight ring on the proposal, in its radius");
            TestAssert.That(PrimOf(p, prop).Shape == StrShape.Ring && PrimOf(b, prop).Shape == StrShape.DashedRing, "and the proposal's own ring turns solid");
            StrTagSpec tag = TagOf(p, prop);
            TestAssert.That(tag.Selected && !tag.Hot && !TagOf(b, prop).Selected, "its tag reads selected");
            TestAssert.That(tag.Ink == StrInkRole.Effort && TagOf(b, prop).Ink == StrInkRole.Muted, "and leaves the ghost ink");

            // Every kind of item can be selected and gets its ring at its place.
            StrItemId[] picks = { vm.Effort.Id, plain.Fronts[0].Id, plain.Sorties[0].Id, plain.Posts[0].Id };
            float[] radii = { plain.Effort.Radius, 0f, plain.Sorties[0].Radius, 0f };
            float[] xs = { plain.Effort.X, plain.Fronts[0].X, plain.Sorties[0].X, plain.Posts[0].X };
            for (int k = 0; k < picks.Length; k++)
            {
                StrViewModel sv = Model(new StrUiState { Selected = picks[k] });
                StrOverlayPlan sp = Plan(sv);
                StrPrim ring = PrimOf(sp, picks[k], true);
                TestAssert.That(sp.PrimCount == b.PrimCount + 1 && ring.Shape == StrShape.Ring && ring.X == xs[k] && ring.RadiusMetres == radii[k], picks[k].Kind + " gets one highlight ring at its place");
                TestAssert.That(TagOf(sp, picks[k]).Selected, picks[k].Kind + " tag reads selected");
            }

            // A selection the model does not hold, or that has no place, adds nothing.
            StrViewModel gone = Model(); gone.SelectedId = new StrItemId(StrItemKind.Proposal, 999);
            TestAssert.That(Plan(gone).PrimCount == b.PrimCount, "a selected id that is not in the model adds nothing");
            StrViewModel unplaced = Model(new StrUiState { Selected = prop }); unplaced.Proposals[1].X = float.NaN;
            TestAssert.That(Plan(unplaced).PrimCount == b.PrimCount - 1, "a selected proposal with no position adds no ring (and loses its own)");

            // The ring belongs to its item's layer: with that layer off there is nothing to highlight.
            StrOverlayPlan off = Plan(vm, StrLayers.Fronts | StrLayers.Sorties);
            for (int i = 0; i < off.PrimCount; i++) TestAssert.That(!off.Prims[i].Selected, "a highlight does not outlive its layer");
        }

        private static void AHoverOnlyWarmsTheTag()
        {
            StrViewModel plain = Model();
            StrOverlayPlan b = Plan(plain);
            StrItemId prop = new StrItemId(StrItemKind.Proposal, 22);
            StrViewModel vm = Model(new StrUiState { Hover = prop });
            StrOverlayPlan p = Plan(vm);
            TestAssert.That(p.PrimCount == b.PrimCount, "a hover adds no highlight ring");
            TestAssert.That(TagOf(p, prop).Hot && !TagOf(p, prop).Selected && !TagOf(b, prop).Hot, "it only warms the tag");
            TestAssert.That(PrimOf(p, prop).Shape == StrShape.Ring && PrimOf(p, prop).Ink == StrInkRole.Effort, "and turns the proposal's ring solid in the effort ink");
            int hot = 0;
            for (int i = 0; i < p.TagCount; i++) if (p.Tags[i].Hot) hot++;
            TestAssert.That(hot == 1, "one hovered tag");
        }

        private static void TheLegendNamesExactlyTheShapesPresent()
        {
            StrViewModel vm = Model();
            const string notch = "NOTCH = HOSTILE SHARE";
            TestAssert.That(Plan(new StrViewModel()).LegendCount == 0, "an empty board has no legend");
            TestAssert.That(Labels(Plan(vm, StrLayers.Fronts)) == "FRONT|" + notch, "fronts only: the platform cross and the lane notch: " + Labels(Plan(vm, StrLayers.Fronts)));
            TestAssert.That(Labels(Plan(vm, StrLayers.Operation)) == "OPERATION AXIS|OBJECTIVE|ASSIGNED GROUP|WAVE ROUTE|PROPOSAL|" + notch, "operation only: " + Labels(Plan(vm, StrLayers.Operation)));
            TestAssert.That(Labels(Plan(vm, StrLayers.Sorties)) == "SORTIE OFFERED|SORTIE ACCEPTED|STRIKE TARGET", "sorties only has no notch: " + Labels(Plan(vm, StrLayers.Sorties)));
            StrOverlayPlan all = Plan(vm);
            TestAssert.That(Labels(all) == "FRONT|OPERATION AXIS|OBJECTIVE|ASSIGNED GROUP|WAVE ROUTE|PROPOSAL|SORTIE OFFERED|SORTIE ACCEPTED|STRIKE TARGET|" + notch && all.LegendCount == StrOverlayPlan.MaxLegend,
                "all three layers name all ten: " + Labels(all));

            for (int i = 0; i < all.LegendCount; i++)
            {
                StrLegendEntry e = all.Legend[i];
                if (e.Shape == StrShape.Notch) continue;
                bool present = false;
                for (int k = 0; k < all.PrimCount; k++) present |= all.Prims[k].Shape == e.Shape && !all.Prims[k].Selected;
                TestAssert.That(present, e.Label + " swatch is a shape the plan really contains");
            }

            // Take the pieces away and their entries go.
            vm.Effort.Advancing = true; vm.Wave.DepotKnown = false; vm.PostCount = 0;
            TestAssert.That(!Labels(Plan(vm)).Contains("WAVE ROUTE") && !Labels(Plan(vm)).Contains("STRIKE TARGET") && Labels(Plan(vm)).Contains("OPERATION AXIS"), "no depot, no route entry; no post, no strike entry");
            for (int i = 0; i < vm.SortieCount; i++) vm.Sorties[i].Offered = false;
            TestAssert.That(!Labels(Plan(vm)).Contains("SORTIE OFFERED") && Labels(Plan(vm)).Contains("SORTIE ACCEPTED"), "no offered sortie, no offered entry");
            for (int i = 0; i < vm.FrontCount; i++) vm.Fronts[i].Pressure = float.NaN;
            vm.Effort.Pressure = float.NaN;
            TestAssert.That(!Labels(Plan(vm)).Contains("NOTCH"), "when no lane has a value no notch is explained");
        }

        private static void TagWordsAreTheModelsWords()
        {
            StrViewModel vm = Model();
            StrOverlayPlan p = Plan(vm);

            StrEffortView e = vm.Effort;
            StrTagSpec eff = TagOf(p, e.Id);
            TestAssert.That(eff.Kind == StrTagKind.Effort && eff.Text == e.Tag + " " + e.KindText && eff.Text == "01 MAIN EFFORT", "the effort tag carries the main effort chip: " + eff.Text);
            TestAssert.That(eff.Sub == e.FrontTag + " " + e.Name && eff.Sub == "P1 KESTREL CROSSING", "and its front's platform and place: " + eff.Sub);
            TestAssert.That(eff.Ink == StrInkRole.Effort && eff.X == e.X && eff.Z == e.Z && eff.Value == e.Pressure && eff.Trend == e.Trend && !eff.Ghost, "at the fix, with the front's lane value");

            for (int k = 0; k < vm.FrontCount; k++)
            {
                StrFrontRow r = vm.Fronts[k];
                StrTagSpec t = TagOf(p, r.Id);
                TestAssert.That(t.Kind == StrTagKind.Front && t.Text == r.Tag + " " + r.Name && t.Ink == r.Ink && t.X == r.X && t.Z == r.Z, "front " + k + " plate is its platform and place");
                if (r.Observed)
                {
                    TestAssert.That(t.Sub == (r.TrendText.Length > 0 ? r.PressureText + " " + r.TrendText : r.PressureText), "front " + k + " second line is the pressure and the trend: " + t.Sub);
                    TestAssert.That(t.Value == r.Pressure && t.Trend.Equals(r.Trend), "front " + k + " lane value is the row's");
                }
                else
                {
                    TestAssert.That(t.Sub == r.StatusText && float.IsNaN(t.Value) && float.IsNaN(t.Trend), "an unobserved front says so and has no value to bar: " + t.Sub);
                }
            }

            for (int k = 0; k < vm.ProposalCount; k++)
            {
                StrProposalRow r = vm.Proposals[k];
                StrTagSpec t = TagOf(p, r.Id);
                TestAssert.That(t.Kind == StrTagKind.Proposal && t.Text == r.Tag + " " + r.KindText && t.Sub == r.Name && t.Ghost && t.Ink == StrInkRole.Muted, "proposal " + k + " is a small ghost tag while an operation runs");
                TestAssert.That(float.IsNaN(t.Value) && float.IsNaN(t.Trend), "a proposal tag has no lane");
            }

            for (int k = 0; k < vm.SortieCount; k++)
            {
                StrSortieRow r = vm.Sorties[k];
                StrTagSpec t = TagOf(p, r.Id);
                TestAssert.That(t.Kind == StrTagKind.Sortie && t.Text == r.Tag && t.Sub == r.Title && t.Ink == (r.Offered ? StrInkRole.Offered : StrInkRole.Effort) && !t.Ghost, "sortie " + k + " tag is its number and title");
            }

            for (int k = 0; k < vm.PostCount; k++)
            {
                StrPostRow r = vm.Posts[k];
                StrTagSpec t = TagOf(p, r.Id);
                TestAssert.That(t.Kind == StrTagKind.Post && t.Text == r.Name && t.Sub == r.BountyText && t.Ink == StrInkRole.Strike, "a post with no ticket shows the officer and the bounty");
            }
            vm.Posts[0].Ticket = vm.Sorties[0].Id;
            StrTagSpec ticketed = TagOf(Plan(vm), vm.Posts[0].Id);
            TestAssert.That(ticketed.Text == vm.Sorties[0].Tag + " STRIKE" && ticketed.Sub == vm.Posts[0].Name + StrWords.Dot + vm.Posts[0].BountyText, "a post with a strike ticket shows the ticket, the officer and the bounty: " + ticketed.Text + " / " + ticketed.Sub);

            // With no operation the proposals are the options, not ghosts.
            StrFeeds f = StrFixtures.Feeds("proposals");
            var deck = new StrViewModel();
            var none = default(StrUiState);
            StrViewBuilder.Build(deck, in f, in none, new StrOrdinals("P"), new StrOrdinals("S"), StrFixtures.Now);
            StrOverlayPlan dp = Plan(deck);
            TestAssert.That(deck.Mode == StrBoardMode.Proposals && !deck.Effort.Live && dp.TagCount == 5 + 3, "the deck has a plate per front and a tag per proposal");
            TestAssert.That(CountTags(dp, StrTagKind.Effort) == 0 && Count(dp, StrShape.DashedArrow) == 0 && Count(dp, StrShape.DashedLine) == 0, "and no effort, axis or route");
            for (int k = 0; k < deck.ProposalCount; k++)
                TestAssert.That(!TagOf(dp, deck.Proposals[k].Id).Ghost && TagOf(dp, deck.Proposals[k].Id).Ink == StrInkRole.Effort && PrimOf(dp, deck.Proposals[k].Id).Ink == StrInkRole.Effort, "an option is not a ghost");
        }

        private static void AFullBoardStaysInsideEveryCap()
        {
            var ui = new StrUiState { Selected = new StrItemId(StrItemKind.Proposal, 23), Hover = new StrItemId(StrItemKind.Proposal, 22) };
            StrViewModel vm = Full(ui);
            TestAssert.That(vm.Mode == StrBoardMode.Operation && vm.FrontCount == 7 && vm.ProposalCount == 3 && vm.SortieCount == 5 && vm.PostCount == 6 && vm.Effort.MarkCount == 8,
                "the full board is full: " + vm.FrontCount + " fronts, " + vm.ProposalCount + " proposals, " + vm.SortieCount + " sorties, " + vm.PostCount + " posts, " + vm.Effort.MarkCount + " marks");
            StrOverlayPlan p = Plan(vm);
            TestAssert.That(p.TagCount == 22, "twenty-two tags: seven plates, the effort, three proposals, five sorties, six posts, got " + p.TagCount);
            TestAssert.That(p.PrimCount <= StrOverlayPlan.MaxPrims && p.PrimCount == 38, "thirty-eight shapes inside the 48 cap, got " + p.PrimCount);
            TestAssert.That(p.TagCount <= StrOverlayPlan.MaxTags && p.LegendCount <= StrOverlayPlan.MaxLegend, "tags and legend inside their caps");
            TestAssert.That(p.EstimateInkQuads() <= 900, "the ink mesh fits 900 quads, estimate " + p.EstimateInkQuads());
            TestAssert.That(p.EstimatePulseQuads() <= 120, "the pulse mesh fits 120 quads, estimate " + p.EstimatePulseQuads());
            TestAssert.That(p.EstimateInkQuads() >= 300, "and the estimate is a real worst case, not zero: " + p.EstimateInkQuads());
            TestAssert.That(StrOverlayPlan.MaxPrims == 48 && StrOverlayPlan.MaxTags == 24 && StrOverlayPlan.MaxLegend == 10 && p.Prims.Length == 48 && p.Tags.Length == 24 && p.Legend.Length == 10, "the caps are the annex's");

            // The marks are drawn nearest-the-fix first like the rows, up to eight boxes.
            TestAssert.That(Count(p, StrShape.Box) == 8, "eight groups, eight boxes");
        }

        private static void EveryFixtureYieldsFinitePlansInsideTheCaps()
        {
            foreach (string name in StrFixtures.Names)
            {
                StrViewModel vm = StrFixtures.Model(name);
                StrOverlayPlan p = Plan(vm);
                TestAssert.That(p.PrimCount <= 48 && p.TagCount <= 24 && p.EstimateInkQuads() <= 900 && p.EstimatePulseQuads() <= 120, name + " stays inside the caps");
                for (int i = 0; i < p.PrimCount; i++)
                {
                    StrPrim s = p.Prims[i];
                    TestAssert.That(Finite(s.X) && Finite(s.Z) && Finite(s.RadiusMetres) && s.RadiusMetres >= 0f, name + " shape " + i + " is finite");
                    if (s.Shape == StrShape.DashedLine || s.Shape == StrShape.DashedArrow || s.Shape == StrShape.Arrow)
                        TestAssert.That(Finite(s.X2) && Finite(s.Z2) && (s.X != s.X2 || s.Z != s.Z2), name + " line " + i + " has two distinct finite ends");
                }
                for (int i = 0; i < p.TagCount; i++)
                    TestAssert.That(Finite(p.Tags[i].X) && Finite(p.Tags[i].Z) && !string.IsNullOrEmpty(p.Tags[i].Text), name + " tag " + i + " is finite and says something");
            }
            TestAssert.That(Plan(StrFixtures.Model("unknown")).TagCount > 0, "even the every-unknown board keeps the places it does know");
            StrOverlayPlan u = Plan(StrFixtures.Model("unknown"));
            for (int i = 0; i < u.TagCount; i++)
                if (u.Tags[i].Kind == StrTagKind.Front) TestAssert.That(float.IsNaN(u.Tags[i].Value), "an unobserved front is hatched, never a zero");
        }

        private static void AMalformedModelNeverThrows()
        {
            TestAssert.That(Plan(null).PrimCount == 0, "no model draws nothing");
            StrOverlayPlan.Build(null, Model(), All);

            StrViewModel vm = Model();
            vm.FrontCount = 500; vm.ProposalCount = 500; vm.SortieCount = 500; vm.PostCount = 500; vm.Effort.MarkCount = 500;
            StrOverlayPlan big = Plan(vm);
            TestAssert.That(big.PrimCount <= 48 && big.TagCount <= 24, "counts past the arrays are clamped");

            vm = Model();
            vm.Fronts[1] = null; vm.Proposals[0] = null; vm.Sorties[2] = null; vm.Posts[1] = null; vm.Wave = null;
            StrOverlayPlan holes = Plan(vm);
            TestAssert.That(holes.TagCount == 13 - 4, "a missing row is skipped, the rest still draws");

            vm = Model();
            vm.Effort.Marks = null; vm.Effort.Live = true;
            TestAssert.That(Count(Plan(vm), StrShape.Box) == 0, "a missing marks array draws no groups");
            vm.Fronts = null; vm.Proposals = null; vm.Sorties = null; vm.Posts = null; vm.Effort = null;
            TestAssert.That(Plan(vm).PrimCount == 0, "a model with its arrays gone draws nothing");

            vm = Model();
            vm.Effort.X = 1e30f; vm.Effort.Radius = 1e30f;
            TestAssert.That(Plan(vm).PrimCount > 0, "a huge but finite fix is still drawn");
        }

        private static void ARebuildAllocatesNothing()
        {
            var ui = new StrUiState { Selected = new StrItemId(StrItemKind.Proposal, 23), Hover = new StrItemId(StrItemKind.Proposal, 22) };
            foreach (StrViewModel vm in new[] { Model(ui), Full(ui) })
            {
                vm.Posts[0].Ticket = vm.Sorties[0].Id;
                var plan = new StrOverlayPlan();
                StrOverlayPlan.Build(plan, vm, All);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) StrOverlayPlan.Build(plan, vm, All);
                long after = GC.GetAllocatedBytesForCurrentThread();
                TestAssert.That(after - before == 0, "a hundred rebuilds of an unchanged model allocate " + (after - before) + " bytes");
                int est = plan.EstimateInkQuads();
                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) { StrOverlayPlan.Build(plan, vm, i % 2 == 0 ? All : StrLayers.Fronts); est += plan.EstimateInkQuads() + plan.EstimatePulseQuads(); }
                after = GC.GetAllocatedBytesForCurrentThread();
                TestAssert.That(after - before == 0 && est > 0, "and so do layer toggles, after the first pass has joined the words: " + (after - before) + " bytes");
            }
        }
    }
}
