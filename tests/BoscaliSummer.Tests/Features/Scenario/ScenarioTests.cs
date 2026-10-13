using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Core.Scenario;

namespace BoscaliSummer.Tests.Features.Scenario
{
    internal static class ScenarioTests
    {
        private const string Script = @"
# a comment
scenario test-one
title ""Test One""
base ""Escalation""
group ""Tests""
campaign saga 2
without weather events
about
| First line
| goes on.
|
| Second paragraph.
time 6.5
weather 0.3
ending 12
hold Base_A Base_B
begin patrol clock
unit raider like COIN at 100 700 -200 heading=90 speed=92 skill=0.7
copy Wave as extra without=TypeA,TypeB
picture snap poster some_event
picture own file ""my photo.png""
objective patrol bdf ""Fly the line"" reach 1 2 -3 range=1500
  then spawn raider
  then start raid
  then message ""Contact \""close\"""" side-only
objective raid bdf ""Break it"" destroy raider ""Unit Two"" need=60%
  then finish clock
objective clock wait 300
objective both after patrol raid need=any
objective first pala sortie 40
on ""Base Objective""
  then stop clock
cast boss command ""BOSS"" ""Somebody in charge""
shot wide orbit airbase ""Big Base"" radius=900 height=200 from=10 to=80 seconds=6 fov=50
shot ship orbit unit Carrier
shot run fly 0 100 0 to 500 120 800 look 1000 0 1000 seconds=4
beat opening when start unless=seen
  cut wide ""A Title"" ""Under it""
  cut ship ""Just a caption"" photo=snap stamp=""PLACE / DAY 1""
  remember seen
beat brief when join side=bdf
  say boss ""Hello there.""
  hint ""Press a thing."" seconds=5
  wait 2
  flag briefed
beat after when flag briefed
  dip
  sky storm
  clock 18.5
  card ""CARD"" ""words"" seconds=3
  finish
beat won when done raid repeat
  say boss ""Again.""
beat late when time 50 if=briefed
  music ""theme.mp3"" volume=0.5
  say boss ""Late.""
  music stop
";

        /// <summary>Records what a script puts on the stage, in order.</summary>
        private sealed class Stage : IScenarioStage
        {
            public readonly List<string> Log = new List<string>();
            public bool Camera = true, Playing;
            public int Finished;

            public void Say(ScenarioCast cast, string text, float seconds) => Log.Add("say " + cast.Callsign + ": " + text);
            public void Card(string title, string text, float seconds) => Log.Add("card " + title + "|" + text);
            public void Hint(string text, float seconds) => Log.Add("hint " + text);

            public bool Cut(IReadOnlyList<ScenarioCut> cuts)
            {
                if (!Camera) return false;
                Playing = true;
                foreach (ScenarioCut cut in cuts) Log.Add("cut " + cut.Shot.Id + "|" + cut.Title + "|" + cut.Caption + "|" + cut.Stamp + "|" + cut.Photo?.Id);
                return true;
            }

            public void Music(string file, float volume) => Log.Add("music " + (file ?? "stop"));
            public void Enlist(ScenarioSide side) => Log.Add("enlist " + side);
            public void Choose() => Log.Add("choose");
            public void Seat(string aircraft, float x, float y, float z, float heading, float speed) => Log.Add("seat " + aircraft);
            public void Dip() => Log.Add("dip");
            public void Sky(string name) => Log.Add("sky " + name);
            public void Clock(float hour) => Log.Add("clock " + hour.ToString(System.Globalization.CultureInfo.InvariantCulture));

            public bool CutPlaying => Playing;
            public void Remember(string flag) => Log.Add("remember " + flag);
            public void Finish() => Finished++;
        }

        public static void Run()
        {
            Parses();
            ReportsMistakes();
            PlaysBeats();
            KeepsProgress();
            BuiltInScriptsAreSound();
        }

        private static void Parses()
        {
            ScenarioDef def = ScenarioParser.Parse("test.scenario", Script);
            TestAssert.Eq(string.Join(" / ", def.Errors), "", "a well-formed script reads without complaint");
            TestAssert.That(def.Id == "test-one" && def.Title == "Test One" && def.Base == "Escalation" && def.Group == "Tests", "the header is read");
            TestAssert.That(def.Campaign == "saga" && def.Chapter == 2 && def.Saga == "saga", "the campaign and chapter are read");
            TestAssert.Eq(def.About, "First line goes on.\n\nSecond paragraph.", "about lines join into paragraphs");
            TestAssert.That(def.Time == 6.5f && def.Weather == 0.3f && def.Ending == 12f, "time, weather and ending are read");
            TestAssert.Eq(string.Join(",", def.Without) + ";" + string.Join(",", def.Hold) + ";" + string.Join(",", def.Begin),
                "weather,events;Base_A,Base_B;patrol,clock", "module, hold and begin lists are read");

            ScenarioUnit unit = def.Units[0];
            TestAssert.That(unit.Name == "raider" && unit.Like == "COIN" && unit.X == 100f && unit.Y == 700f && unit.Z == -200f &&
                unit.Heading == 90f && unit.Speed == 92f && unit.Sets.Count == 1 && unit.Sets[0].Key == "skill", "a unit keeps its place, heading, speed and extra fields");

            TestAssert.That(def.Copies.Count == 1 && def.Copies[0].Outcome == "Wave" && def.Copies[0].Prefix == "extra" &&
                string.Join("+", def.Copies[0].Without) == "TypeA+TypeB", "a copied group keeps its outcome, its name and the types it leaves out");
            TestAssert.That(def.Pictures["snap"].Poster == "some_event" && def.Pictures["own"].File == "my photo.png", "pictures are an event painting or a file");
            TestAssert.Eq(def.Objectives.Count, 6, "every objective and 'on' is kept");
            ScenarioObjective patrol = def.Objectives[0];
            TestAssert.That(patrol.Kind == ObjectiveKind.Reach && patrol.Side == ScenarioSide.Bdf && patrol.Display == "Fly the line" &&
                patrol.Z == -3f && patrol.Range == 1500f, "a reach objective keeps its side, name, point and range");
            TestAssert.That(patrol.Then.Count == 3 && patrol.Then[0].Kind == ThenKind.Spawn && patrol.Then[1].Names[0] == "raid" &&
                patrol.Then[2].Text == "Contact \"close\"" && patrol.Then[2].SideOnly, "outcomes hang on the objective above them; quotes can be escaped");
            ScenarioObjective raid = def.Objectives[1];
            TestAssert.That(raid.Kind == ObjectiveKind.Destroy && raid.Targets.Length == 2 && raid.Targets[1] == "Unit Two" &&
                raid.Need == "some" && Math.Abs(raid.Percent - 0.6f) < 1e-6f, "a destroy objective keeps quoted names and its percentage");
            TestAssert.That(def.Objectives[2].Kind == ObjectiveKind.Wait && def.Objectives[2].Display == null && def.Objectives[2].Seconds == 300f,
                "an objective without a display name is a hidden one");
            TestAssert.That(def.Objectives[3].Kind == ObjectiveKind.After && def.Objectives[3].Need == "any", "'after' waits on other objectives");
            TestAssert.That(def.Objectives[4].Kind == ObjectiveKind.Sortie && def.Objectives[4].Side == ScenarioSide.Pala && def.Objectives[4].Score == 40f,
                "a sortie objective keeps its side and score");
            TestAssert.That(def.Objectives[5].Kind == ObjectiveKind.Existing && def.Objectives[5].Id == "Base Objective" && def.Objectives[5].Then.Count == 1,
                "'on' adds outcomes to an objective of the base mission");

            TestAssert.That(def.Cast["boss"].Callsign == "BOSS" && def.Cast["boss"].Net == "command" && def.Cast["boss"].Role == "Somebody in charge", "cast is read");
            ScenarioShot wide = def.Shots["wide"];
            TestAssert.That(wide.Kind == ShotKind.OrbitAirbase && wide.Target == "Big Base" && wide.Radius == 900f && wide.To == 80f && wide.Fov == 50f,
                "an orbit shot keeps its target and numbers");
            TestAssert.That(def.Shots["ship"].Kind == ShotKind.OrbitUnit && def.Shots["ship"].Seconds == 8f, "a shot's options have defaults");
            TestAssert.That(def.Shots["run"].Kind == ShotKind.Fly && def.Shots["run"].ToZ == 800f && def.Shots["run"].LookX == 1000f, "a fly shot keeps its three points");

            ScenarioBeat opening = def.Beats[0];
            TestAssert.That(opening.Trigger == TriggerKind.Start && opening.Unless == "seen" && opening.Actions.Count == 3 &&
                opening.Actions[0].Title == "A Title" && opening.Actions[0].Text == "Under it" && opening.Actions[1].Title == null &&
                opening.Actions[1].Text == "Just a caption" && opening.Actions[1].Photo == "snap" && opening.Actions[1].Stamp == "PLACE / DAY 1" &&
                opening.Actions[0].Photo == null, "a cut has a caption or a title with a line under it, and may carry a picture and a stamp");
            TestAssert.That(def.Beats[1].Side == ScenarioSide.Bdf && def.Beats[1].Trigger == TriggerKind.Join && def.Beats[1].Actions.Count == 4, "a beat keeps its side and actions");
            TestAssert.That(def.Beats[3].Repeat && def.Beats[3].Arg == "raid" && def.Beats[4].Trigger == TriggerKind.Time && def.Beats[4].Seconds == 50f &&
                def.Beats[4].If == "briefed", "triggers keep their objective, clock time and conditions");

            TestAssert.Eq(ScenarioDef.IdOf("Some words.\n\n" + ScenarioDef.Marker("test-one")), "test-one", "a mission description names its script");
            foreach (string plain in new[] { null, "", "Escalation", ScenarioDef.Marker("test-one") + " edited", "[Boscali Summer scenario: Not An Id]" })
                TestAssert.That(ScenarioDef.IdOf(plain) == null, "a mission without the marker at its end has no script: " + plain);
        }

        private static void ReportsMistakes()
        {
            ScenarioDef def = ScenarioParser.Parse("bad.scenario", @"
scenario Bad Id
title ""T""
base ""B""
| stray text
frobnicate now
objective lonely reach 1 2 3
objective two bdf reach 1 2
then start nothing
say nobody ""words""
cast boss radio ""BOSS""
copy Wave
picture Bad poster x
picture p file ""../escape.png""
cast boss command ""BOSS""
shot s orbit nowhere
shot s2 orbit unit Thing
beat b when someday
beat c when join side=martians
beat d when start
  say ghost ""boo""
  cut missing
  cut s2 ""words"" photo=nope
  sky purple
  clock 30
  card ""unclosed
beat d when start
  wait 1
unit u like X at 1 2
objective o destroy a need=sometimes
time 30
");
            string all = string.Join("\n", def.Errors);
            foreach (string expected in new[]
            {
                "bad.scenario:2:", "'|' text line belongs under 'about'", "unknown statement 'frobnicate'", "a reach objective needs a side",
                "reach needs <x> <height> <z>", "'then' belongs under an 'objective' or 'on'", "'say' belongs under a 'beat'", "unknown net 'radio'",
                "orbit needs", "unknown trigger 'someday'", "side is bdf, pala or both", "nobody in the cast is called 'ghost'", "there is no shot 'missing'",
                "a quote is left open", "beat 'd' is written twice", "copy needs:", "picture needs an id", "picture needs: poster", "there is no picture 'nope'",
                "sky is storm", "clock is an hour of the day", "unit needs 'at <x> <height> <z>'", "need is any, all, order or a percentage", "time is an hour of the day"
            })
                TestAssert.That(all.Contains(expected), "a mistake is named with its line: missing '" + expected + "' in\n" + all);
            TestAssert.That(def.Cast.Count == 1 && def.Beats.Count >= 1, "one bad line never costs the rest of a script");
            TestAssert.That(ScenarioParser.Parse("empty", "").Errors.Count == 3, "an empty script is told what it lacks");
        }

        private static void PlaysBeats()
        {
            ScenarioDef def = ScenarioParser.Parse("test.scenario", Script);
            var stage = new Stage();
            var kept = new HashSet<string>();
            var runner = new ScenarioRunner(def, stage, kept.Contains);
            float now = 0f;
            void Step(float seconds)
            {
                for (float end = now + seconds; now < end; now += 0.25f) runner.Tick(now);
            }

            runner.Raise(TriggerKind.Start);
            Step(1f);
            TestAssert.Eq(string.Join(";", stage.Log), "cut wide|A Title|Under it||;cut ship||Just a caption|PLACE / DAY 1|snap",
                "cuts written one after another play as one cutscene");
            runner.Raise(TriggerKind.Join);
            Step(5f);
            TestAssert.Eq(stage.Log.Count, 2, "the story waits while a cutscene is on screen, and a beat for a side waits for the pilot to have one");
            stage.Playing = false;
            Step(1f);
            TestAssert.Eq(stage.Log[2], "remember seen", "the beat goes on when the cutscene ends");

            runner.Side = ScenarioSide.Pala;
            runner.Raise(TriggerKind.Join);
            Step(2f);
            TestAssert.Eq(stage.Log.Count, 3, "a beat written for the other side stays silent");
            runner.Side = ScenarioSide.Bdf;
            runner.Raise(TriggerKind.Join);
            Step(0.5f);
            TestAssert.That(stage.Log[3] == "say BOSS: Hello there." && stage.Log.Count == 4, "one line at a time: the hint waits for the line before it");
            Step(4f);
            TestAssert.Eq(stage.Log[4], "hint Press a thing.", "the next action follows when the line has been read");
            TestAssert.That(!runner.Flags.Contains("briefed") || stage.Log.Count >= 5, "a wait holds the beat");
            Step(4f);
            TestAssert.That(runner.Flags.Contains("briefed") && stage.Log[5] == "dip" && stage.Log[6] == "sky storm" && stage.Log[7] == "clock 18.5" &&
                stage.Log[8] == "card CARD|words", "setting a flag starts the beat that waits for it; a dip, the sky and the clock do not hold the story up");
            Step(4f);
            TestAssert.That(stage.Finished == 1 && runner.Finished, "finish tells the stage once");
            runner.Raise(TriggerKind.Join);
            Step(3f);
            TestAssert.Eq(stage.Log.Count, 9, "a beat plays once");

            runner.Raise(TriggerKind.Done, "patrol");
            runner.Raise(TriggerKind.Done, "raid");
            Step(4f);
            runner.Raise(TriggerKind.Done, "raid");
            Step(4f);
            TestAssert.That(stage.Log.Count == 11 && stage.Log[9] == "say BOSS: Again." && stage.Log[10] == "say BOSS: Again.",
                "a beat waits for its own objective, and a repeating beat plays every time");
            Step(40f);
            TestAssert.That(stage.Log[stage.Log.Count - 3] == "music theme.mp3" && stage.Log[stage.Log.Count - 2] == "say BOSS: Late." &&
                stage.Log[stage.Log.Count - 1] == "music stop", "a beat on the clock plays when its time and its condition have both come; music starts and stops around it");
            int count = stage.Log.Count;
            Step(10f);
            TestAssert.That(stage.Log.Count == count && runner.Idle, "a beat on the clock plays once, and the runner comes to rest");

            // Without a camera the words of a cutscene are still shown; a kept flag silences the beat that sets it.
            var blind = new Stage { Camera = false };
            var second = new ScenarioRunner(def, blind);
            second.Raise(TriggerKind.Start);
            now = 0f;
            for (; now < 30f; now += 0.25f) second.Tick(now);
            TestAssert.Eq(string.Join(";", blind.Log), "card A Title|Under it;card |Just a caption;remember seen", "a cutscene without a camera becomes cards");
            kept.Add("seen");
            var third = new Stage();
            var again = new ScenarioRunner(def, third, kept.Contains);
            again.Raise(TriggerKind.Start);
            for (now = 0f; now < 5f; now += 0.25f) again.Tick(now);
            TestAssert.Eq(third.Log.Count, 0, "a flag the campaign kept counts for 'unless'");

            TestAssert.That(ScenarioRunner.ReadSeconds("Hi.") >= 2.5f && ScenarioRunner.ReadSeconds(new string('x', 400)) <= 11f &&
                ScenarioRunner.ReadSeconds("A line of ordinary length, about this long.", 2f) < ScenarioRunner.ReadSeconds("A line of ordinary length, about this long."),
                "a line is held long enough to read, never forever, and for less time at a higher text speed");
        }

        private static void KeepsProgress()
        {
            var progress = ScenarioProgress.Parse("done one\nflag saga seen\nnonsense here now\ndone Not-An-Id\n\nflag saga");
            TestAssert.That(progress.IsDone("one") && progress.Has("saga", "seen") && !progress.IsDone("Not-An-Id"), "saved facts are read; bad lines are dropped");
            TestAssert.That(progress.MarkDone("two") && !progress.MarkDone("two") && progress.Set("saga", "x") && !progress.Set("saga", "x"),
                "marking is news once");
            TestAssert.Eq(progress.Format(), "done one\ndone two\nflag saga seen\nflag saga x\n", "progress is saved one fact a line, in a stable order");
            TestAssert.Eq(ScenarioProgress.Parse(progress.Format()).Format(), progress.Format(), "saving and reading back changes nothing");

            ScenarioDef Chapter(string id, string campaign, int number) =>
                ScenarioParser.Parse(id, "scenario " + id + "\ntitle \"" + id + "\"\nbase \"B\"\n" + (campaign == null ? "" : "campaign " + campaign + " " + number));
            var all = new[] { Chapter("one", "saga", 1), Chapter("two", "saga", 2), Chapter("three", "saga", 3), Chapter("solo", null, 0), Chapter("other", "else", 2) };
            var fresh = ScenarioProgress.Parse(null);
            TestAssert.That(fresh.Unlocked(all[0], all) && !fresh.Unlocked(all[1], all) && !fresh.Unlocked(all[2], all) && fresh.Unlocked(all[3], all),
                "a campaign opens with its first chapter; a script outside a campaign is always open");
            TestAssert.That(fresh.Unlocked(all[4], all), "a chapter with no earlier chapter installed is open");
            fresh.MarkDone("one");
            TestAssert.That(fresh.Unlocked(all[1], all) && !fresh.Unlocked(all[2], all), "finishing a chapter opens the next one only");
        }

        /// <summary>Every script that ships must read cleanly; names from the base mission are checked by the patch probe.</summary>
        private static void BuiltInScriptsAreSound()
        {
            string folder = Path.Combine(RepoRoot(), "modules", "Scenario", "Assets");
            string[] files = Directory.GetFiles(folder, "*.scenario");
            TestAssert.That(files.Length > 0, "the built-in mission scripts are in " + folder);
            foreach (string file in files)
            {
                ScenarioDef def = ScenarioParser.Parse(Path.GetFileName(file), File.ReadAllText(file));
                TestAssert.Eq(string.Join(" / ", def.Errors), "", "built-in script reads without complaint: " + Path.GetFileName(file));
                TestAssert.That(def.About.Length > 0 && def.Beats.Count > 0, "a built-in script tells a story: " + def.Id);
                var started = new HashSet<string>(def.Begin);
                foreach (ScenarioObjective objective in def.Objectives)
                    foreach (ScenarioThen then in objective.Then)
                        if (then.Kind == ThenKind.Start) started.UnionWith(then.Names);
                foreach (ScenarioObjective objective in def.Objectives)
                    TestAssert.That(objective.Kind == ObjectiveKind.Existing || started.Contains(objective.Id),
                        def.Id + ": objective '" + objective.Id + "' is never started");
                var set = new HashSet<string>();
                foreach (ScenarioBeat beat in def.Beats)
                    foreach (ScenarioAction action in beat.Actions)
                        if (action.Kind == ActionKind.Flag || action.Kind == ActionKind.Remember) set.Add(action.Name);
                foreach (ScenarioBeat beat in def.Beats)
                    foreach (string flag in new[] { beat.If, beat.Unless, beat.Trigger == TriggerKind.Flag ? beat.Arg : null })
                        TestAssert.That(flag == null || set.Contains(flag), def.Id + ": beat '" + beat.Id + "' waits on a flag nothing sets: " + flag);
            }
        }

        private static string RepoRoot()
        {
            for (DirectoryInfo at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent)
                if (File.Exists(Path.Combine(at.FullName, "BoscaliSummer.csproj"))) return at.FullName;
            throw new DirectoryNotFoundException("repository root not found above " + AppContext.BaseDirectory);
        }
    }
}
