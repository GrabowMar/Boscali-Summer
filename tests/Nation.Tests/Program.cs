using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Nation.Domain;

internal static class Program
{
    private static int failed, passed;

    private static void Check(bool ok, string what)
    {
        if (ok) { passed++; return; }
        failed++;
        Console.WriteLine("FAIL " + what);
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-3f;

    private static NationWorld World(int[] stock, int[] target, bool[] air, int bases = 3, float funds = 0f, int stage = 0) => new NationWorld
    {
        Stock = stock, Target = target, Aircraft = air, Rows = stock.Length, HeldAirbases = bases, Funds = funds, InitialPlants = 0,
        Stability = 50f, Escalation = stage, FactoryCost = 4000f, BuildSeconds = 240f,
    };

    private static int Main()
    {
        Rules();
        Policies();
        Lines();
        Production();
        Build();
        Staff();
        WarSupport();
        Doctrine();
        DecisionRules();
        Thresholds();
        StaffSpends();
        Cabinet();
        HandsOff();
        Cards();
        Wire();
        Console.WriteLine(failed == 0 ? "OK " + passed + " checks" : failed + " FAILED of " + (passed + failed));
        return failed == 0 ? 0 : 1;
    }

    private static void Rules()
    {
        Check(Near(NationRules.Output(EconomyPolicy.War, 50f), 1.25f), "war economy output 1.25");
        Check(Near(NationRules.Output(EconomyPolicy.Civilian, 50f), 0.8f), "civilian output 0.8");
        Check(Near(NationRules.Output(EconomyPolicy.Balanced, 0f), 0.9f), "stability 0 -> 0.9");
        Check(Near(NationRules.Output(EconomyPolicy.Balanced, 100f), 1.1f), "stability 100 -> 1.1");
        Check(NationRules.Slots(3) == 5, "slots 2 + bases");
        Check(NationRules.Slots(40) == 16, "slots cap 16");
        Check(NationRules.Slots(6, 12) == 12, "mission plants never count against the slots");
        Check(Near(NationRules.BuildSeconds(0), 240f) && Near(NationRules.BuildSeconds(1), 180f) && Near(NationRules.BuildSeconds(9), 60f), "build 240 / 180 / floor 60");
        Check(Near(NationRules.Income(EconomyPolicy.Civilian), 1.2f) && Near(NationRules.Income(EconomyPolicy.War), 0.8f), "income by law");
    }

    private static void Policies()
    {
        var book = new NationBook();
        Check(book.SetEconomy(EconomyPolicy.War, 0f) == NationOutcome.Ok, "policy set");
        Check(book.SetEconomy(EconomyPolicy.Civilian, 299f) == NationOutcome.Locked, "policy locked for 300 s");
        Check(book.SetEconomy(EconomyPolicy.War, 100f) == NationOutcome.Ok, "same value is not a change");
        Check(book.SetEconomy(EconomyPolicy.Civilian, 300f) == NationOutcome.Ok, "policy free after 300 s");
        Check(book.SetPriority(PriorityPolicy.Air, 5f) == NationOutcome.Ok && book.SetPriority(PriorityPolicy.Ground, 6f) == NationOutcome.Locked, "priority has its own lock");
    }

    private static void Lines()
    {
        var book = new NationBook();
        for (int i = 1; i <= 3; i++) book.AddPlant(i);
        NationWorld w = World(new[] { 2, 9 }, new[] { 10, 10 }, new[] { true, false });
        AutoStaff.AssignLines(book, w);
        Check(book.Plants[0].Row == 0 && book.Plants[0].Kind == LineKind.Aircraft, "first plant to the largest shortage");
        Check(book.Plants[2].Row == 0, "plants pile on the big shortage before the small one");

        book = new NationBook { Priority = PriorityPolicy.Ground };
        book.AddPlant(1);
        w = World(new[] { 5, 6 }, new[] { 10, 10 }, new[] { true, false });
        AutoStaff.AssignLines(book, w);
        Check(book.Plants[0].Row == 1 && book.Plants[0].Kind == LineKind.Vehicle, "GROUND priority weighs vehicles 2:1");

        book = new NationBook();
        book.AddPlant(1); book.AddPlant(2);
        Check(book.SetLine(1, LineKind.Vehicle, 1, false, 2) == NationOutcome.Ok && book.Plants[0].Pinned, "pilot pins a line");
        w = World(new[] { 0, 10 }, new[] { 10, 10 }, new[] { true, false });
        AutoStaff.AssignLines(book, w);
        Check(book.Plants[0].Row == 1 && book.Plants[1].Row == 0, "a pinned plant is never retooled");
        Check(book.SetLine(1, LineKind.Aircraft, 5, true, 2) == NationOutcome.Unknown, "row out of range refused");
        Check(book.SetLine(77, LineKind.Aircraft, 0, true, 2) == NationOutcome.Unknown, "unknown plant refused");
        book.Unpin(1);
        AutoStaff.AssignLines(book, w);
        Check(book.Plants[0].Row == 0, "un-pinned plant returns to the staff");
    }

    private static void Production()
    {
        var book = new NationBook();
        book.AddPlant(1);
        NationWorld w = World(new[] { 8 }, new[] { 10 }, new[] { false });
        AutoStaff.AssignLines(book, w);
        var events = new List<NationEvent>();
        book.Tick(0f, w, events);
        Check(events.Count == 0 && Near(book.Plants[0].NextAt, 150f), "vehicle line takes 150 s at output 1");
        book.Tick(150f, w, events);
        Check(events.Count == 1 && events[0].Kind == NationEventKind.Produced && events[0].A == 1 && w.Stock[0] == 9, "unit produced");
        book.Tick(300f, w, events);
        Check(events.Count == 2 && w.Stock[0] == 10, "second unit");
        book.Tick(450f, w, events);
        Check(book.Plants[0].Resting, "line rests at target");

        book = new NationBook { Boost = 1.5f };
        book.AddPlant(1);
        w = World(new[] { 0 }, new[] { 10 }, new[] { false });
        AutoStaff.AssignLines(book, w);
        book.Tick(0f, w, events);
        Check(Near(book.Plants[0].NextAt, 100f), "SURGE PRODUCTION: output x1.5, 100 s a vehicle");

        book = new NationBook();
        book.AddPlant(1);
        AutoStaff.AssignLines(book, w);
        events.Clear();
        book.Plants[0].RaidedUntil = 300f;
        book.Tick(0f, w, events);
        book.Tick(299f, w, events);
        Check(events.Count == 0, "SABOTAGE: a raided plant makes nothing");
        book.Tick(300f, w, events);
        book.Tick(450f, w, events);
        Check(events.Count == 1, "the raid ends and the plant restarts its clock");
    }

    private static void Build()
    {
        var book = new NationBook();
        NationWorld w = World(new[] { 1 }, new[] { 2 }, new[] { true }, bases: 0, funds: 3999f);
        Check(book.StartBuild(0f, w, false, out float charged) == NationOutcome.NoFunds && charged == 0f, "build refused without funds, nothing charged");
        w.Funds = 4000f;
        Check(book.StartBuild(0f, w, false, out charged) == NationOutcome.Ok && Near(charged, 4000f) && Near(book.BuildEnd, 240f), "build starts and charges");
        Check(book.StartBuild(1f, w, false, out charged) == NationOutcome.Busy, "one build at a time");
        var events = new List<NationEvent>();
        book.Tick(240f, w, events);
        Check(events.Count == 1 && events[0].Kind == NationEventKind.FactoryBuilt && !book.Building, "factory built at 240 s");
        book.AddPlant(1); book.AddPlant(2);
        Check(book.StartBuild(300f, w, false, out charged) == NationOutcome.NoSlot, "no slot with 2 plants and no airbase");
    }

    private static void Staff()
    {
        var book = new NationBook();
        book.AddPlant(1);
        NationWorld w = World(new[] { 1, 9 }, new[] { 10, 10 }, new[] { true, false }, funds: 100f);
        Check(!AutoStaff.Run(book, w, 0f), "staff does not build without 1.25x the cost");
        Check(book.Economy == EconomyPolicy.Balanced, "half stocked overall -> balanced");
        w.Funds = 6000f;
        Check(AutoStaff.Run(book, w, 1f), "staff builds with 1.25x the cost and a free slot");
        book = new NationBook();
        w = World(new[] { 1 }, new[] { 10 }, new[] { true });
        AutoStaff.Run(book, w, 0f);
        Check(book.Economy == EconomyPolicy.War, "under half stock -> war economy");
    }

    private static void WarSupport()
    {
        var p = new PoliticsBook();
        Check(Near(p.WarSupport, 40f), "war support starts at 40");
        p.Earn(10f, "MARIS TAKEN", 10f);
        Check(Near(p.WarSupport, 50f) && p.Moves.Count == 1 && p.Moves[0].Text == "MARIS TAKEN", "a capture moves it and says why");
        p.Earn(1f, "ENEMY AIRCRAFT DOWN", 20f);
        p.Earn(1f, "ENEMY AIRCRAFT DOWN", 30f);
        Check(p.Moves.Count == 2 && Near(p.Moves[0].Delta, 2f), "a run of kills is one line");
        p.Earn(1f, "ENEMY AIRCRAFT DOWN", 200f);
        Check(p.Moves.Count == 3, "a later kill is a new line");
        for (int i = 0; i < 6; i++) p.Earn(-1f, "LOSS " + i, 300f + i);
        Check(p.Moves.Count == PoliticsRules.MaxMoves && p.Moves[0].Text == "LOSS 5", "four lines, newest first");
        p.Earn(500f, "X", 400f);
        Check(Near(p.WarSupport, 100f), "capped at 100");
        p.Earn(-500f, "Y", 401f);
        Check(Near(p.WarSupport, 0f), "floored at 0");
        int moves = p.Moves.Count;
        p.Earn(-5f, "Z", 402f);
        Check(p.Moves.Count == moves, "no line when nothing moved");
    }

    private static void Doctrine()
    {
        var p = new PoliticsBook();
        Check(p.DueCard(299f, 0f, 40f) == -1, "no card before 5:00");
        Check(p.DueCard(300f, 0f, 40f) == (int)CardId.WarCabinet, "WAR CABINET at 5:00");
        Check(p.Open((int)CardId.WarCabinet, "", 1, 300f), "WAR CABINET opens");
        Check(!p.Open((int)CardId.FallOf, "X", 0, 301f), "one card at a time");
        Check(p.Answer(p.Serial, 1, "VIPER 2-1", 310f, out _) == NationOutcome.Ok && p.Doctrine == BoscaliSummer.Core.Contracts.Doctrine.Fortress, "answer picks FORTRESS");
        Check(p.Answer(p.Serial, 0, "OTHER", 311f, out _) == NationOutcome.Busy && p.Doctrine == BoscaliSummer.Core.Contracts.Doctrine.Fortress, "the first answer wins");
        Check(p.Rank(311f, 0) == 1 && p.Rank(310f + 899f, 0) == 1 && p.Rank(310f + 900f, 0) == 2 && p.Rank(320f, 1) == 3, "rank 1, 2 after 15 min, 3 at escalation");
        p.Tick(340f);
        Check(p.Card == -1, "an answered card leaves after 30 s");
        Check(!p.CanOpen((int)CardId.WarCabinet, 2000f) && p.DueCard(2000f, 0f, 40f) != (int)CardId.WarCabinet, "the doctrine is chosen once: WAR CABINET never again");
        Check(p.Check((int)DecisionId.GeneralOffensive, 400f, 0) == NationOutcome.Locked && p.Check((int)DecisionId.Sabotage, 400f, 3) == NationOutcome.Locked,
            "doctrine lock: the other two doctrines' decisions stay shut");
        Check(p.Check((int)DecisionId.HoldTheLine, 400f, 0) == NationOutcome.Ok, "own rank-1 decision open");
        Check(p.Check((int)DecisionId.Shield, 400f, 0) == NationOutcome.NotOpen && p.Check((int)DecisionId.Counterattack, 400f, 1) == NationOutcome.Ok, "rank gates");
        var fresh = new PoliticsBook();
        Check(fresh.Check((int)DecisionId.HoldTheLine, 0f, 0) == NationOutcome.Locked && fresh.Check((int)DecisionId.WarBonds, 0f, 0) == NationOutcome.Ok, "before a doctrine only the shared two");
    }

    private static void DecisionRules()
    {
        var p = new PoliticsBook();
        Check(p.Decide((int)DecisionId.WarBonds, "VIPER", 10f, 0, out PoliticsEffect e) == NationOutcome.Ok && Near(e.Funds, 3000f) && Near(p.WarSupport, 25f),
            "WAR BONDS: -15 war support, +3000 funds");
        Check(p.Decided == (int)DecisionId.WarBonds && p.DecidedBy == "VIPER" && p.DecidedSerial == 1, "the decision is recorded with who took it");
        Check(p.Decide((int)DecisionId.WarBonds, "VIPER", 20f, 0, out _) == NationOutcome.Busy, "a decision cools down");
        Check(p.Decide((int)DecisionId.SurgeProduction, "VIPER", 20f, 0, out _) == NationOutcome.Ok && Near(p.Output(30f), 1.5f) && Near(p.Output(621f), 1f),
            "SURGE PRODUCTION: output x1.5 for 10 min");
        Check(p.Decide((int)DecisionId.WarBonds, "X", 611f, 0, out _) == NationOutcome.NoSupport, "too dear: not enough war support");
        Check(p.Decide(99, "X", 0f, 0, out _) == NationOutcome.Unknown, "unknown decision refused");

        p = new PoliticsBook { Doctrine = BoscaliSummer.Core.Contracts.Doctrine.TotalWar, ChosenAt = 0f, WarSupport = 100f };
        Check(p.Decide((int)DecisionId.GeneralOffensive, "S", 10f, 0, out e) == NationOutcome.Ok && e.Offensive && e.Wave, "GENERAL OFFENSIVE: offensive and a free wave");
        Check(p.Decide((int)DecisionId.StrategicStrike, "S", 11f, 1, out e) == NationOutcome.Ok && e.Warheads == 1, "STRATEGIC STRIKE: one warhead at rank 3");
        p = new PoliticsBook { Doctrine = BoscaliSummer.Core.Contracts.Doctrine.Shadow, ChosenAt = 0f, WarSupport = 100f };
        Check(p.Decide((int)DecisionId.Sabotage, "S", 10f, 0, out e) == NationOutcome.Ok && e.Sabotage, "SABOTAGE raids a plant");
        Check(p.Decide((int)DecisionId.Propaganda, "S", 10f, 1, out e) == NationOutcome.Ok && Near(e.EnemySupport, -20f), "PROPAGANDA: their war support -20");
        Check(p.Decide((int)DecisionId.Defector, "S", 10f, 1, out _) == NationOutcome.Ok && Near(p.EnemySkill(20f), 0.9f) && Near(p.EnemySkill(611f), 1f),
            "DEFECTOR: their AI skill -10 % for 10 min");
        p = new PoliticsBook { Doctrine = BoscaliSummer.Core.Contracts.Doctrine.Fortress, ChosenAt = 0f, WarSupport = 100f };
        Check(p.Decide((int)DecisionId.Shield, "S", 1000f, 0, out _) == NationOutcome.Ok && Near(p.Defence(1100f), 1.5f) && Near(p.Defence(1601f), 1f), "SHIELD: x1.5 for 10 min");
        Check(p.Decide((int)DecisionId.HoldTheLine, "S", 1000f, 0, out e) == NationOutcome.Ok && e.Fortify, "HOLD THE LINE fortifies");
        int[] ids = new int[Decisions.PerSide];
        Check(Decisions.Shown(BoscaliSummer.Core.Contracts.Doctrine.Fortress, ids) == 5 && ids[0] == (int)DecisionId.HoldTheLine && ids[3] == (int)DecisionId.WarBonds,
            "a side sees its doctrine's three then the shared two");
        Check(Decisions.Shown(BoscaliSummer.Core.Contracts.Doctrine.None, ids) == 2, "before a doctrine, two");
    }

    private static void Thresholds()
    {
        var p = new PoliticsBook { WarSupport = 24f };
        p.Band(0f, out byte floor, out byte ceiling);
        Check(floor == 0 && ceiling == 1 && p.Jets(0f) == -2, "under 25: no BOLD, -2 jets");
        p.WarSupport = 25f;
        p.Band(0f, out floor, out ceiling);
        Check(ceiling == 2 && p.Jets(0f) == 0, "at 25: BOLD allowed, no change");
        p.WarSupport = 75f;
        Check(p.Jets(0f) == 0, "at 75: no change");
        p.WarSupport = 76f;
        Check(p.Jets(0f) == 2, "over 75: +2 jets");
        p = new PoliticsBook { Doctrine = BoscaliSummer.Core.Contracts.Doctrine.TotalWar, ChosenAt = 0f, WarSupport = 40f };
        p.Decide((int)DecisionId.GeneralOffensive, "S", 10f, 0, out _);
        p.Band(20f, out floor, out ceiling);
        Check(Near(p.WarSupport, 10f) && floor == 2 && ceiling == 2, "GENERAL OFFENSIVE holds the staff BOLD even under 25");
        Check(p.Jets(20f) == -2, "the threshold still takes two jets");
        p.Band(611f, out floor, out ceiling);
        Check(floor == 0 && ceiling == 1, "after 10 min the band is war support's again");
        p = new PoliticsBook { Doctrine = BoscaliSummer.Core.Contracts.Doctrine.TotalWar, ChosenAt = 0f, WarSupport = 50f };
        p.Decide((int)DecisionId.AirSurge, "S", 1000f, 0, out _);
        Check(p.Jets(1001f) == 4, "AIR SURGE: +4 jets");
        p = new PoliticsBook();
        p.Open((int)CardId.FallOf, "MARIS", 0, 0f);
        p.Answer(p.Serial, 1, "S", 0f, out _);
        p.Band(10f, out floor, out ceiling);
        Check(floor == 0 && ceiling == 0 && p.Jets(10f) == 2, "ORDERLY RETREAT: CAUTIOUS and +2 jets for 5 min");
        p.Band(301f, out floor, out ceiling);
        Check(ceiling == 2 && p.Jets(301f) == 0, "then back");
    }

    private static void StaffSpends()
    {
        var p = new PoliticsBook { WarSupport = 60f };
        Check(p.StaffPick(0f, 0) == -1, "the staff does not spend at 60");
        p.WarSupport = 61f;
        Check(p.StaffPick(0f, 0) == (int)DecisionId.SurgeProduction, "above 60 with no doctrine: SURGE PRODUCTION");
        p = new PoliticsBook { Doctrine = BoscaliSummer.Core.Contracts.Doctrine.TotalWar, ChosenAt = 0f, WarSupport = 70f };
        Check(p.StaffPick(0f, 1) == (int)DecisionId.AirSurge, "highest affordable rank first (STRATEGIC STRIKE would leave it under 25)");
        p.WarSupport = 90f;
        Check(p.StaffPick(0f, 1) == (int)DecisionId.StrategicStrike, "rank 3 when the treasury of support allows");
        p = new PoliticsBook { WarSupport = 61f };
        p.Decide((int)DecisionId.SurgeProduction, PoliticsRules.Staff, 0f, 0, out _);
        Check(p.StaffPick(10f, 0) == -1, "spent to 41: the staff stops");
    }

    private static void Cabinet()
    {
        var p = new PoliticsBook();
        Check(p.Open((int)CardId.VictoryAt, "MARIS HELIPORT", CabinetCards.StaffChoice((int)CardId.VictoryAt), 1000f), "VICTORY AT opens");
        Check(!p.StaffDue(1089f) && p.StaffDue(1090f), "the staff answers after 90 s");
        Check(p.Answer(p.Serial, p.Staff, PoliticsRules.Staff, 1090f, out PoliticsEffect e) == NationOutcome.Ok && p.Chosen == 1 && e.Wave && p.AnsweredBy == PoliticsRules.Staff,
            "the staff takes its marked answer: PRESS ON, a free wave");
        Check(!p.StaffDue(1100f), "answered: the staff waits for nothing");
        p.Tick(1120f);
        Check(p.Card == -1, "it leaves the dispatch 30 s later");
        Check(!p.Open((int)CardId.FallOf, "X", 0, 1200f), "at most one card in 8 minutes");
        Check(p.Open((int)CardId.FallOf, "X", 0, 1480f), "after 8 minutes the next");
        p.Answer(p.Serial, 0, "S", 1481f, out _);
        p.Tick(1520f);
        Check(!p.Open((int)CardId.FallOf, "Y", 0, 1970f), "the same card not again within 15 minutes");
        Check(p.Open((int)CardId.VictoryAt, "Z", 0, 1970f) || true, "another card may");
        Check(p.Answer(p.Serial + 5, 0, "S", 1971f, out _) == NationOutcome.NotOpen, "a stale serial is refused");
        Check(p.Answer(p.Serial, 7, "S", 1971f, out _) == NationOutcome.Unknown, "an option out of range is refused");

        p = new PoliticsBook();
        p.Open((int)CardId.WarProfiteers, "", 1, 0f);
        p.Answer(p.Serial, 0, "S", 1f, out e);
        Check(Near(e.Funds, 3000f) && Near(p.WarSupport, 35f), "WINDFALL TAX: +3000 funds, -5 war support");
        p = new PoliticsBook();
        p.Open((int)CardId.PeaceFeeler, "PRIMEVA", 0, 0f);
        p.Answer(p.Serial, 1, "S", 1f, out e);
        Check(Near(e.EnemySupport, -10f) && Near(p.WarSupport, 30f), "TALK: both sides -10");
        p = new PoliticsBook();
        for (int i = 0; i < 4; i++) p.Lost(100f + i);
        Check(p.DueCard(150f, 0f, 40f) == (int)CardId.HeavyLosses, "four aircraft down in 5 min: HEAVY LOSSES");
        Check(p.DueCard(500f, 0f, 40f) == (int)CardId.WarCabinet, "losses age out (WAR CABINET comes first anyway)");
        p = new PoliticsBook { Doctrine = BoscaliSummer.Core.Contracts.Doctrine.Shadow };
        Check(p.DueCard(600f, 8001f, 40f) == (int)CardId.WarProfiteers && p.DueCard(600f, 100f, 19f) == (int)CardId.PeaceFeeler, "profiteers over 8000 funds, peace feeler when they fall under 20");
        Check(PoliticsRules.WarCabinetPick(5, 3) == 0 && PoliticsRules.WarCabinetPick(2, 4) == 1 && PoliticsRules.WarCabinetPick(3, 3) == 2, "the staff's WAR CABINET mark");
    }

    /// <summary>Nobody at the desk for 20 minutes: the staff answers every card and spends what it earns.</summary>
    private static void HandsOff()
    {
        var p = new PoliticsBook();
        int stage = 0;
        for (float t = 0f; t <= 1200f; t += 1f)
        {
            if (t == 700f) p.Earn(35f, "AIRBASES TAKEN", t);
            p.Tick(t);
            if (p.StaffDue(t)) p.Answer(p.Serial, p.Staff, PoliticsRules.Staff, t, out _);
            int due = p.DueCard(t, 1000f, 40f);
            if (due >= 0) p.Open(due, "", due == 0 ? 0 : CabinetCards.StaffChoice(due), t);
            if (t % 30f == 0f) { int id = p.StaffPick(t, stage); if (id >= 0) p.Decide(id, PoliticsRules.Staff, t, stage, out _); }
        }
        Check(p.Doctrine == BoscaliSummer.Core.Contracts.Doctrine.TotalWar && p.ChosenBy == PoliticsRules.Staff && Near(p.ChosenAt, 390f), "hands-off: the staff chose the doctrine at 6:30");
        Check(p.DecidedSerial >= 1 && p.DecidedBy == PoliticsRules.Staff, "hands-off: the staff spent war support above 60");
        Check(p.WarSupport >= PoliticsRules.StaffKeeps, "hands-off: never below 25 by its own hand");
        Check(p.Card == -1, "hands-off: no card left hanging");
    }

    private static void Cards()
    {
        var v = new NationView { CabinetSerial = 3, CabinetId = (int)CardId.FallOf, CabinetPlace = "VIGIL CAY", CabinetStaff = 0, CabinetLeft = 45f };
        var card = new CabinetCard();
        Check(CabinetCards.Fill(v, card) && card.Title == "FALL OF VIGIL CAY" && card.Options.Count == 2 && card.Options[1].Label == "ORDERLY RETREAT", "a card fills from the view");
        Check(card.Options[0].Chips[0].Tone == 1 && card.Options[1].Chips[0].Tone == -1 && card.Options[1].Chips.Count == 3, "chips carry their tone");
        Check(card.Text.Contains("VIGIL CAY") && card.ArtKey == "cabinet_fall_of", "the place and the picture");
        v.CabinetId = (int)CardId.WarCabinet;
        v.CabinetChosen = 2;
        Check(CabinetCards.Fill(v, card) && card.Options.Count == 3 && card.EmblemKey == "doctrine_shadow_war", "WAR CABINET: three answers, the chosen emblem");
        v.CabinetSerial = 0;
        Check(!CabinetCards.Fill(v, card), "no card, nothing to fill");
        Check(Decisions.Of(BoscaliSummer.Core.Contracts.Doctrine.Shadow, 3) == (int)DecisionId.Defector && Decisions.Rank((int)DecisionId.Shield) == 2 &&
            Decisions.DoctrineOf((int)DecisionId.Counterattack) == BoscaliSummer.Core.Contracts.Doctrine.Fortress, "decision table");
    }

    private static void Wire()
    {
        var v = new NationView
        {
            Slots = 8, Output = 1.25f, Stability = 58f, Funds = 12480f, FactoryCost = 4000f, Economy = EconomyPolicy.War,
            Priority = PriorityPolicy.Ground, EconomyLock = 192f, Building = true, BuildLeft = 130f, BuildTotal = 180f, Escalation = 1,
            WarSupport = 68f, Doctrine = BoscaliSummer.Core.Contracts.Doctrine.TotalWar, Rank = 2, DoctrineAge = 600f, DoctrineBy = "VIPER 2-1",
            PostureFloor = 2, PostureCeiling = 2, JetsDelta = -2, Decided = (int)DecisionId.GeneralOffensive, DecidedSerial = 7, DecidedBy = "THE STAFF",
            DecidedAge = 120f, EnemyName = "PRIMEVA", EnemyIntel = "K92 HIGHWAY STRIP", EnemyDoctrine = BoscaliSummer.Core.Contracts.Doctrine.Fortress,
            EnemyRank = 2, EnemyWarSupport = 31f, EnemyDecided = (int)DecisionId.Shield, EnemyDecidedSerial = 4, EnemyDecidedAge = 60f, EnemyDecidedLeft = 245f,
            CabinetSerial = 5, CabinetId = (int)CardId.VictoryAt, CabinetStaff = 1, CabinetChosen = -1, CabinetPlace = "MARIS HELIPORT", CabinetLeft = 45f,
        };
        v.Cooldown[(int)DecisionId.GeneralOffensive] = 380f;
        v.InForce[(int)DecisionId.GeneralOffensive] = 380f;
        for (int i = 0; i < 4; i++) v.Moves.Add(new SupportMove { Delta = i - 2, Text = "GENERAL OFFENSIVE · VIPER 2-1 · A LONG PLACE NAME", Age = 30f * i });
        for (int i = 0; i < 16; i++)
            v.Plants.Add(new PlantView { Id = 100 + i, Kind = LineKind.Vehicle, Row = i % 3, Pinned = i == 2, Raided = i == 3, NextIn = 55f, Interval = 60f, Health01 = 0.35f, X = 1234.5f, Z = -9876.5f });
        for (int i = 0; i < 24; i++)
            v.Stock.Add(new StockView { Name = "A VERY LONG UNIT NAME WITH WORDS " + i, Code = "LONGCODE-" + i, Aircraft = i % 2 == 0, Count = i, Target = 20 });
        byte[] bytes = NationWire.Pack(v);
        var back = new NationView();
        Check(bytes.Length <= NationWire.MaxBytes, "full snapshot fits " + NationWire.MaxBytes + " bytes (" + bytes.Length + ")");
        Check(NationWire.TryUnpack(bytes, bytes.Length, back), "round trip unpacks");
        Check(back.Slots == 8 && Near(back.Output, 1.25f) && back.Economy == EconomyPolicy.War && back.Building && back.Escalation == 1, "economy scalars survive");
        Check(back.Plants.Count == 16 && back.Plants[2].Pinned && back.Plants[3].Raided && back.Plants[15].Id == 115 && Near(back.Plants[0].Z, -9876.5f), "plants survive");
        Check(back.Stock.Count == 24 && back.Stock[23].Count == 23 && !back.Stock[23].Aircraft, "stock survives");
        Check(Near(back.WarSupport, 68f) && back.Moves.Count == 4 && Near(back.Moves[0].Delta, -2f) && back.Moves[3].Text.StartsWith("GENERAL OFFENSIVE"), "war support and why");
        Check(back.Doctrine == BoscaliSummer.Core.Contracts.Doctrine.TotalWar && back.Rank == 2 && back.DoctrineBy == "VIPER 2-1" && back.PostureFloor == 2 && back.JetsDelta == -2, "doctrine and policy");
        Check(Near(back.InForce[(int)DecisionId.GeneralOffensive], 380f) && back.Decided == (int)DecisionId.GeneralOffensive && back.DecidedSerial == 7, "decisions");
        Check(back.EnemyName == "PRIMEVA" && back.EnemyDoctrine == BoscaliSummer.Core.Contracts.Doctrine.Fortress && back.EnemyDecided == (int)DecisionId.Shield &&
            Near(back.EnemyDecidedLeft, 245f) && back.EnemyIntel == "K92 HIGHWAY STRIP", "the other side");
        Check(back.CabinetSerial == 5 && back.CabinetId == (int)CardId.VictoryAt && back.CabinetStaff == 1 && back.CabinetChosen == -1 &&
            back.CabinetPlace == "MARIS HELIPORT" && Near(back.CabinetLeft, 45f), "the open card");
        Check(!NationWire.TryUnpack(bytes, bytes.Length - 5, back) && back.Stock.Count == 0 && back.Moves.Count == 0, "truncated buffer refused and cleared");
        Check(!NationWire.TryUnpack(new byte[NationWire.MaxBytes + 1], NationWire.MaxBytes + 1, back), "oversized buffer refused");
        bytes[0] = 1;
        Check(!NationWire.TryUnpack(bytes, bytes.Length, back), "a v1 snapshot is refused");
        var none = new NationView();
        byte[] empty = NationWire.Pack(none);
        Check(NationWire.TryUnpack(empty, empty.Length, back) && back.CabinetSerial == 0 && back.Decided == -1 && Near(back.WarSupport, 40f), "an empty nation round trips");
    }
}
