using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The INFO tab's pure core: the chain of command (order, fog, dashes, stated reasons, LOCATE) and the MISC lines.</summary>
    internal static class StrStaffTests
    {
        private const string Dash = "—";

        public static void Run()
        {
            OfficersStandTheaterFirstThenComponentsThenBases();
            TheRowCarriesItsParentAndAShortOffice();
            AnUnconfirmedEnemyOfficerIsSealed();
            AnUnknownReadsAsADash();
            NoStaffReadsAStatedReason();
            OwnStatusIsUnderFireOrAtPost();
            LocateFollowsTheStrikeList();
            TheMiscLinesReadOnlyWhatTheFeedsCarry();
            AMalformedOfficerFeedNeverThrows();
        }

        private static StrViewModel Build(StrFeeds f)
        {
            f = StrFixtures.Stamped(f);
            var vm = new StrViewModel();
            StrUiState ui = default;
            StrViewBuilder.Build(vm, in f, in ui, new StrOrdinals("P"), new StrOrdinals("S"), StrFixtures.Now);
            return vm;
        }

        private static StrFeeds WithOfficers(params StrOfficerIn[] rows)
        {
            StrFeeds f = StrFixtures.Feeds("rich");
            f.Officers = rows; f.OfficerCount = rows.Length;
            return f;
        }

        private static void OfficersStandTheaterFirstThenComponentsThenBases()
        {
            StrViewModel vm = Build(WithOfficers(
                StrFixtures.Officer(3, 2, true, "MAJ.", "Base Three", "PORT AURELIA"),
                StrFixtures.Officer(9, 1, false, "COL.", "Enemy Nine", "HARBOR"),
                StrFixtures.Officer(0, 0, true, "GEN.", "Theater Zero", "HQ"),
                StrFixtures.Officer(2, 1, true, "COL.", "Comp Two", "AIRBASE 2"),
                StrFixtures.Officer(1, 1, true, "COL.", "Comp One", "AIRBASE 1"),
                StrFixtures.Officer(8, 0, false, "GEN.", "Enemy Eight", "CAPITAL")));
            StrStaffView s = vm.Staff;
            TestAssert.That(s.Mode == StrStaffMode.Roster && s.OwnCount == 4 && s.EnemyCount == 2, "four own, two enemy");
            TestAssert.That(s.Own[0].Id == 0 && s.Own[1].Id == 1 && s.Own[2].Id == 2 && s.Own[3].Id == 3, "own: tier, then slot");
            TestAssert.That(s.Enemy[0].Id == 8 && s.Enemy[1].Id == 9, "enemy: theater before component");
            TestAssert.That(s.Own[0].Rank == "GEN." && s.Own[0].Name == "Theater Zero" && s.Own[0].Site == "HQ", "the row carries the post as sent");

            var many = new StrOfficerIn[12];
            for (int i = 0; i < 12; i++) many[i] = StrFixtures.Officer(i, 2, true, "CPT.", "Own " + i, "B" + i);
            vm = Build(WithOfficers(many));
            TestAssert.That(vm.Staff.OwnCount == StrViewModel.MaxOfficers, "twelve own posts in, eight rows out (the host's ceiling)");
        }

        private static void TheRowCarriesItsParentAndAShortOffice()
        {
            StrViewModel vm = Build(WithOfficers(
                StrFixtures.Officer(0, 0, true, "GEN.", "Theater Zero", "HQ", role: "THEATER COMMANDER"),
                StrFixtures.Officer(2, 1, true, "COL.", "Comp Two", "AIRBASE 2", role: "GROUND COMPONENT CMDR", parent: 0),
                StrFixtures.Officer(3, 2, true, "MAJ.", "Base Three", "PORT_AURELIA", role: "BASE COMMANDER", parent: 2),
                StrFixtures.Officer(4, 2, true, "MAJ.", "Base Four", "", role: "BASE COMMANDER", parent: 2)));
            StrStaffView s = vm.Staff;
            TestAssert.That(s.Own[0].ParentId == -1 && s.Own[1].ParentId == 0 && s.Own[2].ParentId == 2 && s.Own[3].ParentId == 2, "the row carries the post it answers to");
            TestAssert.That(s.Own[0].Office == "THEATER" && s.Own[1].Office == "GROUND COMPONENT" && s.Own[2].Office == "PORT AURELIA" && s.Own[3].Office == "BASE", "the office is one short line: theater, component, where the base stands");
            StrOfficerIn hidden = StrFixtures.Officer(12, 1, false, "COL.", "IMRE DANKOV", "PORT AURELIA", confirmed: false, role: "GROUND COMPONENT CMDR");
            TestAssert.That(Build(WithOfficers(hidden)).Staff.Enemy[0].Office == "COMPONENT", "a sealed post is told by its tier alone");
        }

        private static void AnUnconfirmedEnemyOfficerIsSealed()
        {
            StrOfficerIn hidden = StrFixtures.Officer(12, 1, false, "COL.", "IMRE DANKOV", "PORT AURELIA", intelAge: 30f, confirmed: false, trait: "VETERAN +20% BOUNTY", x: 3100f, z: 5000f);
            StrViewModel vm = Build(WithOfficers(hidden));
            StrOfficerRow r = vm.Staff.Enemy[0];
            TestAssert.That(r.Sealed && r.Name == "SEALED POST" && r.Rank == "" && r.Site == Dash, "a sealed post shows no name, rank or station");
            TestAssert.That(r.TraitCount == 0 && r.Locate.IsNone && r.Portrait == null, "and no traits, no portrait, no LOCATE");
            string all = r.Name + r.Rank + r.Role + r.Site + r.StatusText + r.SeenText + r.LocateWhy + r.Traits[0] + r.Traits[1] + r.Traits[2];
            TestAssert.That(!all.Contains("DANKOV") && !all.Contains("AURELIA") && !all.Contains("VETERAN") && !all.Contains("COL."), "none of the sealed officer's details leaks into any word of the row");
            TestAssert.That(r.StatusText == "SEALED" && r.Role == "COMPONENT COMMANDER", "it is told by its tier alone");
            TestAssert.That(vm.Staff.EnemyNote.Contains("SEALED"), "the enemy side states the fog rule");

            StrOfficerIn own = hidden; own.Own = true;
            TestAssert.That(!Build(WithOfficers(own)).Staff.Own[0].Sealed, "an own post is never sealed");
        }

        private static void AnUnknownReadsAsADash()
        {
            var bare = new StrOfficerIn { Id = 4, Tier = 2, Own = false, Confirmed = true, Name = "", Rank = "", Role = "", Site = "", Trait = "", IntelAge = -1f, X = float.NaN, Z = float.NaN };
            StrViewModel vm = Build(WithOfficers(bare));
            StrOfficerRow r = vm.Staff.Enemy[0];
            TestAssert.That(r.Name == Dash && r.Site == Dash && r.Rank == "" && r.Role == "BASE COMMANDER", "a post with nothing known reads dashes and its tier");
            TestAssert.That(r.SeenText == "SEEN " + Dash && r.StatusText == "SEEN " + Dash, "an unknown age is a dash, not a time");
            TestAssert.That(r.Locate.IsNone && r.LocateWhy.Length > 8, "no known place: no LOCATE, and it says why");

            StrOfficerIn own = bare; own.Own = true;
            StrOfficerRow o = Build(WithOfficers(own)).Staff.Own[0];
            TestAssert.That(o.Site == Dash && o.StatusText == Dash, "an own post with no site claims no position");
            TestAssert.That(Build(WithOfficers(StrFixtures.Officer(5, 2, false, "LT.", "Name", "port_aurelia", intelAge: 45f))).Staff.Enemy[0].Site == "PORT AURELIA", "wire identifiers print as words");
        }

        private static void NoStaffReadsAStatedReason()
        {
            StrFeeds off = StrFixtures.Feeds("rich");
            off.StaffPresent = false; off.PostsAvailable = false;
            StrStaffView s = Build(off).Staff;
            TestAssert.That(s.Mode == StrStaffMode.Off && s.OwnCount == 0 && s.EnemyCount == 0, "HIGH COMMAND off lists nobody");
            TestAssert.That(s.Head.Length >= 8 && s.Why.Length >= 12 && s.Do.Length >= 12, "and says what, why and what to do");

            StrFeeds waiting = StrFixtures.Feeds("rich");
            waiting.PostsAvailable = false; waiting.StaffStatus = "STAFF FORMING - WAIT FOR THE FIRST BOARD";
            s = Build(waiting).Staff;
            TestAssert.That(s.Mode == StrStaffMode.NoBoard && s.Why == "STAFF FORMING - WAIT FOR THE FIRST BOARD", "a board that is not here repeats the service's own words");
            waiting.StaffStatus = "";
            TestAssert.That(Build(waiting).Staff.Why.Length >= 12, "with none of its own it still gives a reason");

            s = Build(WithOfficers()).Staff;
            TestAssert.That(s.Mode == StrStaffMode.Roster && s.OwnNote.Length >= 12 && s.EnemyNote.Length >= 12, "a board with no posts says so for each side");
            TestAssert.That(Build(StrFixtures.Feeds("staffoff")).Staff.Mode == StrStaffMode.Off, "the staff-off fixture reads the stated reason");
            TestAssert.That(Build(StrFixtures.Feeds("unknown")).Staff.Mode == StrStaffMode.NoBoard, "the unknown fixture has no board and says so");
        }

        private static void OwnStatusIsUnderFireOrAtPost()
        {
            StrViewModel vm = Build(WithOfficers(
                StrFixtures.Officer(1, 1, true, "COL.", "Calm", "AIRBASE 1"),
                StrFixtures.Officer(2, 1, true, "COL.", "Hit", "AIRBASE 2", alert: true)));
            TestAssert.That(vm.Staff.Own[0].StatusText == "AT POST" && vm.Staff.Own[0].Ink == StrInkRole.Own, "an own post at its base is at post");
            TestAssert.That(vm.Staff.Own[1].StatusText == "UNDER FIRE" && vm.Staff.Own[1].Ink == StrInkRole.Danger, "a post under fire says so in the danger ink");
        }

        private static void LocateFollowsTheStrikeList()
        {
            StrFeeds f = WithOfficers(
                StrFixtures.Officer(12, 0, false, "GEN.", "On List", "CAPITAL", intelAge: 40f, x: 3100f, z: 5000f),
                StrFixtures.Officer(99, 1, false, "COL.", "Off List", "HARBOR", intelAge: 40f, x: 900f, z: 100f));
            f.Posts = new[] { StrFixtures.Post(12, "GEN. On List", 3000, 40f, 3100f, 5000f) }; f.PostCount = 1;
            StrViewModel vm = Build(f);
            StrOfficerRow on = vm.Staff.Enemy[0], off = vm.Staff.Enemy[1];
            TestAssert.That(on.Locate == new StrItemId(StrItemKind.Post, 12) && on.LocateWhy == "" && StrAnchors.LocateGate(vm, on.Locate).Allowed, "an officer on the strike list locates the same post the post rows do");
            TestAssert.That(off.Locate.IsNone && off.LocateWhy.Length >= 12, "an officer off the list has no LOCATE and the row says why");
            StrOfficerIn own = StrFixtures.Officer(1, 0, true, "GEN.", "Own", "HQ", x: 1f, z: 1f);
            TestAssert.That(Build(WithOfficers(own)).Staff.Own[0].Locate.IsNone, "an own post has no LOCATE");
        }

        private static void TheMiscLinesReadOnlyWhatTheFeedsCarry()
        {
            StrViewModel rich = Build(StrFixtures.Feeds("rich"));
            TestAssert.That(rich.Misc.Length == StrViewModel.MiscCount, "seven lines");
            string[] keys = { "STAFF", "CENSUS", "SORTIES", "BOARD", "HOST", "TACTICS", "CONVOYS" };
            for (int i = 0; i < keys.Length; i++)
                TestAssert.That(rich.Misc[i].Key == keys[i] && rich.Misc[i].Text.Length >= 8 && rich.Misc[i].Value.Length > 0, "line " + keys[i] + " has words and a value");
            TestAssert.That(rich.Misc[0].Value == "4 S OLD" && rich.Misc[3].Value == "2 S OLD", "the staff report and the board ages are the feeds' own");
            TestAssert.That(rich.Misc[2].Value == "LIVE" && rich.Misc[4].Value == "YOU", "the sortie feed is live and this peer is the host");
            TestAssert.That(rich.Misc[5].Value == "ON", "ground steering is on while the engine says Ready");

            StrFeeds f = StrFixtures.Feeds("rich");
            f.War.GroundReason = GroundReason.TacticsOff;
            TestAssert.That(Build(f).Misc[5].Value == "OFF", "the host switch off reads OFF");
            f.War.GroundReason = GroundReason.Unknown;
            TestAssert.That(Build(f).Misc[5].Value == Dash, "an unmeasured switch is a dash");
            TestAssert.That(rich.Misc[6].Value == Dash && rich.Misc[6].Text.Contains("NOT REPORTED"), "AI staff convoys are not published to pilots: a dash that says so");

            StrViewModel client = Build(StrFixtures.Feeds("client"));
            TestAssert.That(client.Misc[4].Value == "REMOTE", "a client names the host as remote");

            StrViewModel unknown = Build(StrFixtures.Feeds("unknown"));
            TestAssert.That(unknown.Misc[0].Value == Dash && unknown.Misc[1].Value == Dash && unknown.Misc[3].Value == Dash, "unknown ages are dashes");

            StrFeeds stale = StrFixtures.Feeds("rich");
            stale.SortiesFresh = false;
            TestAssert.That(Build(stale).Misc[2].Value == "STALE", "a stale sortie feed says stale");
            stale.SortiesAvailable = false;
            TestAssert.That(Build(stale).Misc[2].Value == "NO FEED", "no sortie feed says none");
        }

        private static void AMalformedOfficerFeedNeverThrows()
        {
            StrFeeds f = StrFixtures.Feeds("rich");
            f.Officers = null; f.OfficerCount = 5;
            Build(f);
            f.Officers = new StrOfficerIn[40]; f.OfficerCount = 40;
            for (int i = 0; i < 40; i++) f.Officers[i] = new StrOfficerIn { Id = -i, Tier = 9 - i, Own = i % 2 == 0, IntelAge = float.NaN, X = float.PositiveInfinity, Z = float.NaN };
            StrViewModel vm = null;
            try { vm = Build(f); } catch (System.Exception e) { TestAssert.That(false, "a malformed officer feed threw " + e.GetType().Name); }
            TestAssert.That(vm.Staff.OwnCount <= StrViewModel.MaxOfficers && vm.Staff.EnemyCount <= StrViewModel.MaxOfficers, "no side exceeds its cap");
            for (int i = 0; i < vm.Staff.EnemyCount; i++)
                TestAssert.That(vm.Staff.Enemy[i].Locate.IsNone, "a bad position is no place");
        }
    }
}
