using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>
    /// Wire-legal feeds for the offline panel and overlay harnesses and the builder tests (annex 05 P12 item 3). Data only: no TestAssert,
    /// no UnityEngine, so the Unity projects compile this file too. A model is always built by the real builder, never set by hand.
    /// Every fixture carries its own Stamp; a test that edits a feed must bump it, or the builder (rightly) builds nothing.
    /// </summary>
    internal static class StrFixtures
    {
        public const float Now = 1000f;
        public const ulong Local = 1001UL, Other = 2002UL;

        public static readonly string[] Names =
        {
            "rich", "proposals", "assessing", "noopening", "reduced", "staffoff", "hostlacks", "nofaction", "waiting", "noanswer",
            "stale", "unknown", "locked-by-other", "wave-held", "wing-tasked", "client",
        };

        private static int stamps;

        public static StrUiState Ui(string name) => default;

        /// <summary>The model the real builder makes from a fixture: fresh ordinals, the fixture clock.</summary>
        public static StrViewModel Model(string name)
        {
            StrFeeds feeds = Feeds(name);
            StrUiState ui = Ui(name);
            var vm = new StrViewModel();
            StrViewBuilder.Build(vm, in feeds, in ui, new StrOrdinals("P"), new StrOrdinals("S"), Now);
            return vm;
        }

        public static StrFeeds Feeds(string name)
        {
            switch (name)
            {
                case "rich": return Rich();
                case "proposals": return Live(Deck(Snapshot()));
                case "assessing": return Live(Reason(Snapshot(), TheaterEmptyReason.FirstReview, 12f));
                case "noopening": return NoOpening();
                case "reduced": return Reduced();
                case "staffoff": { StrFeeds f = Base(); f.StaffPresent = false; f.PostsAvailable = false; f.WarAvailable = false; f.War = null; f.WarAgeSeconds = -1f; f.WarSincePublished = -1f; f.WarCanCommand = false; f.Census = KnownCensus(); f.CensusAgeSeconds = 3f; return Stamped(f); }
                case "hostlacks": { StrFeeds f = Base(); f.IsHost = false; f.WarState = TheaterViewState.HostLacksModule; f.War = null; f.WarAgeSeconds = -1f; f.WarSincePublished = -1f; f.WarCanCommand = false; f.SortiesAvailable = false; return Stamped(f); }
                case "nofaction": { StrFeeds f = Base(); f.FactionCode = ""; f.WarState = TheaterViewState.NoFaction; f.War = null; f.WarAgeSeconds = -1f; f.WarSincePublished = -1f; f.WarCanCommand = false; return Stamped(f); }
                case "waiting": { StrFeeds f = Base(); f.IsHost = false; f.WarState = TheaterViewState.WaitingForHost; f.War = null; f.WarAgeSeconds = -1f; f.WarSincePublished = -1f; f.WarCanCommand = false; f.WaitSeconds = 8f; return Stamped(f); }
                case "noanswer": { StrFeeds f = Feeds("waiting"); f.WaitSeconds = 31f; return Stamped(f); }
                case "stale": { StrFeeds f = Rich(); f.WarState = TheaterViewState.Stale; f.WarAgeSeconds = 22f; f.WarSincePublished = 22f; f.WarCanCommand = false; return Stamped(f); }
                case "unknown": return Unknown();
                case "locked-by-other": { StrFeeds f = Rich(); f.War.Operation.PickedById = Other; f.War.Operation.PickedBy = "Raptor 2-3"; f.War.Operation.LockSeconds = 75f; return Stamped(f); }
                case "wave-held": { StrFeeds f = Rich(); f.War.Wave = Wave(WaveState.Blocked, WaveBlock.PoolShort, 3, 1850, 1200); return Stamped(f); }
                case "wing-tasked":
                    {
                        StrFeeds f = Rich();
                        f.Wing[1].State = WingSortieState.Tasked; f.Wing[1].Words = "WING TASKED"; f.Wing[1].Intent = WingSortieIntent.Cover;
                        return Stamped(f);
                    }
                case "client": { StrFeeds f = Rich(); f.IsHost = false; f.War.GroundReason = GroundReason.Unknown; f.Wing = new StrWingIn[0]; f.WingCount = 0; return Stamped(f); }
                default: throw new ArgumentException("unknown fixture " + name);
            }
        }

        // ---- building blocks (public: the builder tests and the harnesses compose their own variants from these) -----------------

        /// <summary>A host with a seat on BDF, a live war channel with no snapshot yet and nothing else.</summary>
        public static StrFeeds Base()
        {
            return new StrFeeds
            {
                IsHost = true, LocalId = Local, FactionCode = "BDF", HasOwn = true, OwnX = 0f, OwnZ = 0f,
                WarAvailable = true, WarState = TheaterViewState.Live, WarAgeSeconds = 4f, WarSincePublished = 4f, WarCanCommand = true,
                SortiesAvailable = true, SortiesFresh = true, SortieActiveLimit = 3, Sorties = new SecondaryObjectiveView[0],
                PostsAvailable = true, PostsAgeSeconds = 2f, Posts = new StrPostIn[0], Wing = new StrWingIn[0], StaffPresent = true, StaffStatus = "", Officers = new StrOfficerIn[0],
                PickerPrompt = "", KnownAirDefence = 6, Census = Unmeasured(), CensusAgeSeconds = -1f,
            };
        }

        /// <summary>A live war feed over a snapshot: the census comes with the snapshot, as the reader copies it.</summary>
        public static StrFeeds Live(TheaterWarSnapshot war)
        {
            StrFeeds f = Base();
            f.War = war; f.Census = war.Census; f.CensusAgeSeconds = f.WarAgeSeconds;
            return Stamped(f);
        }

        public static StrFeeds Stamped(StrFeeds f) { f.Stamp = ++stamps; return f; }

        public static TheaterWarSnapshot Snapshot()
        {
            return new TheaterWarSnapshot
            {
                Faction = "BDF", Posture = TheaterWarPosture.Steady, Reason = TheaterEmptyReason.None, StaffPickSeconds = -1f,
                Caps = TheaterCaps.StaffRunning | TheaterCaps.GroundSteering | TheaterCaps.GroundWar | TheaterCaps.HumanPilot | TheaterCaps.TerritoryField,
                Census = KnownCensus(), GroundReason = GroundReason.Ready,
                Fronts = new[]
                {
                    Front("@1,-2", "KESTREL CROSSING", 2400f, -1800f, 0.62f, 0.12f, TheaterFrontStatus.InContact),
                    Front("@3,1", "HARBOR", 6100f, 900f, 0.80f, 0.05f, TheaterFrontStatus.UnderPressure),
                    Front("#9", "PORT AURELIA", 3000f, 5200f, 0.35f, -0.08f, TheaterFrontStatus.Holding),
                    Front("@0,4", "NORTH RIDGE", -1500f, 7300f, 0.10f, 0f, TheaterFrontStatus.Holding),
                    Front("@6,6", "EAST FLATS", 9000f, 4400f, 0f, 0f, TheaterFrontStatus.Unconfirmed, observed: false, observedAge: 360f),
                },
                Log = new[] { "KESTREL CROSSING TAKEN - THE LINE MOVES UP +150", "STAFF PICKED KESTREL CROSSING", "POSTURE STEADY" },
            };
        }

        public static TheaterWarSnapshot Deck(TheaterWarSnapshot s)
        {
            s.StaffPickSeconds = 38f;
            s.Proposals = new[]
            {
                Proposal(22, TheaterOpKind.Defend, "#9", "PORT AURELIA", 3000f, 5200f, TheaterRisk.Moderate, 6, 3, false),
                Proposal(23, TheaterOpKind.Assault, "@3,1", "HARBOR", 6100f, 900f, TheaterRisk.High, 4, 5, true),
                Proposal(24, TheaterOpKind.Recon, "@6,6", "EAST FLATS", 9000f, 4400f, TheaterRisk.Unknown, 255, 255, false),
            };
            return s;
        }

        public static TheaterWarSnapshot Reason(TheaterWarSnapshot s, TheaterEmptyReason reason, float seconds)
        {
            s.Reason = reason; s.ReasonSeconds = seconds;
            return s;
        }

        public static TheaterFrontView Front(string key, string name, float x, float z, float pressure, float trend, TheaterFrontStatus status,
                                             bool observed = true, float observedAge = 0f)
        {
            return new TheaterFrontView
            {
                Key = key, Name = name, X = x, Z = z,
                Pressure = observed ? pressure : 0f, Trend = observed ? trend : 0f,
                ObservedAgeSeconds = observed ? 0f : observedAge, Status = observed ? status : TheaterFrontStatus.Unconfirmed,
                Observed = observed, NearFriendly = 3, NearHostile = observed ? (byte)2 : (byte)255, Carriers = 1,
            };
        }

        public static TheaterProposalView Proposal(int id, TheaterOpKind kind, string target, string name, float x, float z, TheaterRisk risk,
                                                   byte friendly, byte hostile, bool recommended)
        {
            return new TheaterProposalView
            {
                Id = id, Revision = 1, Kind = kind, TargetKey = target, Name = name, X = x, Z = z, Radius = 1200f,
                Pressure = 0.4f, Risk = risk, Carrier = TheaterCarrier.Ground, FriendlyGround = friendly, FriendlyOther = 255,
                HostileTracked = hostile, Recommended = recommended,
            };
        }

        public static TheaterOperationView Operation(int id, TheaterOpKind kind, string target, string name, float x, float z, TheaterPhase phase)
        {
            return new TheaterOperationView
            {
                Id = id, Revision = 1, Kind = kind, TargetKey = target, Name = name, X = x, Z = z, OriginX = 800f, OriginZ = -300f, Radius = 1200f,
                Phase = phase, ElapsedSeconds = 95f, LimitSeconds = 900f, Progress = 0.3f,
                PickedBy = "Viper 1-1", PickedById = Local, LockSeconds = 90f,
                Forces = new TheaterForcesView
                {
                    Groups = 3, Vehicles = 11, Formed = 11, Rally = 0, Line = 1, Assault = 2, Contact = 0, Withdraw = 0,
                    Moving = 5, Steered = 7, OnSlot = 2, Stalled = 1, EtaSeconds = 250, NearestToFixMeters = 1800f,
                    Marks = new[]
                    {
                        new TheaterForceMark { X = 1500f, Z = -1200f, Stage = 3, Alive = 4 },
                        new TheaterForceMark { X = 900f, Z = -500f, Stage = 3, Alive = 3 },
                        new TheaterForceMark { X = 700f, Z = -300f, Stage = 2, Alive = 4 },
                    },
                },
            };
        }

        public static TheaterWaveView Wave(WaveState state, WaveBlock block, int credits, int cost, int pool) =>
            new TheaterWaveView(state, block, credits, 3, "SUPPLY CONVOY ALPHA", cost, pool, 0, 1, 130f, true, 700f, -250f);

        public static TheaterCensus KnownCensus()
        {
            return new TheaterCensus
            {
                Known = true, AirFriendly = 6, AirParked = 4, AirHostileTracked = 3, GroundFriendly = 14, GroundHostileTracked = 9,
                ShipsFriendly = 2, ShipsHostileTracked = 1, BasesFriendly = 3, BasesHostile = 2, BasesNeutral = 1, BasesContested = 1, Radars = 4,
                SectorsFriendly = 9, SectorsHostile = 5, SectorsContested = 2, SectorsNeutral = 1,
                SortieCap = 2, SortieSead = 1, SortieCas = 3, SortieStrike = 1, SortieTransit = 2,
                FrontMetres = 14200f, Threat = 3, ThreatWhy = TheaterThreatWhy.AirContact,
            };
        }

        public static TheaterCensus Unmeasured() => TheaterCensus.Unmeasured;

        /// <summary>A sortie card tied to the live operation (operation id 21) unless tied is false.</summary>
        public static SecondaryObjectiveView Card(int id, string title, ObjectiveFamily family, bool offered, bool active, float secondsRemaining,
                                                  bool tied = true, bool operationLive = true, float x = 2200f, float z = -1500f, string acceptedBy = null,
                                                  int money = 4200, int xp = 120)
        {
            SortieLink link = tied
                ? new SortieLink(21, FocusRole.Assault, "KESTREL CROSSING", operationLive, true, x, z, 800f, false)
                : new SortieLink(0, FocusRole.None, "", false, false, 0f, 0f, 0f, false);
            var tasking = new ObjectiveTasking(family, ObjectiveAsset.Site, ObjectiveContact.Fixed, 0f, "", "", "", new ObjectivePhase[0],
                active ? ObjectiveLifecycle.Active : ObjectiveLifecycle.Offered);
            return new SecondaryObjectiveView(id, title, "", "", active ? "ACTIVE" : "OFFERED", "", active ? 0.4f : 0f, secondsRemaining, money, xp, false,
                offered, active, active, x, z, 800f, acceptedBy, tasking, link);
        }

        public static StrPostIn Post(int id, string name, int bounty, float intelAge, float x, float z, bool capped = false) =>
            new StrPostIn
            {
                Id = id, Bounty = bounty, Name = name, Rank = "COL.", Role = "GROUND COMPONENT CMDR", Site = "PORT AURELIA",
                Trait = "VETERAN +20% BOUNTY", IntelAge = intelAge, X = x, Z = z, BountyCapped = capped,
            };

        public static StrOfficerIn Officer(int id, int tier, bool own, string rank, string name, string site, float intelAge = -1f, bool confirmed = true,
            bool alert = false, string trait = "", float x = 0f, float z = 0f, string role = "") =>
            new StrOfficerIn
            {
                Id = id, Tier = tier, Own = own, Alert = alert, Confirmed = confirmed || own, Rank = rank, Name = name, Role = role, Site = site, Trait = trait,
                IntelAge = intelAge, X = x, Z = z,
            };

        // ---- the named fixtures that need more than one block ------------------------------------------------------------------

        private static StrFeeds Rich()
        {
            TheaterWarSnapshot s = Deck(Snapshot());
            s.StaffPickSeconds = -1f;
            s.Operation = Operation(21, TheaterOpKind.Assault, "@1,-2", "KESTREL CROSSING", 2400f, -1800f, TheaterPhase.Advancing);
            s.Wave = Wave(WaveState.Earning, WaveBlock.None, 2, 1850, 7400);
            StrFeeds f = Live(s);
            f.Sorties = new[]
            {
                Card(31, "STRIKE HARBOR SAM", ObjectiveFamily.Strike, true, false, 540f),
                Card(32, "COVER THE ADVANCE", ObjectiveFamily.AirCover, true, false, 720f),
                Card(33, "SCOUT THE RIDGE", ObjectiveFamily.Reconnaissance, false, true, 580f, acceptedBy: "Viper 1-1"),
            };
            f.Wing = new[]
            {
                new StrWingIn { SortieId = 31, Refusal = null, State = WingSortieState.None, Intent = WingSortieIntent.Destroy, Words = "" },
                new StrWingIn { SortieId = 32, Refusal = null, State = WingSortieState.None, Intent = WingSortieIntent.Cover, Words = "" },
                new StrWingIn { SortieId = 33, Refusal = null, State = WingSortieState.None, Intent = WingSortieIntent.Observe, Words = "" },
            };
            f.WingCount = 3;
            f.Posts = new[] { Post(12, "COL. IMRE DANKOV", 3000, 360f, 3100f, 5000f), Post(5, "MAJ. VERA STOLL", 1800, 45f, 6400f, 1100f) };
            f.PostCount = 2;
            f.Officers = new[]
            {
                Officer(0, 0, true, "GEN.", "MARA OKONKWO", "HIGH COMMAND", role: "THEATER COMMANDER", trait: "STRATEGIC MIND +10% INCOME"),
                Officer(1, 1, true, "COL.", "ILYA BRANDT", "PORT AURELIA", role: "AIR COMPONENT CMDR", trait: "LOGISTICS MIND +15% STIPEND" + StrWords.Dot + "FAST SORTIES +5% PATROL"),
                Officer(2, 1, true, "COL.", "TESS HARKER", "KESTREL AIRBASE", role: "GROUND COMPONENT CMDR", alert: true, trait: "NO STAFF BONUS"),
                Officer(3, 2, true, "MAJ.", "DEV PATEL", "HARBOR AIRFIELD", role: "BASE COMMANDER", trait: "SUPPLY HAWK +5% PAY"),
                Officer(4, 2, true, "CPT.", "NOA LINDQVIST", "RIDGE STRIP", role: "BASE COMMANDER"),
                Officer(14, 0, false, "GEN.", "KORVIN ASH", "CAPITAL BARRACKS", intelAge: 520f, x: 900f, z: 7200f, role: "THEATER COMMANDER", trait: "IRON WILL +25% BOUNTY"),
                Officer(12, 1, false, "COL.", "IMRE DANKOV", "PORT AURELIA", intelAge: 360f, x: 3100f, z: 5000f, role: "GROUND COMPONENT CMDR", trait: "VETERAN +20% BOUNTY"),
                Officer(5, 2, false, "MAJ.", "VERA STOLL", "NORTH RIDGE BASE", intelAge: 45f, x: 6400f, z: 1100f, role: "BASE COMMANDER", trait: "NO STAFF BONUS"),
            };
            f.OfficerCount = 8;
            return Stamped(f);
        }

        private static StrFeeds NoOpening()
        {
            TheaterWarSnapshot s = Reason(Snapshot(), TheaterEmptyReason.NoFriendlyGroundNearFront, 0f);
            s.Fronts = new[] { s.Fronts[2], s.Fronts[3], s.Fronts[4] };
            return Live(s);
        }

        private static StrFeeds Reduced()
        {
            TheaterWarSnapshot s = Reason(Snapshot(), TheaterEmptyReason.NoGroundWar, 0f);
            s.Fronts = new TheaterFrontView[0];
            s.GroundReason = GroundReason.NoVehicles;
            return Live(s);
        }

        /// <summary>Every unknown encoding the wire allows, in one snapshot: nothing here may print as a number.</summary>
        private static StrFeeds Unknown()
        {
            TheaterWarSnapshot s = Deck(Snapshot());
            s.StaffPickSeconds = -1f;
            s.Fronts = new[]
            {
                Front("@1,-2", "KESTREL CROSSING", 2400f, -1800f, 0f, 0f, TheaterFrontStatus.Unconfirmed, observed: false, observedAge: -1f),
                Front("@3,1", "HARBOR", 6100f, 900f, 0f, 0f, TheaterFrontStatus.Unconfirmed, observed: false, observedAge: -1f),
            };
            foreach (TheaterFrontView fv in s.Fronts) { fv.ObservedAgeSeconds = -1f; fv.NearFriendly = 255; fv.Carriers = 255; }
            foreach (TheaterProposalView p in s.Proposals) { p.Risk = TheaterRisk.Unknown; p.FriendlyGround = 255; p.FriendlyOther = 255; p.HostileTracked = 255; p.Pressure = 0f; }
            s.Operation = Operation(21, TheaterOpKind.Assault, "@1,-2", "KESTREL CROSSING", 2400f, -1800f, TheaterPhase.Forming);
            s.Operation.Progress = -1f;
            s.Operation.Forces = new TheaterForcesView();     // every byte 255, eta 65535, nearest -1, no marks
            s.Operation.PickedBy = ""; s.Operation.PickedById = 0UL; s.Operation.PickedByStaff = true; s.Operation.LockSeconds = 0f;
            s.Wave = new TheaterWaveView(WaveState.Earning, WaveBlock.None, 0, 3, "", -1, -1, -1, 0, -1f, false, 0f, 0f);
            s.Census = Unmeasured();
            s.GroundReason = GroundReason.Unknown;
            StrFeeds f = Live(s);
            f.WarAgeSeconds = -1f; f.WarSincePublished = -1f; f.HasOwn = false; f.KnownAirDefence = -1; f.PostsAvailable = false; f.CensusAgeSeconds = -1f;
            f.SortiesFresh = true;
            f.Sorties = new[] { Card(31, "STRIKE HARBOR SAM", ObjectiveFamily.Strike, true, false, 0f, money: 0, xp: 0) };
            return Stamped(f);
        }

        // ---- wire legality (spec 6.1 bounds; the builder tests and StrFixtureTests assert it) -------------------------------------

        /// <summary>The first thing wrong with a feed as a wire value, or null. Unknown is carried only by the sentinels, never NaN.</summary>
        public static string Illegal(in StrFeeds f)
        {
            if (!Fine(f.OwnX) || !Fine(f.OwnZ) || !Fine(f.WarAgeSeconds) || !Fine(f.WarSincePublished) || !Fine(f.WaitSeconds) || !Fine(f.PostsAgeSeconds) || !Fine(f.CensusAgeSeconds))
                return "a feed number is not finite";
            if (f.Officers != null && (f.OfficerCount < 0 || f.OfficerCount > f.Officers.Length || f.OfficerCount > 2 * StrViewModel.MaxOfficers)) return "officer count";
            for (int i = 0; f.Officers != null && i < f.OfficerCount; i++)
            {
                StrOfficerIn o = f.Officers[i];
                if (!Fine(o.X) || !Fine(o.Z) || !Fine(o.IntelAge) || o.IntelAge > 600f || o.Tier < 0 || o.Tier > 2) return "officer " + i;
            }
            if (f.Posts != null && (f.PostCount < 0 || f.PostCount > f.Posts.Length || f.PostCount > StrViewModel.MaxPosts + 3)) return "post count";
            for (int i = 0; i < f.PostCount; i++)
            {
                StrPostIn p = f.Posts[i];
                if (!Fine(p.X) || !Fine(p.Z) || !Fine(p.IntelAge) || p.IntelAge < 0f || p.IntelAge > 600f || p.Bounty < 0) return "post " + i;
            }
            for (int i = 0; i < f.WingCount; i++)
                if ((f.Wing[i].Refusal ?? "").Length > 100 || (f.Wing[i].Words ?? "").Length > 40) return "wing " + i;
            if (f.WarLast != null && (!Fine(f.WarLast.LockSeconds) || f.WarLast.LockSeconds < 0f || f.WarLast.LockSeconds > 120f || (f.WarLast.Detail ?? "").Length > 24)) return "command result";

            TheaterCensus c = f.Census;
            string census = CensusProblem(c);
            if (census != null) return census;
            TheaterWarSnapshot s = f.War;
            if (s == null) return null;
            if (!Fine(s.SensedAgeSeconds) || !Fine(s.ReasonSeconds) || s.ReasonSeconds < 0f || s.ReasonSeconds > 600f || s.StaffPickSeconds < -1f || s.StaffPickSeconds > 120f) return "snapshot meta";
            if (s.Fronts.Length > 8) return "more than 8 fronts";
            foreach (TheaterFrontView fv in s.Fronts)
            {
                if (string.IsNullOrEmpty(fv.Key) || fv.Key.Length > 96 || fv.Name.Length > 32 || !Fine(fv.X) || !Fine(fv.Z)) return "front " + fv.Key;
                if (!Fine(fv.Pressure) || fv.Pressure < 0f || fv.Pressure > 1f || !Fine(fv.Trend) || fv.Trend < -1f || fv.Trend > 1f) return "front range " + fv.Key;
                if (fv.ObservedAgeSeconds < -1f || fv.ObservedAgeSeconds > 86400f) return "front age " + fv.Key;
                if (!fv.Observed && (fv.NearHostile != 255 || fv.Pressure != 0f || fv.Trend != 0f)) return "an unobserved front carries a number " + fv.Key;
            }
            if (s.Proposals.Length > 3) return "more than 3 proposals";
            foreach (TheaterProposalView p in s.Proposals)
                if (p.Id <= 0 || p.Revision <= 0 || p.Name.Length > 32 || string.IsNullOrEmpty(p.TargetKey) || p.TargetKey.Length > 96 || !Fine(p.X) || !Fine(p.Z) ||
                    p.Radius < 0f || p.Radius > 30000f || !Fine(p.Pressure) || p.Pressure < 0f || p.Pressure > 1f) return "proposal " + p.Id;
            TheaterOperationView op = s.Operation;
            if (op != null)
            {
                if (op.Id <= 0 || op.Revision <= 0 || op.Name.Length > 32 || string.IsNullOrEmpty(op.TargetKey) || op.TargetKey.Length > 96 || op.PickedBy.Length > 24) return "operation text";
                if (!Fine(op.X) || !Fine(op.Z) || !Fine(op.OriginX) || !Fine(op.OriginZ) || op.Radius < 0f || op.Radius > 30000f) return "operation place";
                if (op.ElapsedSeconds < 0f || op.ElapsedSeconds > 3600f || op.LimitSeconds < 0f || op.LimitSeconds > 3600f || op.Progress < -1f || op.Progress > 1f) return "operation clock";
                if (!Fine(op.LockSeconds) || op.LockSeconds < 0f || op.LockSeconds > 120f) return "operation lock";
                TheaterForcesView fo = op.Forces;
                foreach (byte b in new[] { fo.Groups, fo.Rally, fo.Line, fo.Assault, fo.Contact, fo.Withdraw }) if (b > 32 && b != 255) return "force count";
                foreach (byte b in new[] { fo.Vehicles, fo.Formed, fo.Moving, fo.Steered, fo.OnSlot, fo.Stalled }) if (b > 192 && b != 255) return "force count";
                if ((fo.EtaSeconds > 3600 && fo.EtaSeconds != 65535) || !Fine(fo.NearestToFixMeters) || fo.NearestToFixMeters < -1f) return "force eta";
                if (fo.Marks.Length > 8) return "more than 8 marks";
                foreach (TheaterForceMark mk in fo.Marks) if (!Fine(mk.X) || !Fine(mk.Z) || mk.Stage > 5 || mk.Alive > 6) return "mark";
            }
            TheaterWaveView w = s.Wave;
            if (w != null)
            {
                if ((int)w.State > 2 || (int)w.Block > 8 || w.Credits < 0 || w.Credits > 6 || w.CreditsPerWave < 1 || w.CreditsPerWave > 9 || w.Group.Length > 32) return "wave header";
                if (w.Cost < -1 || w.Cost > 1000000 || w.Pool < -1 || w.Pool > 1000000 || w.CooldownSeconds < -1 || w.Bought < 0 || w.LastBoughtAgo < -1f || !Fine(w.LastBoughtAgo)) return "wave numbers";
                if (w.State == WaveState.Off && ((int)w.Block < 1 || (int)w.Block > 3)) return "wave off without a block";
                if (w.State == WaveState.Blocked && w.Block == WaveBlock.None) return "wave blocked without a block";
                if (w.DepotKnown && (!Fine(w.DepotX) || !Fine(w.DepotZ))) return "wave depot";
            }
            if (s.Log.Length > 8) return "more than 8 log lines";
            foreach (string line in s.Log) if (line.Length > 64) return "log line";
            string snapCensus = CensusProblem(s.Census);
            return snapCensus;
        }

        private static string CensusProblem(in TheaterCensus c)
        {
            short[] counts = { c.AirFriendly, c.AirParked, c.AirHostileTracked, c.GroundFriendly, c.GroundHostileTracked, c.ShipsFriendly, c.ShipsHostileTracked,
                c.BasesFriendly, c.BasesHostile, c.BasesNeutral, c.BasesContested, c.Radars, c.SectorsFriendly, c.SectorsHostile, c.SectorsContested, c.SectorsNeutral,
                c.SortieCap, c.SortieSead, c.SortieCas, c.SortieStrike, c.SortieTransit };
            if (!c.Known)
            {
                foreach (short n in counts) if (n != -1) return "an unmeasured census carries a number";
                return c.FrontMetres == -1f && c.Threat == 0 && c.ThreatWhy == TheaterThreatWhy.None ? null : "an unmeasured census carries a threat or a front";
            }
            foreach (short n in counts) if (n < -1 || n > 16384) return "census range";
            if (c.BasesFriendly > 255 || c.BasesHostile > 255 || c.BasesNeutral > 255 || c.BasesContested > 255) return "census bases";
            if (!Fine(c.FrontMetres) || c.FrontMetres < -1f || c.FrontMetres > 1e7f || c.Threat > 5 || (int)c.ThreatWhy > 6) return "census front or threat";
            return null;
        }

        private static bool Fine(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
