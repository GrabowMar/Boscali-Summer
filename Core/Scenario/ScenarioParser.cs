using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BoscaliSummer.Core.Scenario
{
    /// <summary>
    /// Reads a mission script (docs/SCENARIOS.md). One statement a line: a keyword, its arguments in order, then <c>name=value</c> options; words with spaces go in
    /// double quotes. A line that cannot be read is skipped and named in <see cref="ScenarioDef.Errors"/>, so one typo never costs the rest of a script. Pure.
    /// </summary>
    internal static class ScenarioParser
    {
        public const int MaxTextChars = 400;

        private struct Tok
        {
            public string Text;
            public bool Quoted;
        }

        /// <summary>One statement: positional arguments (0 is the keyword) and options, read left to right.</summary>
        private sealed class Row
        {
            public readonly List<Tok> Args = new List<Tok>();
            public readonly Dictionary<string, string> Opts = new Dictionary<string, string>(StringComparer.Ordinal);
            public int At = 1;

            public bool More => At < Args.Count;
            public string Next() => More ? Args[At++].Text : null;

            public bool Quoted(out string text)
            {
                text = null;
                if (!More || !Args[At].Quoted) return false;
                text = Args[At++].Text;
                return true;
            }

            public bool Word(string word)
            {
                if (!More || Args[At].Quoted || !string.Equals(Args[At].Text, word, StringComparison.OrdinalIgnoreCase)) return false;
                At++;
                return true;
            }

            public bool Number(out float value)
            {
                value = 0f;
                if (!More || Args[At].Quoted || !Float(Args[At].Text, out value)) return false;
                At++;
                return true;
            }

            public string[] Rest()
            {
                var rest = new string[Args.Count - At];
                for (int i = 0; i < rest.Length; i++) rest[i] = Args[At + i].Text;
                At = Args.Count;
                return rest;
            }

            /// <summary>Reads a number option into <paramref name="value"/> when the row has it; false on a value that is not a number.</summary>
            public bool Opt(string key, ref float value)
            {
                if (!Opts.TryGetValue(key, out string text)) return true;
                Opts.Remove(key);
                return Float(text, out value);
            }

            public string Opt(string key)
            {
                if (!Opts.TryGetValue(key, out string text)) return null;
                Opts.Remove(key);
                return text;
            }
        }

        public static ScenarioDef Parse(string source, string text)
        {
            var def = new ScenarioDef { Source = source };
            var about = new StringBuilder();
            ScenarioObjective objective = null;
            ScenarioBeat beat = null;
            bool inAbout = false;
            string[] rows = (text ?? "").Replace("\r", "").Split('\n');
            for (int n = 0; n < rows.Length; n++)
            {
                string raw = rows[n].Trim();
                if (raw.Length == 0 || raw[0] == '#') continue;
                int line = n + 1;
                if (raw[0] == '|')
                {
                    if (!inAbout) { def.Error(line, "a '|' text line belongs under 'about'"); continue; }
                    string words = raw.Substring(1).Trim();
                    if (words.Length == 0) about.Append("\n\n");
                    else about.Append(about.Length == 0 || about[about.Length - 1] == '\n' ? "" : " ").Append(words);
                    continue;
                }
                inAbout = false;
                Row row = Read(raw, out string error);
                if (row == null) { def.Error(line, error); continue; }
                string keyword = row.Args[0].Text.ToLowerInvariant();
                error = null;
                switch (keyword)
                {
                    case "cut": case "card": case "say": case "hint": case "wait":
                    case "flag": case "unflag": case "remember": case "finish":
                    case "dip": case "sky": case "clock": case "music": case "enlist": case "choose": case "seat":
                    case "start": case "complete": case "cancel": case "countdown": case "ace": case "random":
                        if (beat == null) error = "'" + keyword + "' belongs under a 'beat'";
                        else error = ReadAction(keyword, row, beat);
                        break;
                    case "then":
                        if (objective == null) error = "'then' belongs under an 'objective' or 'on'";
                        else error = ReadThen(row, objective, line);
                        break;
                    default:
                        objective = null;
                        beat = null;
                        error = ReadStatement(keyword, row, def, line, ref objective, ref beat, ref inAbout);
                        break;
                }
                if (error == null && row.More) error = "unexpected '" + row.Args[row.At].Text + "'";
                if (error == null)
                    foreach (string key in row.Opts.Keys) { error = "unknown option '" + key + "'"; break; }
                if (error != null) def.Error(line, error);
            }
            def.About = about.ToString().Trim();
            Check(def);
            return def;
        }

        private static string ReadStatement(string keyword, Row row, ScenarioDef def, int line,
            ref ScenarioObjective objective, ref ScenarioBeat beat, ref bool inAbout)
        {
            switch (keyword)
            {
                case "scenario":
                    def.Id = row.Next();
                    return ScenarioDef.IsId(def.Id) ? null : "a scenario id is lower-case letters, digits and '-'";
                case "title": def.Title = row.Next(); return Need(def.Title, "a title");
                case "base": def.Base = row.Next(); return Need(def.Base, "the name of the mission to copy");
                case "group": def.Group = row.Next(); return Need(def.Group, "a group name");
                case "campaign":
                    def.Campaign = row.Next();
                    if (!ScenarioDef.IsId(def.Campaign)) return "a campaign id is lower-case letters, digits and '-'";
                    if (!row.Number(out float chapter) || chapter < 1f || chapter != (int)chapter) return "campaign needs a chapter number, 1 or more";
                    def.Chapter = (int)chapter;
                    return null;
                case "without": def.Without.AddRange(row.Rest()); return def.Without.Count > 0 ? null : "without needs module ids";
                case "hold": def.Hold.AddRange(row.Rest()); return def.Hold.Count > 0 ? null : "hold needs objective names";
                case "begin": def.Begin.AddRange(row.Rest()); return def.Begin.Count > 0 ? null : "begin needs objective names";
                case "about": inAbout = true; return null;
                case "lock":
                    if (!Side(row.Next(), out ScenarioSide locked) || locked == ScenarioSide.Any) return "lock needs a side: bdf, pala, pmc or pirates";
                    def.Locked.Add(locked);
                    return null;
                case "peace":
                    if (!Side(row.Next(), out ScenarioSide one) || one == ScenarioSide.Any || !row.More)
                        return "peace needs a side and the sides it is at peace with: peace pmc bdf pala";
                    // A line with a mistake is left out whole.
                    var pairs = new List<KeyValuePair<ScenarioSide, ScenarioSide>>();
                    while (row.More)
                    {
                        if (!Side(row.Next(), out ScenarioSide other) || other == ScenarioSide.Any || other == one)
                            return "peace names sides (bdf, pala, pmc, pirates), each other than the first";
                        pairs.Add(new KeyValuePair<ScenarioSide, ScenarioSide>(one, other));
                    }
                    def.Peace.AddRange(pairs);
                    return null;
                case "contract":
                    if (!Side(row.Next(), out def.Contract) || def.Contract == ScenarioSide.Any || def.Contract == ScenarioSide.Pmc)
                        return "contract needs the employer: bdf or pala";
                    return null;
                case "time": return row.Number(out def.Time) && def.Time >= 0f && def.Time < 24f ? null : "time is an hour of the day, 0 to 24";
                case "weather": return row.Number(out def.Weather) && def.Weather >= 0f && def.Weather <= 1f ? null : "weather is 0 (clear) to 1 (storm)";
                case "ending": return row.Number(out def.Ending) && def.Ending >= 1f && def.Ending <= 120f ? null : "ending is 1 to 120 seconds";
                case "gamemusic":
                    if (!row.Word("off")) return "gamemusic needs: off";
                    def.GameMusic = false;
                    return null;
                case "cast": return ReadCast(row, def);
                case "unit": return ReadUnit(row, def, line);
                case "copy":
                    var copy = new ScenarioCopy { Outcome = row.Next(), Line = line };
                    if (string.IsNullOrEmpty(copy.Outcome) || !row.Word("as") || string.IsNullOrEmpty(copy.Prefix = row.Next()))
                        return "copy needs: <a spawn outcome of the base mission> as <name for the copies>";
                    string without = row.Opt("without");
                    if (without != null) copy.Without = without.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    def.Copies.Add(copy);
                    return null;
                case "picture":
                    var picture = new ScenarioPicture { Id = row.Next() };
                    if (!ScenarioDef.IsId(picture.Id)) return "picture needs an id (lower-case letters, digits, '-')";
                    if (row.Word("poster")) picture.Poster = row.Next();
                    else if (row.Word("file")) picture.File = row.Next();
                    if (!IsFileName(picture.Poster ?? picture.File)) return "picture needs: poster <event picture name>, or file <name.png>";
                    if (def.Pictures.ContainsKey(picture.Id)) return "picture '" + picture.Id + "' is written twice";
                    def.Pictures[picture.Id] = picture;
                    return null;
                case "shot": return ReadShot(row, def, line);
                case "objective":
                case "on":
                    objective = new ScenarioObjective { Line = line };
                    string error = keyword == "on" ? ReadOn(row, objective) : ReadObjective(row, objective);
                    if (error != null) { objective = null; return error; }
                    def.Objectives.Add(objective);
                    return null;
                case "beat":
                    beat = new ScenarioBeat { Line = line };
                    error = ReadBeat(row, beat);
                    if (error != null) { beat = null; return error; }
                    def.Beats.Add(beat);
                    return null;
                default: return "unknown statement '" + keyword + "'";
            }
        }

        private static string ReadCast(Row row, ScenarioDef def)
        {
            var cast = new ScenarioCast { Id = row.Next() };
            if (!ScenarioDef.IsId(cast.Id)) return "cast needs an id (lower-case letters, digits, '-')";
            if (!row.More || row.Args[row.At].Quoted) return "cast needs a net: command, awacs, tower, wing or enemy";
            cast.Net = row.Next().ToLowerInvariant();
            if (Array.IndexOf(new[] { "command", "awacs", "tower", "wing", "enemy" }, cast.Net) < 0)
                return "unknown net '" + cast.Net + "': command, awacs, tower, wing or enemy";
            if (!row.Quoted(out cast.Callsign) || cast.Callsign.Length == 0) return "cast needs a \"CALLSIGN\"";
            row.Quoted(out cast.Role);
            if (def.Cast.ContainsKey(cast.Id)) return "cast '" + cast.Id + "' is written twice";
            def.Cast[cast.Id] = cast;
            return null;
        }

        private static string ReadUnit(Row row, ScenarioDef def, int line)
        {
            var unit = new ScenarioUnit { Name = row.Next(), Line = line };
            if (string.IsNullOrEmpty(unit.Name)) return "unit needs a name";
            if (row.Word("new"))
            {
                unit.Kind = (row.Next() ?? "").ToLowerInvariant();
                unit.Type = row.Next();
                if (unit.Kind != "aircraft" && unit.Kind != "ship" && unit.Kind != "vehicle" || string.IsNullOrEmpty(unit.Type))
                    return "a new unit needs: new aircraft|ship|vehicle <type>";
            }
            else if (!row.Word("like") || (unit.Like = row.Next()) == null) return "unit needs 'like <a unit of the base mission>' or 'new <kind> <type>'";
            if (!row.Word("at") || !row.Number(out unit.X) || !row.Number(out unit.Y) || !row.Number(out unit.Z))
                return "unit needs 'at <x> <height> <z>'";
            if (!row.Opt("heading", ref unit.Heading) || !row.Opt("speed", ref unit.Speed)) return "heading and speed are numbers";
            string side = row.Opt("side");
            if (side != null && !Side(side, out unit.Side)) return "side is bdf, pala, pmc or pirates";
            if (unit.Kind != null && unit.Side == ScenarioSide.Any) return "a new unit needs a side: side=bdf, pala, pmc or pirates";
            string loadout = row.Opt("loadout");
            if (loadout != null) unit.Loadout = loadout.Split('+');
            string sail = row.Opt("to");
            if (sail != null)
            {
                string[] point = sail.Split(',');
                if (point.Length != 2 || !Float(point[0], out unit.SailX) || !Float(point[1], out unit.SailZ)) return "to is a map point: to=x,z";
                unit.Sails = true;
            }
            foreach (KeyValuePair<string, string> other in row.Opts)
            {
                if (!Float(other.Value, out float value)) return "'" + other.Key + "' needs a number";
                unit.Sets.Add(new KeyValuePair<string, float>(other.Key, value));
            }
            row.Opts.Clear();
            def.Units.Add(unit);
            return null;
        }

        private static string ReadOn(Row row, ScenarioObjective objective)
        {
            objective.Kind = ObjectiveKind.Existing;
            objective.Id = row.Next();
            return Need(objective.Id, "the name of an objective of the base mission");
        }

        private static string ReadObjective(Row row, ScenarioObjective objective)
        {
            objective.Id = row.Next();
            if (string.IsNullOrEmpty(objective.Id)) return "objective needs a name";
            bool sided = row.More && !row.Args[row.At].Quoted && Side(row.Args[row.At].Text, out objective.Side);
            if (sided) row.At++;
            row.Quoted(out objective.Display);
            string kind = (row.Next() ?? "").ToLowerInvariant();
            switch (kind)
            {
                case "wait":
                    objective.Kind = ObjectiveKind.Wait;
                    return row.Number(out objective.Seconds) && objective.Seconds > 0f ? null : "wait needs seconds";
                case "reach":
                    objective.Kind = ObjectiveKind.Reach;
                    if (!row.Number(out objective.X) || !row.Number(out objective.Y) || !row.Number(out objective.Z)) return "reach needs <x> <height> <z>";
                    if (!row.Opt("range", ref objective.Range) || objective.Range <= 0f) return "range is metres";
                    return objective.Side == ScenarioSide.Any ? "a reach objective needs a side: bdf, pala or pmc" : null;
                case "destroy": objective.Kind = ObjectiveKind.Destroy; break;
                case "capture":
                    objective.Kind = ObjectiveKind.Capture;
                    if (objective.Side == ScenarioSide.Any) return "a capture objective needs a side: bdf, pala or pmc";
                    break;
                case "after": objective.Kind = ObjectiveKind.After; break;
                case "sortie":
                    objective.Kind = ObjectiveKind.Sortie;
                    return row.Number(out objective.Score) && objective.Score > 0f ? null : "sortie needs a score above 0";
                default: return "objective needs a kind: wait, reach, destroy, capture, after or sortie";
            }
            objective.Targets = row.Rest();
            if (objective.Targets.Length == 0) return kind + " needs names";
            string need = row.Opt("need");
            if (need == null) return null;
            need = need.ToLowerInvariant();
            if (need == "any" || need == "all" || need == "order") { objective.Need = need; return null; }
            if (need.EndsWith("%", StringComparison.Ordinal) && Float(need.Substring(0, need.Length - 1), out float percent) && percent > 0f && percent <= 100f)
            {
                objective.Need = "some";
                objective.Percent = percent / 100f;
                return null;
            }
            return "need is any, all, order or a percentage like 60%";
        }

        private static string ReadThen(Row row, ScenarioObjective objective, int line)
        {
            var then = new ScenarioThen { Line = line };
            string kind = (row.Next() ?? "").ToLowerInvariant();
            switch (kind)
            {
                case "start": then.Kind = ThenKind.Start; break;
                case "stop": then.Kind = ThenKind.Stop; break;
                case "finish": then.Kind = ThenKind.Finish; break;
                case "spawn": then.Kind = ThenKind.Spawn; break;
                case "victory":
                    then.Kind = ThenKind.Victory;
                    if (row.More && (!row.Number(out then.Seconds) || then.Seconds < 0f || then.Seconds > 120f)) return "victory takes the seconds before the end screen, 0 to 120";
                    objective.Then.Add(then);
                    return null;
                case "message":
                    then.Kind = ThenKind.Message;
                    if (!row.Quoted(out then.Text) || !Words(then.Text)) return "message needs \"words\", up to " + MaxTextChars + " characters";
                    then.SideOnly = row.Word("side-only");
                    objective.Then.Add(then);
                    return null;
                default: return "then needs start, stop, finish, spawn, message or victory";
            }
            then.Names = row.Rest();
            if (then.Names.Length == 0) return "then " + kind + " needs names";
            objective.Then.Add(then);
            return null;
        }

        private static readonly string[] BoardNames = { "feed", "clean", "title", "news", "paper", "map", "sat", "dossier", "chapter", "screen" };

        /// <summary>The words a cut may give its board; each board reads the ones it uses (docs/SCENARIOS.md).</summary>
        private static readonly string[] BoardWords =
        {
            "channel", "kicker", "headline", "sub", "ticker", "tag", "clock", "date", "masthead", "edition", "body", "number", "company", "heading",
            "mark", "note", "area", "arrows", "retreat", "front", "labels", "marks", "header", "legend", "clip",
        };

        private static readonly string[] EffectNames = { "glitch", "strike", "lock", "lost", "alert", "warm", "cold", "night" };

        /// <summary>A comma list of effect names, in <see cref="CutFx"/> order.</summary>
        private static bool Effects(string list, out CutFx fx)
        {
            fx = CutFx.None;
            foreach (string name in list.Split(','))
            {
                int index = Array.IndexOf(EffectNames, name.Trim());
                if (index < 0) return false;
                fx |= (CutFx)(1 << index);
            }
            return true;
        }

        private static string ReadShot(Row row, ScenarioDef def, int line)
        {
            var shot = new ScenarioShot { Id = row.Next(), Line = line };
            if (!ScenarioDef.IsId(shot.Id)) return "shot needs an id (lower-case letters, digits, '-')";
            if (row.Word("orbit"))
            {
                if (row.Word("airbase")) { shot.Kind = ShotKind.OrbitAirbase; shot.Target = row.Next(); }
                else if (row.Word("unit")) { shot.Kind = ShotKind.OrbitUnit; shot.Target = row.Next(); }
                else if (row.Word("at") && row.Number(out shot.X) && row.Number(out shot.Y) && row.Number(out shot.Z)) shot.Kind = ShotKind.OrbitPoint;
                else return "orbit needs: airbase \"Name\", unit <name> or at <x> <height> <z>";
                if (shot.Kind != ShotKind.OrbitPoint && string.IsNullOrEmpty(shot.Target)) return "orbit needs the name to circle";
                if (!row.Opt("radius", ref shot.Radius) || !row.Opt("height", ref shot.Height) || !row.Opt("from", ref shot.From) || !row.Opt("to", ref shot.To))
                    return "radius, height, from and to are numbers";
                if (shot.Radius < 20f || shot.Radius > 20000f) return "radius is 20 to 20000 metres";
            }
            else if (row.Word("fly"))
            {
                shot.Kind = ShotKind.Fly;
                if (!row.Number(out shot.X) || !row.Number(out shot.Y) || !row.Number(out shot.Z) || !row.Word("to") ||
                    !row.Number(out shot.ToX) || !row.Number(out shot.ToY) || !row.Number(out shot.ToZ) || !row.Word("look") ||
                    !row.Number(out shot.LookX) || !row.Number(out shot.LookY) || !row.Number(out shot.LookZ))
                    return "fly needs: <x> <height> <z> to <x> <height> <z> look <x> <height> <z>";
            }
            else if (row.Word("chase"))
            {
                // Rides with a unit, in the unit's own frame: X to its right, Y above it, Z ahead of it.
                shot.Kind = ShotKind.Chase;
                float back = 30f;
                shot.Y = 5f;
                if (!row.Word("unit") || string.IsNullOrEmpty(shot.Target = row.Next())) return "chase needs: unit <name>";
                if (!row.Opt("back", ref back) || !row.Opt("up", ref shot.Y) || !row.Opt("right", ref shot.X)) return "back, up and right are metres";
                shot.Z = -back;
                if (System.Math.Abs(shot.X) + System.Math.Abs(shot.Y) + System.Math.Abs(shot.Z) < 3f) return "a chase camera stands at least 3 metres off its unit";
            }
            else return "shot needs a move: orbit, fly or chase";
            if (!row.Opt("seconds", ref shot.Seconds) || !row.Opt("fov", ref shot.Fov)) return "seconds and fov are numbers";
            if (shot.Seconds < 1f || shot.Seconds > 60f) return "a shot lasts 1 to 60 seconds";
            if (shot.Fov < 15f || shot.Fov > 80f) return "fov is 15 to 80 degrees";
            if (!row.Opt("shake", ref shot.Shake) || !row.Opt("roll", ref shot.Roll)) return "shake and roll are numbers";
            if (shot.Shake < 0f || shot.Shake > 1f) return "shake is 0 to 1";
            if (shot.Roll < -20f || shot.Roll > 20f) return "roll is -20 to 20 degrees";
            if (def.Shots.ContainsKey(shot.Id)) return "shot '" + shot.Id + "' is written twice";
            def.Shots[shot.Id] = shot;
            return null;
        }

        private static string ReadBeat(Row row, ScenarioBeat beat)
        {
            beat.Id = row.Next();
            if (!ScenarioDef.IsId(beat.Id)) return "beat needs an id (lower-case letters, digits, '-')";
            if (!row.Word("when")) return "beat needs 'when <trigger>'";
            string trigger = (row.Next() ?? "").ToLowerInvariant();
            switch (trigger)
            {
                case "start": beat.Trigger = TriggerKind.Start; break;
                case "join": beat.Trigger = TriggerKind.Join; break;
                case "spawn": beat.Trigger = TriggerKind.Spawn; break;
                case "airborne": beat.Trigger = TriggerKind.Airborne; break;
                case "landed": beat.Trigger = TriggerKind.Landed; break;
                case "time":
                    beat.Trigger = TriggerKind.Time;
                    if (!row.Number(out beat.Seconds) || beat.Seconds < 0f) return "time needs seconds";
                    break;
                case "started": beat.Trigger = TriggerKind.Started; beat.Arg = row.Next(); break;
                case "done": beat.Trigger = TriggerKind.Done; beat.Arg = row.Next(); break;
                case "flag": beat.Trigger = TriggerKind.Flag; beat.Arg = row.Next(); break;
                case "destroyed": beat.Trigger = TriggerKind.Destroyed; beat.Arg = row.Next(); break;
                case "damaged":
                    beat.Trigger = TriggerKind.Damaged;
                    beat.Arg = row.Next();
                    string share = row.Next() ?? "";
                    if (!Float(share.TrimEnd('%'), out beat.Seconds) || beat.Seconds <= 0f || beat.Seconds >= 100f) return "damaged needs a unit and the health left, 1 to 99 (percent)";
                    beat.Seconds /= 100f;
                    break;
                case "near":
                    beat.Trigger = TriggerKind.Near;
                    if (!row.Number(out beat.X) || !row.Number(out beat.Z)) return "near needs <x> <z>";
                    if (!row.Opt("range", ref beat.Range) || beat.Range <= 0f) return "range is metres, more than 0";
                    break;
                case "below":
                case "above":
                    beat.Trigger = trigger == "below" ? TriggerKind.Below : TriggerKind.Above;
                    if (!row.Number(out beat.Seconds) || beat.Seconds < 0f) return trigger + " needs metres above the ground";
                    break;
                case "down": beat.Trigger = TriggerKind.Down; break;
                default: return "unknown trigger '" + trigger + "': start, join, spawn, airborne, landed, time, started, done, flag, destroyed, damaged, near, below, above or down";
            }
            if ((beat.Trigger == TriggerKind.Started || beat.Trigger == TriggerKind.Done || beat.Trigger == TriggerKind.Flag ||
                beat.Trigger == TriggerKind.Destroyed || beat.Trigger == TriggerKind.Damaged) && string.IsNullOrEmpty(beat.Arg))
                return trigger + " needs a name";
            beat.Repeat = row.Word("repeat");
            string side = row.Opt("side");
            if (side != null && !Side(side, out beat.Side)) return "side is bdf, pala, pmc or both";
            beat.If = row.Opt("if");
            beat.Unless = row.Opt("unless");
            return null;
        }

        private static string ReadAction(string keyword, Row row, ScenarioBeat beat)
        {
            var action = new ScenarioAction();
            switch (keyword)
            {
                case "cut":
                    action.Kind = ActionKind.Cut;
                    action.Name = row.Next();
                    if (string.IsNullOrEmpty(action.Name)) return "cut needs a shot";
                    if (row.Quoted(out action.Text) && !Words(action.Text)) return "a caption is up to " + MaxTextChars + " characters";
                    // Two quoted texts: a large title and the line under it.
                    if (row.Quoted(out string under))
                    {
                        if (!Words(under)) return "a caption is up to " + MaxTextChars + " characters";
                        action.Title = action.Text;
                        action.Text = under;
                    }
                    action.Photo = row.Opt("photo");
                    action.Stamp = row.Opt("stamp");
                    if (action.Stamp != null && action.Stamp.Length > 80) return "a stamp is up to 80 characters";
                    string fx = row.Opt("fx");
                    if (fx != null && !Effects(fx, out action.Fx)) return "fx is a list of: " + string.Join(", ", EffectNames);
                    action.Voice = row.Opt("voice");
                    if (action.Voice != null && !IsFileName(action.Voice)) return "voice needs a file name (an .mp3 or .ogg beside the script)";
                    action.Sfx = row.Opt("sfx");
                    if (action.Sfx != null && !EffectList(action.Sfx)) return "sfx is a list of file.mp3@seconds, split by ','";
                    action.Board = row.Opt("board");
                    if (action.Board != null && Array.IndexOf(BoardNames, action.Board) < 0) return "board is one of: " + string.Join(", ", BoardNames);
                    string clipName = row.Opts.TryGetValue("clip", out string named) ? named : null;
                    if (action.Board == "screen" && !IsFileName(clipName)) return "a screen board needs clip=<a .webm (VP8) beside the script>";
                    foreach (string key in BoardWords)
                    {
                        string word = row.Opt(key);
                        if (word == null) continue;
                        if (!Words(word)) return key + " is up to " + MaxTextChars + " characters";
                        if (action.Words == null) action.Words = new Dictionary<string, string>(StringComparer.Ordinal);
                        action.Words[key] = word;
                    }
                    break;
                case "card":
                    action.Kind = ActionKind.Card;
                    action.Seconds = 4f;
                    if (!row.Quoted(out action.Name) || !Words(action.Name)) return "card needs a \"TITLE\"";
                    if (row.Quoted(out action.Text) && !Words(action.Text)) return "a card's second line is up to " + MaxTextChars + " characters";
                    if (!row.Opt("seconds", ref action.Seconds) || action.Seconds < 0.5f || action.Seconds > 30f) return "seconds is 0.5 to 30";
                    break;
                case "say":
                    action.Kind = ActionKind.Say;
                    action.Name = row.Next();
                    if (string.IsNullOrEmpty(action.Name)) return "say needs a speaker from the cast";
                    if (!row.Quoted(out action.Text) || !Words(action.Text)) return "say needs \"words\", up to " + MaxTextChars + " characters";
                    action.Voice = row.Opt("voice");
                    if (action.Voice != null && !IsFileName(action.Voice)) return "voice needs a file name (an .mp3 or .ogg beside the script)";
                    break;
                case "hint":
                    action.Kind = ActionKind.Hint;
                    action.Seconds = 12f;
                    if (!row.Quoted(out action.Text) || !Words(action.Text)) return "hint needs \"words\", up to " + MaxTextChars + " characters";
                    if (!row.Opt("seconds", ref action.Seconds) || action.Seconds < 1f || action.Seconds > 120f) return "seconds is 1 to 120";
                    break;
                case "wait":
                    action.Kind = ActionKind.Wait;
                    if (!row.Number(out action.Seconds) || action.Seconds < 0f || action.Seconds > 3600f) return "wait needs seconds, up to 3600";
                    break;
                case "finish": action.Kind = ActionKind.Finish; break;
                case "dip": action.Kind = ActionKind.Dip; break;
                case "choose": action.Kind = ActionKind.Choose; break;
                case "seat":
                    action.Kind = ActionKind.Seat;
                    action.Name = row.Next();
                    action.Speed = 120f;
                    if (string.IsNullOrEmpty(action.Name) || !row.Word("at") || !row.Number(out action.X) || !row.Number(out action.Y) || !row.Number(out action.Z))
                        return "seat needs: \"Aircraft name\" at <x> <height> <z>";
                    if (!row.Opt("heading", ref action.Heading) || !row.Opt("speed", ref action.Speed)) return "heading and speed are numbers";
                    break;
                case "enlist":
                    action.Kind = ActionKind.Enlist;
                    action.Name = (row.Next() ?? "").ToLowerInvariant();
                    if (!Side(action.Name, out ScenarioSide enlisted) || enlisted == ScenarioSide.Any || enlisted == ScenarioSide.Pirates)
                        return "enlist needs a side: bdf, pala or pmc";
                    break;
                case "sky":
                    action.Kind = ActionKind.Sky;
                    action.Name = (row.Next() ?? "").ToLowerInvariant();
                    if (Array.IndexOf(new[] { "storm", "hurricane", "front", "fog", "wave", "clear" }, action.Name) < 0)
                        return "sky is storm, hurricane, front, fog, wave or clear";
                    break;
                case "music":
                    action.Kind = ActionKind.Music;
                    action.Seconds = 0.85f;
                    if (row.Word("stop")) break;
                    action.Name = row.Next();
                    if (!IsFileName(action.Name)) return "music needs a file name (an .mp3 or .ogg beside the script), or 'stop'";
                    if (!row.Opt("volume", ref action.Seconds) || action.Seconds < 0f || action.Seconds > 1f) return "volume is 0 to 1";
                    // loop: the track starts again until 'music stop', for a cutscene longer than it.
                    if (row.Word("loop")) action.Text = "loop";
                    break;
                case "start":
                case "complete":
                case "cancel":
                    action.Kind = keyword == "start" ? ActionKind.Start : keyword == "complete" ? ActionKind.Complete : ActionKind.Cancel;
                    action.Names = row.Rest();
                    if (action.Names.Length == 0) return keyword + " needs objective names";
                    break;
                case "countdown":
                    action.Kind = ActionKind.Countdown;
                    if (row.Word("stop")) break;
                    if (!row.Number(out action.Seconds) || action.Seconds < 1f || action.Seconds > 7200f) return "countdown needs seconds (1 to 7200) and a \"LABEL\", or 'stop'";
                    if (!row.Quoted(out action.Text) || action.Text.Length == 0 || action.Text.Length > 40) return "countdown needs a \"LABEL\", up to 40 characters";
                    action.Name = row.Opt("flag");
                    if (action.Name != null && !ScenarioDef.IsId(action.Name)) return "flag is a flag name (lower-case letters, digits, '-')";
                    break;
                case "ace":
                    action.Kind = ActionKind.Ace;
                    if (row.Word("off")) break;
                    action.Name = row.Next();
                    if (string.IsNullOrEmpty(action.Name) || !row.Quoted(out action.Title) || action.Title.Length == 0 || action.Title.Length > 40)
                        return "ace needs a unit and its \"CALLSIGN\" (up to 40 characters), or 'off'";
                    if (row.Quoted(out action.Text) && action.Text.Length > 80) return "an ace's line is up to 80 characters";
                    break;
                case "random":
                    action.Kind = ActionKind.Random;
                    action.Names = row.Rest();
                    if (action.Names.Length < 2) return "random needs two flag names or more";
                    foreach (string flag in action.Names)
                        if (!ScenarioDef.IsId(flag)) return "random needs flag names (lower-case letters, digits, '-')";
                    break;
                case "clock":
                    action.Kind = ActionKind.Clock;
                    if (!row.Number(out action.Seconds) || action.Seconds < 0f || action.Seconds >= 24f) return "clock is an hour of the day, 0 to 24";
                    break;
                default:
                    action.Kind = keyword == "flag" ? ActionKind.Flag : keyword == "unflag" ? ActionKind.Unflag : ActionKind.Remember;
                    action.Name = row.Next();
                    if (!ScenarioDef.IsId(action.Name)) return keyword + " needs a flag name (lower-case letters, digits, '-')";
                    break;
            }
            beat.Actions.Add(action);
            return null;
        }

        /// <summary>What a script gets wrong inside itself. Names of the base mission's objectives and units are checked when the mission is built.</summary>
        private static void Check(ScenarioDef def)
        {
            if (def.Id == null) def.Error(1, "the script needs 'scenario <id>'");
            if (string.IsNullOrEmpty(def.Title)) def.Error(1, "the script needs 'title \"Name in the mission list\"'");
            if (string.IsNullOrEmpty(def.Base)) def.Error(1, "the script needs 'base \"Mission to copy\"'");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ScenarioUnit unit in def.Units)
                if (!names.Add(unit.Name)) def.Error(unit.Line, "unit '" + unit.Name + "' is written twice");
            names.Clear();
            foreach (ScenarioObjective objective in def.Objectives)
                if (objective.Kind != ObjectiveKind.Existing && !names.Add(objective.Id))
                    def.Error(objective.Line, "objective '" + objective.Id + "' is written twice");
            names.Clear();
            foreach (ScenarioBeat beat in def.Beats)
            {
                if (!names.Add(beat.Id)) def.Error(beat.Line, "beat '" + beat.Id + "' is written twice");
                if (beat.Actions.Count == 0) def.Error(beat.Line, "beat '" + beat.Id + "' does nothing");
                foreach (ScenarioAction action in beat.Actions)
                {
                    if (action.Kind == ActionKind.Say && !def.Cast.ContainsKey(action.Name))
                        def.Error(beat.Line, "beat '" + beat.Id + "': nobody in the cast is called '" + action.Name + "'");
                    if (action.Kind == ActionKind.Cut && !def.Shots.ContainsKey(action.Name))
                        def.Error(beat.Line, "beat '" + beat.Id + "': there is no shot '" + action.Name + "'");
                    if (action.Kind == ActionKind.Cut && action.Photo != null && !def.Pictures.ContainsKey(action.Photo))
                        def.Error(beat.Line, "beat '" + beat.Id + "': there is no picture '" + action.Photo + "'");
                }
            }
        }

        /// <summary>Sound effects of a cut, <c>file.mp3@seconds, other.mp3@2.5</c>: each a file name and a time from 0 to 120 seconds into the shot.</summary>
        public static bool EffectList(string list)
        {
            string[] entries = (list ?? "").Split(',');
            if (entries.Length == 0 || entries.Length > 24) return false;
            foreach (string entry in entries)
            {
                int at = entry.LastIndexOf('@');
                if (at <= 0 || !IsFileName(entry.Substring(0, at).Trim()) || !Float(entry.Substring(at + 1).Trim(), out float seconds) || seconds < 0f || seconds > 120f) return false;
            }
            return true;
        }

        /// <summary>A plain file or picture name: no folders, nothing that climbs out of one.</summary>
        private static bool IsFileName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 96 || name[0] == '.') return false;
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ' ')) return false;
            return true;
        }

        private static string Need(string value, string what) => string.IsNullOrEmpty(value) ? "this needs " + what : null;

        private static bool Words(string text) => !string.IsNullOrEmpty(text) && text.Length <= MaxTextChars;

        private static bool Float(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool Side(string word, out ScenarioSide side)
        {
            side = ScenarioSide.Any;
            switch ((word ?? "").ToLowerInvariant())
            {
                case "bdf": case "boscali": side = ScenarioSide.Bdf; return true;
                case "pala": case "primeva": side = ScenarioSide.Pala; return true;
                case "pmc": side = ScenarioSide.Pmc; return true;
                case "pirates": case "pirate": side = ScenarioSide.Pirates; return true;
                case "both": case "any": return true;
                default: return false;
            }
        }

        /// <summary>Splits a line into words, "quoted words" (\" and \n inside) and name=value options (name="quoted value" too).</summary>
        private static Row Read(string raw, out string error)
        {
            error = null;
            var row = new Row();
            var word = new StringBuilder();
            int i = 0;
            while (i < raw.Length)
            {
                if (char.IsWhiteSpace(raw[i])) { i++; continue; }
                word.Length = 0;
                bool quoted = raw[i] == '"';
                while (i < raw.Length && (!char.IsWhiteSpace(raw[i])))
                {
                    if (raw[i] != '"') { word.Append(raw[i++]); continue; }
                    for (i++; ; i++)
                    {
                        if (i >= raw.Length) { error = "a quote is left open"; return null; }
                        if (raw[i] == '"') { i++; break; }
                        if (raw[i] == '\\' && i + 1 < raw.Length)
                        {
                            i++;
                            word.Append(raw[i] == 'n' ? '\n' : raw[i]);
                        }
                        else word.Append(raw[i]);
                    }
                    if (quoted) break;
                }
                string text = word.ToString();
                int equals = quoted ? -1 : text.IndexOf('=');
                if (equals > 0 && row.Args.Count > 0 && IsOptionName(text, equals))
                {
                    string key = text.Substring(0, equals).ToLowerInvariant();
                    if (row.Opts.ContainsKey(key)) { error = "option '" + key + "' is written twice"; return null; }
                    row.Opts[key] = text.Substring(equals + 1);
                }
                else row.Args.Add(new Tok { Text = text, Quoted = quoted });
            }
            if (row.Args.Count == 0 || row.Args[0].Quoted) { error = "a line starts with a keyword"; return null; }
            return row;
        }

        private static bool IsOptionName(string text, int length)
        {
            for (int i = 0; i < length; i++)
                if (!char.IsLetter(text[i])) return false;
            return true;
        }
    }
}
