using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>
    /// Protocol 41: the front's POWER shaping the perks (counts, damage, jam depth, cooldown, room bonuses) and the pure rules of the four new perks: STARFALL's line, SIPHON's economics, DIRECT ACTION's odds and
    /// timing, OVERWATCH's numbers, and the words of their refusals.
    /// </summary>
    internal static class PerkPowerTests
    {
        public static void Run()
        {
            Counts();
            Cooldowns();
            Jams();
            RoomBonuses();
            Overwatch();
            Starfall();
            RodScales();
            SiphonMath();
            DirectActionRules();
            Words();
        }

        private static void Counts()
        {
            TestAssert.Eq(PerkPower.Count(4, 1f), 4, "power 1 keeps the table count");
            TestAssert.Eq(PerkPower.Count(4, 0.6f), 2, "4 x 0.6 = 2.4 rounds to 2");
            TestAssert.Eq(PerkPower.Count(4, 1.25f), 5, "4 x 1.25 = 5");
            TestAssert.Eq(PerkPower.Count(4, 1.5f), 6, "4 x 1.5 = 6");
            TestAssert.Eq(PerkPower.Count(1, 0.6f), 1, "a count never falls below one");
            TestAssert.Eq(PerkPower.Count(8, 1.5f, 8), 8, "a count honours its cap (the CRUISE salvo is 1..8)");
            TestAssert.Eq(PerkPower.Count(36, 0.6f), 22, "36 flares x 0.6 = 21.6");
            TestAssert.Eq(PerkPower.Count(4, float.NaN), 4, "an unreadable power is neutral");
            Near(PerkPower.Damage(0.3f), 0.6f, "damage follows the power, floor 0.6");
            Near(PerkPower.Damage(3f), 1.5f, "and ceiling 1.5");
            Near(PerkPower.Damage(1.25f), 1.25f, "inside the range it is the power");
        }

        private static void Cooldowns()
        {
            Near(PerkPower.Cooldown(120f, 1f), 120f, "neutral power, table cooldown");
            Near(PerkPower.Cooldown(120f, 1.5f), 80f, "ahead shortens: 120 / 1.5");
            Near(PerkPower.Cooldown(120f, 1.25f), 96f, "120 / 1.25");
            Near(PerkPower.Cooldown(120f, 0.6f), 120f, "behind never lengthens a cooldown");
            Near(PerkPower.Cooldown(300f, 1.5f), 200f, "R5 at full power: 200 s");
            Near(PerkPower.Cooldown(45f, float.NaN), 45f, "an unreadable power is neutral");
        }

        private static void Jams()
        {
            Near(PerkPower.JamFactor(0.4f, 1f), 0.4f, "neutral: RADAR BLIND keeps x0.4");
            Near(PerkPower.JamFactor(0.4f, 1.5f), 0.1f, "full power deepens x0.4 to x0.1");
            Near(PerkPower.JamFactor(0.4f, 0.6f), 0.64f, "behind only reaches x0.64");
            Near(PerkPower.JamFactor(1f, 1.5f), 1f, "a factor of 1 is no jam and passes");
            Near(PerkPower.JamFactor(1.5f, 1.5f), 1.5f, "a slowdown factor above 1 passes through untouched");
            // The package builder uses it: a fired package at power 1 is the table effect, at 1.5 stronger, longer and wider.
            CyberPackages.TryOfAction(SupportActionId.CyberJamRadar, out PackageDef jam);
            CyberEffect plain = CyberPackages.Package(jam, 1, 0f, 0f, 1, false, 100f);
            CyberEffect strong = CyberPackages.Package(jam, 1, 0f, 0f, 1, false, 100f, 1.5f);
            Near(plain.Factor, 0.4f, "the plain package keeps the table factor");
            Near(strong.Factor, 0.1f, "a powerful package jams harder");
            TestAssert.That(strong.Radius > plain.Radius && strong.Until > plain.Until, "and wider and longer");
            CyberEffect longer = CyberPackages.Package(jam, 1, 0f, 0f, 1, false, 100f, 1f, 1.6f);
            Near(longer.Until - 100f, (plain.Until - 100f) * 1.6f, "the duration bonus stretches the length alone");
            Near(longer.Radius, plain.Radius, "and not the radius");
        }

        private static void RoomBonuses()
        {
            Near(PerkPower.SatCameraRadius(1000f, 1), 1000f, "the camera's own bird adds nothing");
            Near(PerkPower.SatCameraRadius(1000f, 2), 1150f, "one more bird covering: +15 %");
            Near(PerkPower.SatCameraRadius(1000f, 3), 1300f, "all three: +30 %");
            Near(PerkPower.SatCameraRadius(1000f, 0), 1000f, "no negative bonus");
            Near(PerkPower.SamNetDuration(60f, 0), 60f, "no node held, no bonus");
            Near(PerkPower.SamNetDuration(60f, 2), 78f, "two nodes: x1.3");
            Near(PerkPower.SamNetDuration(60f, 4), 96f, "four nodes reach the x1.6 cap");
            Near(PerkPower.SamNetDuration(60f, 12), 96f, "and stay there");
            Near(PerkPower.SabotageOdds(1f, 0), 0.7f, "SABOTAGE STRIKE: 70 % at neutral power and no camp");
            Near(PerkPower.SabotageOdds(1f, 2), 0.8f, "+5 % per live own camp");
            Near(PerkPower.SabotageOdds(1f, 9), 0.85f, "the camp bonus caps at +15 %");
            Near(PerkPower.SabotageOdds(1.5f, 3), 0.95f, "odds clamp at 95 %");
            Near(PerkPower.SabotageOdds(0.6f, 0), 0.42f, "a front that is behind rolls worse");
            Near(PerkPower.ClampOdds(0.01f), 0.2f, "SOF odds never fall under 20 %");
            Near(PerkPower.ClampOdds(2f), 0.95f, "or rise over 95 %");
            Near(PerkPower.ClampOdds(float.NaN), 0.2f, "an unreadable roll is the floor");
        }

        private static void Overwatch()
        {
            Near(PerkPower.OverwatchRadius(1f), 3000f, "3 km at power 1");
            Near(PerkPower.OverwatchRadius(1.25f), 3750f, "3 km x power");
            TestAssert.Eq(PerkPower.OverwatchUnits(1f), 4, "four units at power 1");
            TestAssert.Eq(PerkPower.OverwatchUnits(1.5f), 6, "round(4 x 1.5) = 6");
            TestAssert.Eq(PerkPower.OverwatchUnits(0.6f), 2, "round(4 x 0.6) = 2");
            Near(PerkPower.OverwatchSeconds(1f), 60f, "60 s at power 1");
            Near(PerkPower.OverwatchSeconds(1.5f), 90f, "60 s x power");
        }

        private static void Starfall()
        {
            TestAssert.Eq(StarfallPlan.Count(0.6f), 3, "three rods when the front is behind");
            TestAssert.Eq(StarfallPlan.Count(1.05f), 4, "four at the middle of the range");
            TestAssert.Eq(StarfallPlan.Count(1f), 4, "three plus two x 0.44 rounds to four at neutral power");
            TestAssert.Eq(StarfallPlan.Count(1.5f), 5, "five at full power");
            TestAssert.Eq(StarfallPlan.Count(0.8f), 3, "and three just above the floor");
            TestAssert.Eq(StarfallPlan.Count(9f), 5, "never more than five");
            TestAssert.Eq(StarfallPlan.Count(float.NaN), 4, "an unreadable power is neutral");

            var pts = new SpaceImpact[5];
            // The aircraft is south of the aim: the line runs north, the aim in the middle, the first rod nearest the aircraft.
            StarfallPlan.Points(1000f, 2000f, 1000f, -3000f, true, 5, pts);
            for (int i = 0; i < 5; i++) Near(pts[i].X, 1000f, "a north-running line keeps its x");
            Near(pts[0].Z, 2000f - 600f, "first rod 600 m short of the aim");
            Near(pts[2].Z, 2000f, "the middle rod lands on the aim");
            Near(pts[4].Z, 2000f + 600f, "last rod 600 m beyond");
            for (int i = 1; i < 5; i++) Near(pts[i].Z - pts[i - 1].Z, 300f, "300 m apart");

            // An even count has no rod on the aim, the aim lies midway between the two middle ones.
            StarfallPlan.Points(0f, 0f, -5000f, 0f, true, 4, pts);
            Near(pts[0].X, -450f, "four rods from the west: -450"); Near(pts[1].X, -150f, "-150"); Near(pts[2].X, 150f, "+150"); Near(pts[3].X, 450f, "+450");
            for (int i = 0; i < 4; i++) Near(pts[i].Z, 0f, "an east-running line keeps its z");

            // A diagonal approach: the spacing stays 300 m along the real line.
            StarfallPlan.Points(0f, 0f, -3000f, -4000f, true, 3, pts);
            Near(pts[1].X, 0f, "the middle rod of three is the aim"); Near(pts[1].Z, 0f, "(z)");
            Near(pts[0].X, -180f, "the first rod sits 300 m back along (0.6, 0.8)"); Near(pts[0].Z, -240f, "(z)");
            float dx = pts[2].X - pts[1].X, dz = pts[2].Z - pts[1].Z;
            Near((float)Math.Sqrt(dx * dx + dz * dz), 300f, "300 m on the diagonal");

            // No aircraft, or one right on top of the aim: north.
            StarfallPlan.Points(0f, 0f, 0f, 0f, false, 3, pts);
            Near(pts[0].X, 0f, "no aircraft: the line runs north"); Near(pts[0].Z, -300f, "(first)"); Near(pts[2].Z, 300f, "(last)");
            StarfallPlan.Points(10f, 10f, 10.2f, 10.2f, true, 3, pts);
            Near(pts[0].X, 10f, "an aircraft on the aim has no direction: north"); Near(pts[0].Z, -290f, "(z)");
            StarfallPlan.Points(0f, 0f, 0f, -100f, true, 2, pts);
            Near(StarfallPlan.ReleaseAt(0), 0f, "the first rod leaves at once"); Near(StarfallPlan.ReleaseAt(3), 1.2f, "the fourth 1.2 s later (0.4 s apart)");
            Near(StarfallPlan.BlastScale, 0.35f, "each rod is about 35 % of an ORBITAL ROD");
        }

        private static void RodScales()
        {
            string plain = "BoscaliSummer:Support:Rod:7:12:0";
            SupportEffectPolicy.RodScales(plain, out float d0, out float r0);
            Near(d0, 1f, "a name without scales is a plain rod"); Near(r0, 1f, "(radius)");
            string powered = SupportEffectPolicy.RodName(plain, 1.25f, 1f);
            SupportEffectPolicy.RodScales(powered, out float d1, out float r1);
            Near(d1, 1.25f, "the front's power rides in the name as damage"); Near(r1, 1f, "and the radius stays whole");
            string small = SupportEffectPolicy.RodName("BoscaliSummer:Support:Rod:7:12:f2", StarfallPlan.BlastScale, StarfallPlan.BlastScale);
            SupportEffectPolicy.RodScales(small, out float d2, out float r2);
            Near(d2, 0.35f, "a STARFALL rod reads 35 % damage"); Near(r2, 0.35f, "and 35 % reach");
            SupportEffectPolicy.RodScales(plain + ":k=500,10", out float d3, out float r3);
            Near(d3, 1f, "an out-of-range scale is ignored"); Near(r3, 1f, "(radius)");
            SupportEffectPolicy.RodScales(plain + ":k=oops", out float d4, out float r4);
            Near(d4, 1f, "and so is a garbled one"); Near(r4, 1f, "(radius)");
            SupportEffectPolicy.RodScales(null, out float d5, out _);
            Near(d5, 1f, "a missile with no name is a plain rod");

            Near(SupportEffectPolicy.RodDamage(0f), 12000f, "the core still hits for 12000");
            Near(SupportEffectPolicy.RodDamage(0f, 1.5f, 1f), 18000f, "x1.5 at full power");
            Near(SupportEffectPolicy.RodDamage(0f, 0.6f, 1f), 7200f, "x0.6 behind");
            Near(SupportEffectPolicy.RodDamage(300f), SupportEffectPolicy.RodDamage(300f, 1f, 1f), "the unscaled overload is the scaled one at 1, 1");
            Near(SupportEffectPolicy.RodDamage(50f, 1f, 0.35f), 12000f, "a small rod's core is 35 % of the radius");
            Near(SupportEffectPolicy.RodDamage(147f, 1f, 0.35f), 0f, "and its blast ends at 147 m");
            TestAssert.That(SupportEffectPolicy.RodDamage(100f, 1f, 0.35f) > 0f && SupportEffectPolicy.RodDamage(100f, 1f, 0.35f) < 12000f, "with a falloff between");
            Near(SupportEffectPolicy.RodDamage(430f, 1.5f, 1f), 0f, "damage power never widens the blast past 420 m");
        }

        private static void SiphonMath()
        {
            Near(Siphon.Wanted(0, 1f), 40f, "40 funds with no node held");
            Near(Siphon.Wanted(2, 1f), 70f, "+15 per node held: 40 + 30");
            Near(Siphon.Wanted(2, 1.5f), 105f, "x power");
            Near(Siphon.Wanted(4, 0.6f), 60f, "(40 + 60) x 0.6");
            Near(Siphon.Wanted(-3, 1f), 40f, "no negative nodes");

            var takes = new float[3];
            float total = Siphon.Plan(100f, new[] { 30f, 200f, 50f }, takes);
            Near(total, 100f, "a rich budget pays it all"); Near(takes[1], 100f, "from the richest front"); Near(takes[0] + takes[2], 0f, "and only from it");

            total = Siphon.Plan(250f, new[] { 30f, 200f, 50f }, takes);
            Near(total, 250f, "two budgets pay when one cannot"); Near(takes[1], 200f, "the richest is emptied"); Near(takes[2], 50f, "then the next richest"); Near(takes[0], 0f, "the poorest is left alone");

            total = Siphon.Plan(500f, new[] { 40f, 40f, 40f }, takes);
            Near(total, 120f, "the ask is capped by what the enemy has");
            Near(takes[0] + takes[1] + takes[2], 120f, "and every budget gave");

            total = Siphon.Plan(100f, new[] { 5f, 4f, 0f }, takes);
            Near(total, 0f, "under ten funds there is nothing to siphon"); Near(takes[0] + takes[1] + takes[2], 0f, "and nothing is taken");

            total = Siphon.Plan(100f, new[] { 10.7f, 0f, 0f }, takes);
            Near(total, 10f, "whole funds only: a fraction stays behind"); Near(takes[0], 10f, "taken from the one budget");

            // An AI faction pours its budget into the queue head every tick: SIPHON reaches into the funding bars of programmes not yet building.
            var side = new FrontSide { Budget = 15f };
            side.Queue.Add(new FrontProgrammeEntry { Id = ProgrammeId.Readiness, Cost = 150f, Funded = 120f });
            side.Queue.Add(new FrontProgrammeEntry { Id = ProgrammeId.Readiness, Cost = 300f, Funded = 300f, BuildEnd = 500f });
            Near(Siphon.Available(side), 135f, "budget plus the unstarted bar; a building programme is out of reach");
            Near(Siphon.Drain(side, 60f), 60f, "the take is paid");
            Near(side.Budget, 0f, "the budget goes first"); Near(side.Queue[0].Funded, 75f, "then the head's bar slips"); Near(side.Queue[1].Funded, 300f, "the building one keeps its funds");
            Near(Siphon.Drain(side, 500f), 75f, "never more than there is");

            total = Siphon.Plan(100f, new[] { float.NaN, 80f, -5f }, takes);
            Near(total, 80f, "unreadable and negative budgets count for nothing"); Near(takes[1], 80f, "(taken)");

            total = Siphon.Plan(0f, new[] { 80f, 80f, 80f }, takes);
            Near(total, 0f, "asking for nothing takes nothing");
        }

        private static void DirectActionRules()
        {
            Near(DirectAction.Seconds(1f), 45f, "45 s at power 1");
            Near(DirectAction.Seconds(1.5f), 30f, "45 / 1.5");
            Near(DirectAction.Seconds(0.6f), 75f, "a front that is behind takes longer");
            TestAssert.Eq(DirectAction.OddsPercent(1f, 0), 55, "0.55 x power + 0.05 per team afield");
            TestAssert.Eq(DirectAction.OddsPercent(1f, 2), 65, "two teams afield add ten");
            TestAssert.Eq(DirectAction.OddsPercent(1.5f, 4), 95, "0.825 + 0.2 clamps at 95");
            TestAssert.Eq(DirectAction.OddsPercent(0.6f, 0), 33, "a weak front: 0.33");
            TestAssert.Eq(DirectAction.OddsPercent(float.NaN, 0), 55, "an unreadable power is neutral");

            var seen = new List<SofTarget>
            {
                new SofTarget(1, TargetKind.Ground, AnchorSub.Uplink, 101, 1500f, 0f, 0f),   // a tank, not high value
                new SofTarget(2, TargetKind.Ground, AnchorSub.Uplink, 102, 900f, 0f, 0f),    // a SAM, 900 m from the aim
                new SofTarget(3, TargetKind.Anchor, AnchorSub.EwTruck, 103, 0f, 1200f, 0f),  // an anchor, 1.2 km
                new SofTarget(4, TargetKind.Building, AnchorSub.Uplink, 104, 100f, 0f, 0f),  // a building is never a raid target
                new SofTarget(5, TargetKind.Ground, AnchorSub.Uplink, 105, 2500f, 0f, 0f),   // a SAM, out of reach
            };
            Func<SofTarget, bool> highValue = t => t.Key == 102 || t.Key == 105 || t.Kind == TargetKind.Anchor;
            TestAssert.That(DirectAction.TryNearest(seen, highValue, 0f, 0f, out SofTarget best) && best.Id == 2, "the nearest high-value target wins");
            TestAssert.That(DirectAction.TryNearest(seen, highValue, 0f, 1000f, out best) && best.Id == 3, "from further north the anchor is nearer");
            TestAssert.That(DirectAction.TryNearest(seen, highValue, 6000f, 0f, out _) == false, "nothing within 2 km of a far aim");
            TestAssert.That(DirectAction.TryNearest(seen, highValue, 2500f, 0f, out best) && best.Id == 5, "the reach is 2 km from the aim");
            TestAssert.That(DirectAction.TryNearest(seen, t => false, 0f, 0f, out _) == false, "a plain unit is not a raid target");
            TestAssert.That(DirectAction.TryNearest(null, highValue, 0f, 0f, out _) == false, "nothing revealed, nothing to raid");
            TestAssert.That(DirectAction.TryNearest(seen, null, 0f, 0f, out best) && best.Id == 2, "without a predicate every ground unit or anchor counts, never a building");
        }

        private static void Words()
        {
            TestAssert.Eq(SupportWords.Refusal(SupportResult.NoMovers), "NEGATIVE: NO MOVING CONTACTS NEAR THE AIM", "OVERWATCH refusal");
            TestAssert.Eq(SupportWords.Refusal(SupportResult.NothingToSiphon), "NEGATIVE: NOTHING TO SIPHON", "SIPHON refusal");
            TestAssert.Eq(SupportWords.Refusal(SupportResult.NoReadyTeam), "NEGATIVE: NO READY TEAM", "DIRECT ACTION team refusal");
            TestAssert.Eq(SupportWords.Refusal(SupportResult.NoHighValue), "NEGATIVE: NO HIGH-VALUE TARGET NEAR THE AIM", "DIRECT ACTION target refusal");
            TestAssert.That((byte)SupportResult.NoMovers == 137 && (byte)SupportResult.NothingToSiphon == 138 && (byte)SupportResult.NoReadyTeam == 139 && (byte)SupportResult.NoHighValue == 140,
                "the new result bytes follow the highest earlier one");
            var seen = new HashSet<byte>();
            foreach (SupportResult r in Enum.GetValues(typeof(SupportResult))) TestAssert.That(seen.Add((byte)r), "a result byte is never reused: " + r);
            var ids = new HashSet<byte>();
            foreach (SupportActionId id in Enum.GetValues(typeof(SupportActionId))) TestAssert.That(ids.Add((byte)id), "an action id is never reused: " + id);
            // The siphon lines read on both sides.
            var gain = new FrontLogRow { Code = (byte)FrontLogCode.Siphon, A = 105 };
            var loss = new FrontLogRow { Code = (byte)FrontLogCode.Siphoned, A = 105 };
            TestAssert.Eq(FrontWords.Line(Front.Cyber, gain), "HEXWARD · SIPHON · +105 FUNDS TAKEN FROM THE ENEMY BUDGETS", "the caster's CYBER line");
            TestAssert.Eq(FrontWords.Line(Front.Sof, loss), "SHADOW · SIPHONED · -105 FUNDS TAKEN BY THE ENEMY", "the victim's line on the drained front");
        }

        private static void Near(float actual, float expected, string message, float tolerance = 0.01f) =>
            TestAssert.That(Math.Abs(actual - expected) <= tolerance, message + " (got " + actual + ", want " + expected + ")");
    }
}
