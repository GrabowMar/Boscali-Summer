using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>ACE3 interact_menu geometry and navigation for the interaction menu.</summary>
    internal static class AceRadialMathTests
    {
        private static readonly AceVec2 Center = new AceVec2(960f, 540f);

        public static void Run()
        {
            FanUsesAceStepsAndCentresOnParent();
            FanBecomesRingAtThreshold();
            SubLevelSqueezesIntoSpan();
            RadiusWidensAsStepNarrows();
            ExpandScale();
            ClosestPointWithinThreshold();
            CollectDropsHiddenAndPrunesEmptyBranches();
            CollectHonoursNodeBudget();
            RootOptionsFanRightOfCentre();
            DwellExpandsBranchAndKeepsBreadcrumb();
            LeafDwellCollapsesDeeperLevels();
            ReleaseRunsOnlyEnabledLeaf();
            ConditionsAreRecheckedBeforeRunning();
            ClickExpandsBranchImmediately();
            StatusAndIconTravelWithTheNode();
            StatusIsReadAgainOnRecollect();
            LayoutStopsAtVisibleCap();
            ToggleStatusReadsOnOff();
        }

        private static void FanUsesAceStepsAndCentresOnParent()
        {
            float[] angles = AceRadialMath.FanAngles(3, 0f, AceRadialMath.RootSpanDeg, out float interval);
            Near(interval, 55f, "three options step 55 degrees");
            Near(angles[0], -55f, "first option sits below the parent direction");
            Near(angles[1], 0f, "middle option follows the parent direction");
            Near(angles[2], 55f, "last option sits above the parent direction");

            float[] single = AceRadialMath.FanAngles(1, 40f, AceRadialMath.SubLevelSpanDeg, out _);
            Near(single[0], 40f, "a lone child continues its parent's direction");
        }

        private static void FanBecomesRingAtThreshold()
        {
            AceRadialMath.FanAngles(6, 0f, AceRadialMath.RootSpanDeg, out float six);
            Near(six, 55f, "six root options still fan (275 degrees)");
            float[] seven = AceRadialMath.FanAngles(7, 0f, AceRadialMath.RootSpanDeg, out float ring);
            Near(ring, 360f / 7f, "seven root options close into a ring");
            Near(seven[0], 0f, "a ring starts at the parent direction");
        }

        private static void SubLevelSqueezesIntoSpan()
        {
            float[] angles = AceRadialMath.FanAngles(4, 90f, AceRadialMath.SubLevelSpanDeg, out float interval);
            Near(interval, 50f, "four sub-options share the 150 degree cap");
            Near(angles[0], 15f, "sub-level fan is centred on the parent angle");
            Near(angles[3], 165f, "sub-level fan spans exactly 150 degrees");
        }

        private static void RadiusWidensAsStepNarrows()
        {
            Near(AceRadialMath.RadiusFactor(55f), 0.797f, "ACE radius factor at a 55 degree step", 0.002f);
            TestAssert.That(AceRadialMath.RadiusFactor(20f) > AceRadialMath.RadiusFactor(55f),
                "tighter steps push options further out");
            Near(AceRadialMath.RadiusFactor(5f), 1.1f, "radius factor is capped at 1.1");
            Near(AceRadialMath.RadiusFactor(180f), 0.5f, "radius factor floors at 0.5");
        }

        private static void ExpandScale()
        {
            Near(AceRadialMath.ExpandScale(0f), 0.3f, "new level starts at 30%");
            Near(AceRadialMath.ExpandScale(2f), 1f, "new level settles at full size");
        }

        private static void ClosestPointWithinThreshold()
        {
            var points = new List<AceVec2> { new AceVec2(100f, 100f), new AceVec2(200f, 100f) };
            TestAssert.That(AceRadialMath.FindClosest(new AceVec2(190f, 104f), points, 50f) == 1,
                "nearest point wins");
            TestAssert.That(AceRadialMath.FindClosest(new AceVec2(900f, 900f), points, 50f) == -1,
                "nothing is picked beyond the threshold");
        }

        private static void CollectDropsHiddenAndPrunesEmptyBranches()
        {
            var root = new AceRadialAction("root", "")
                .Add(new AceRadialAction("hidden", "HIDDEN", () => { }, visible: () => false))
                .Add(new AceRadialAction("empty", "EMPTY BRANCH")
                    .Add(new AceRadialAction("gone", "GONE", () => { }, visible: () => false)))
                .Add(new AceRadialAction("broken", "THROWS", () => { }, visible: () => throw new InvalidOperationException()))
                .Add(new AceRadialAction("ok", "OK", () => { }, enabled: () => false));
            AceRadialNode node = AceRadialNode.Collect(root, 32, 4);
            TestAssert.That(node.Children.Count == 1, "hidden, throwing and emptied branches are dropped");
            TestAssert.That(node.Children[0].Path == "root/ok", "paths join ids with slashes");
            TestAssert.That(!node.Children[0].Enabled, "a disabled option stays visible but not enabled");
        }

        private static void CollectHonoursNodeBudget()
        {
            var root = new AceRadialAction("root", "");
            for (int i = 0; i < 50; i++) root.Add(new AceRadialAction("a" + i, "A", () => { }));
            TestAssert.That(AceRadialNode.Collect(root, 32, 4).Children.Count == 32, "collection stops at the node cap");
        }

        private static void RootOptionsFanRightOfCentre()
        {
            var tree = OpenSample(out _);
            IReadOnlyList<AceRadialNodeLayout> layout = tree.Layout;
            TestAssert.That(layout.Count == 3, "centre plus two root options; nothing expanded yet");
            TestAssert.That(layout[0].Level == 0 && Equal(layout[0].Position, Center), "centre node sits at the menu centre");
            for (int i = 1; i < layout.Count; i++)
            {
                TestAssert.That(layout[i].Position.X > Center.X, "root options open to the right like ACE self-interaction");
                TestAssert.That(layout[i].CurrentLevel, "root options are the live choice");
            }
        }

        private static void DwellExpandsBranchAndKeepsBreadcrumb()
        {
            var tree = OpenSample(out _);
            AceVec2 autopilot = Find(tree, "root/autopilot").Position;
            tree.Tick(autopilot, Center, 1f, 0.2f);
            TestAssert.That(tree.Layout.Count == 3, "no expansion before the dwell");
            tree.Tick(autopilot, Center, 1f, 0.2f + AceRadialMenuTree.DwellSec + 0.01f);
            tree.Tick(autopilot, Center, 1f, 0.45f);
            TestAssert.That(tree.Layout.Count == 5, "dwelling on a branch fans its children");
            TestAssert.That(Find(tree, "root/autopilot").OnPath, "expanded branch is on the breadcrumb");
            TestAssert.That(!Find(tree, "root/presets").CurrentLevel && !Find(tree, "root/presets").OnPath,
                "the sibling branch stays visible but dimmed");
            TestAssert.That(Find(tree, "root/autopilot/land").CurrentLevel, "new children are the live choice");

            tree.Tick(Center, Center, 1f, 1f);
            tree.Tick(Center, Center, 1f, 1.5f);
            tree.Tick(Center, Center, 1f, 1.6f);
            TestAssert.That(tree.Layout.Count == 3, "resting on the centre collapses back to the root");
        }

        private static void LeafDwellCollapsesDeeperLevels()
        {
            var tree = OpenSample(out _);
            Expand(tree, "root/autopilot", 0.2f);
            AceVec2 land = Find(tree, "root/autopilot/land").Position;
            tree.Tick(land, Center, 1f, 1f);
            tree.Tick(land, Center, 1f, 1.5f);
            TestAssert.That(tree.Layout.Count == 5, "resting on a leaf keeps its own level open");
            TestAssert.That(Find(tree, "root/autopilot/hold").CurrentLevel, "leaf siblings stay live, not dimmed");
        }

        private static void ReleaseRunsOnlyEnabledLeaf()
        {
            var tree = OpenSample(out List<string> ran);
            TestAssert.That(tree.HoveredRunnable() == null, "nothing runs from the centre");
            Expand(tree, "root/autopilot", 0.2f);
            tree.Tick(Find(tree, "root/autopilot/hold").Position, Center, 1f, 1f);
            TestAssert.That(tree.HoveredRunnable() == null, "a disabled option never runs");
            tree.Tick(Find(tree, "root/autopilot/land").Position, Center, 1f, 1.1f);
            tree.HoveredRunnable()?.Run();
            TestAssert.That(ran.Count == 1 && ran[0] == "land", "releasing over an enabled leaf runs it");
            tree.Tick(Find(tree, "root/autopilot").Position, Center, 1f, 1.2f);
            TestAssert.That(tree.HoveredRunnable() == null, "a branch never runs");
        }

        private static void ConditionsAreRecheckedBeforeRunning()
        {
            bool allowed = true;
            var root = new AceRadialAction("root", "").Add(new AceRadialAction("go", "GO", () => { }, enabled: () => allowed));
            var tree = new AceRadialMenuTree();
            tree.Open(root, 0f);
            tree.Tick(Center, Center, 1f, AceRadialMenuTree.ExpandSec);
            tree.Tick(Find(tree, "root/go").Position, Center, 1f, 0.2f);
            allowed = false;
            TestAssert.That(tree.HoveredRunnable() == null, "an option that became unavailable does not run");
        }

        private static void ClickExpandsBranchImmediately()
        {
            var tree = OpenSample(out _);
            tree.Tick(Find(tree, "root/presets").Position, Center, 1f, 0.15f);
            TestAssert.That(tree.ExpandHovered(0.15f), "clicking a branch expands it at once");
            tree.Tick(Find(tree, "root/presets").Position, Center, 1f, 0.16f);
            TestAssert.That(tree.Layout.Count == 4, "clicked branch shows its child");
        }

        private static void StatusAndIconTravelWithTheNode()
        {
            var root = new AceRadialAction("root", "")
                .Add(new AceRadialAction("gear", "GEAR", () => { }).WithIcon(AceIcon.Gear)
                    .WithStatus(() => new AceRadialStatus("DOWN", AceTone.Active)))
                .Add(new AceRadialAction("broken", "BROKEN", () => { })
                    .WithStatus(() => throw new InvalidOperationException()))
                .Add(new AceRadialAction("plain", "PLAIN", () => { }));
            AceRadialNode node = AceRadialNode.Collect(root, AceRadialMenuTree.MaxNodes, AceRadialMenuTree.MaxDepth);
            TestAssert.That(node.Children[0].Action.Icon == AceIcon.Gear, "the icon travels with the action");
            TestAssert.That(node.Children[0].Status.Text == "DOWN" && node.Children[0].Status.Tone == AceTone.Active,
                "the state line is read at collection with its tone");
            TestAssert.That(node.Children.Count == 3 && node.Children[1].Status.IsEmpty,
                "a throwing state line blanks instead of dropping the option");
            TestAssert.That(node.Children[2].Status.IsEmpty && node.Children[2].Action.Icon == AceIcon.None,
                "options without a state line or icon collect empty ones");
        }

        private static void StatusIsReadAgainOnRecollect()
        {
            int ammo = 24;
            var root = new AceRadialAction("root", "")
                .Add(new AceRadialAction("flares", "FLARES", () => { }).WithStatus(() => "x" + ammo));
            var tree = new AceRadialMenuTree();
            tree.Open(root, 0f);
            tree.Tick(Center, Center, 1f, 0.1f);
            TestAssert.That(Find(tree, "root/flares").Node.Status.Text == "x24", "state line on open");
            ammo = 12;
            tree.Tick(Center, Center, 1f, 0.5f);
            TestAssert.That(Find(tree, "root/flares").Node.Status.Text == "x24", "state line holds between collections");
            tree.Tick(Center, Center, 1f, AceRadialMenuTree.RecollectSec + 0.1f);
            TestAssert.That(Find(tree, "root/flares").Node.Status.Text == "x12", "state line refreshes on recollect");
        }

        private static void LayoutStopsAtVisibleCap()
        {
            var root = new AceRadialAction("root", "");
            var wide = new AceRadialAction("wide", "WIDE");
            for (int i = 0; i < 60; i++) wide.Add(new AceRadialAction("w" + i, "W", () => { }));
            root.Add(wide);
            var tree = new AceRadialMenuTree();
            tree.Open(root, 0f);
            tree.Tick(Center, Center, 1f, 0.2f);
            Expand(tree, "root/wide", 0.3f);
            TestAssert.That(tree.Layout.Count <= AceRadialMenuTree.MaxVisible + 1,
                "layout never exceeds the drawn-widget cap (" + tree.Layout.Count + ")");
            TestAssert.That(AceRadialMenuTree.MaxNodes >= 128, "the collected tree fits the full catalog");
        }

        private static void ToggleStatusReadsOnOff()
        {
            TestAssert.That(AceRadialStatus.OnOff(true).Text == "ON" && AceRadialStatus.OnOff(true).Tone == AceTone.Active,
                "ON reads as engaged");
            TestAssert.That(AceRadialStatus.OnOff(false).Text == "OFF" && AceRadialStatus.OnOff(false).Tone == AceTone.Normal,
                "OFF reads plain");
            AceRadialStatus implicitText = "UP";
            TestAssert.That(implicitText.Text == "UP" && implicitText.Tone == AceTone.Normal, "plain text converts to a normal state line");
        }

        private static AceRadialMenuTree OpenSample(out List<string> ran)
        {
            var log = new List<string>();
            ran = log;
            var root = new AceRadialAction("root", "")
                .Add(new AceRadialAction("autopilot", "AUTOPILOT")
                    .Add(new AceRadialAction("land", "LAND", () => log.Add("land")))
                    .Add(new AceRadialAction("hold", "HOLD", () => log.Add("hold"), enabled: () => false)))
                .WithChildren(() => new[]
                {
                    new AceRadialAction("presets", "PRESETS").Add(new AceRadialAction("p1", "SLOT 1", () => log.Add("p1")))
                });
            var tree = new AceRadialMenuTree();
            tree.Open(root, 0f);
            tree.Tick(Center, Center, 1f, AceRadialMenuTree.ExpandSec); // root pop-out finished
            return tree;
        }

        private static void Expand(AceRadialMenuTree tree, string path, float start)
        {
            AceVec2 at = Find(tree, path).Position;
            tree.Tick(at, Center, 1f, start);
            tree.Tick(at, Center, 1f, start + AceRadialMenuTree.DwellSec + 0.01f);
            tree.Tick(at, Center, 1f, start + 0.5f);
        }

        private static AceRadialNodeLayout Find(AceRadialMenuTree tree, string path)
        {
            foreach (AceRadialNodeLayout node in tree.Layout)
                if (node.Node.Path == path) return node;
            throw new InvalidOperationException("option " + path + " is not laid out");
        }

        private static bool Equal(AceVec2 a, AceVec2 b) => AceVec2.Distance(a, b) < 0.01f;

        private static void Near(float actual, float expected, string message, float tolerance = 0.01f) =>
            TestAssert.That(Math.Abs(actual - expected) < tolerance, message + " (got " + actual + ")");
    }
}
