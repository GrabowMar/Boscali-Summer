using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Modules.Wing.Domain;

// The chatter engine's rules and a lint of the built-in packs. Run: dotnet run --project tests/Chatter.Tests
int checks = 0;
void Check(bool condition, string label)
{
    checks++;
    if (!condition) throw new InvalidOperationException(label);
}

ChatterSpeaker Pilot(string id, string persona, string character = null) => new ChatterSpeaker("wing:" + id, ChatterNet.Wing, id.ToUpperInvariant(), persona, character);

ChatterDirector Director(string pack)
{
    var d = new ChatterDirector();
    d.Library.Load("test", pack);
    Check(d.Library.Errors.Count == 0, "test pack reads clean: " + string.Join("; ", d.Library.Errors));
    d.Reset(7);
    return d;
}

const string Pack = @"
=snack
coffee
@t.amb ambient
[*] Ambient talk that takes a while to say on the radio.
@t.status status
[*] Status one.
@t.tac tactical
[*] Tactical call.
@t.emg emergency
[*] Emergency.
@t.bag status
[veteran] One.
[veteran] Two.
[veteran] Three.
[veteran] Four.
[*] Anybody.
@t.tier status
[*] Anybody.
[ice] Persona.
[@spectre] Character.
@t.fact tactical
[*] Fox three, <target>.
[*] Fox three.
@t.need status
[*] Needs <missing>.
@t.list status
[*] I want <snack>, <lead>. <callsign> out.
@t.cond status
[* night] Dark.
[* !night] Bright.
>talk
A[veteran]: First.
B[rookie]: Second.
A: Third.
>night-talk when=night
A[*]: Night first.
B[*]: Night second.
";

// --- priority, cut, patience
{
    ChatterDirector d = Director(Pack);
    var aired = new List<ChatterAired>();
    d.Aired = aired.Add;
    ChatterSpeaker a = Pilot("a", "veteran"), b = Pilot("b", "rookie");
    Check(d.Raise(0f, "t.amb", a), "ambient accepted");
    d.Tick(0f);
    Check(aired.Count == 1 && !aired[0].Cut && d.Busy(0.5f), "ambient on air");
    Check(d.Raise(0.5f, "t.status", b), "status accepted");
    d.Tick(0.5f);
    Check(aired.Count == 1, "status waits behind a line on air");
    Check(d.Raise(0.6f, "t.tac", b), "tactical accepted");
    d.Tick(0.6f);
    Check(aired.Count == 2 && aired[1].Event == "t.tac" && aired[1].Cut, "tactical cuts ambient");
    Check(d.Raise(0.7f, "t.emg", a), "emergency accepted");
    d.Tick(0.7f);
    Check(aired.Count == 3 && aired[2].Priority == ChatterPriority.Emergency && aired[2].Cut, "emergency cuts tactical");
    d.Tick(0.8f);
    Check(aired.Count == 3 && d.Pending == 1, "status still waits");
    d.Tick(0.7f + aired[2].Seconds + ChatterDirector.GapSeconds + 0.01f);
    Check(aired.Count == 4 && aired[3].Event == "t.status", "status goes when the channel is free");
    Check(d.Log.Count == 4 && d.Log.TextAt(3) == "B: Status one.", "log holds what aired");

    Check(d.Raise(20f, "t.amb", a), "second ambient");
    d.Tick(20f);
    Check(d.Raise(20.1f, "t.status", b, "late"), "status behind it");
    d.Hold(20f, 25f);
    d.Tick(20.1f + ChatterDirector.Patience(ChatterPriority.Status) + 0.1f);
    Check(d.Pending == 0 && d.DroppedCount == 1 && aired.Count == 5, "a line that waited too long is dropped, not said late");
}

// --- repeats, dead speakers, level
{
    ChatterDirector d = Director(Pack);
    var aired = new List<ChatterAired>();
    d.Aired = aired.Add;
    ChatterSpeaker a = Pilot("a", "veteran"), dead = Pilot("dead", "rookie");
    dead.CanTalk = () => false;
    Check(d.Raise(0f, "t.tac", a, "kill:1"), "first call");
    Check(!d.Raise(0f, "t.tac", a, "kill:1"), "same key refused while waiting");
    d.Tick(0f, s => s.CanTalk == null || s.CanTalk());
    Check(!d.Raise(5f, "t.tac", a, "kill:1"), "same key refused inside the repeat window");
    Check(d.Raise(5f + ChatterDirector.RepeatSeconds, "t.tac", a, "kill:1"), "same key allowed after it");
    d.Reset(3);
    aired.Clear();
    d.Raise(0f, "t.tac", dead);
    d.Raise(0f, "t.status", a);
    d.Tick(0f, s => s.CanTalk == null || s.CanTalk());
    Check(aired.Count == 1 && aired[0].SpeakerId == a.Id, "a dead speaker is skipped and costs the channel nothing");
    Check(!d.Raise(0f, "t.nope", a) && d.Unknown.Contains("t.nope"), "unknown event refused and remembered");
    d.Level = ChatterLevel.Essential;
    Check(!d.Raise(30f, "t.amb", a) && d.Raise(30f, "t.status", a, "s2"), "Essential drops ambient only");
    d.Level = ChatterLevel.Off;
    Check(!d.Raise(40f, "t.emg", a), "Off says nothing");
}

// --- drawing lines
{
    ChatterDirector d = Director(Pack);
    var aired = new List<ChatterAired>();
    d.Aired = aired.Add;
    ChatterSpeaker vet = Pilot("v", "veteran"), ice = Pilot("i", "ice"), spectre = Pilot("s", "ice", "spectre"), none = Pilot("n", null);
    float t = 0f;
    void Air(string evt, ChatterSpeaker who, params (string, string)[] facts)
    {
        Check(d.Raise(t, evt, who, "k" + t, facts), evt + " accepted");
        d.Tick(t);
        t += 30f;
    }
    for (int i = 0; i < 8; i++) Air("t.bag", vet);
    var first = new HashSet<string>(aired.Take(4).Select(x => x.Text));
    var second = new HashSet<string>(aired.Skip(4).Take(4).Select(x => x.Text));
    Check(first.Count == 4 && second.Count == 4 && !first.Contains("Anybody."), "a persona's lines are all used before one repeats, and beat the anybody line");
    aired.Clear();
    Air("t.tier", spectre); Air("t.tier", ice); Air("t.tier", vet); Air("t.tier", none);
    Check(aired[0].Text == "Character." && aired[1].Text == "Persona." && aired[2].Text == "Anybody." && aired[3].Text == "Anybody.", "character beats persona beats anybody");
    aired.Clear();
    for (int i = 0; i < 4; i++) Air("t.fact", vet);
    Check(aired.All(x => x.Text == "Fox three."), "a line whose fact is missing is never drawn");
    aired.Clear();
    for (int i = 0; i < 4; i++) Air("t.fact", vet, ("target", "Vortex"));
    Check(aired.Any(x => x.Text == "Fox three, Vortex."), "a fact fills its token");
    Check(!d.Raise(t, "t.need", vet), "an event with no sayable line is refused");
    aired.Clear();
    d.Lead = "Boss";
    Air("t.list", vet);
    Check(aired[0].Text == "I want coffee, Boss. V out.", "token lists, lead and callsign fill");
    aired.Clear();
    Air("t.cond", vet);
    d.Conditions.Add("night");
    Air("t.cond", vet);
    Check(aired[0].Text == "Bright." && aired[1].Text == "Dark.", "conditions choose lines");
}

// --- exchanges
{
    ChatterDirector d = Director(Pack);
    var aired = new List<ChatterAired>();
    d.Aired = aired.Add;
    ChatterSpeaker vet = Pilot("v", "veteran"), rook = Pilot("r", "rookie"), ice = Pilot("i", "ice");
    Check(!d.StartExchange(0f, new[] { vet }), "one pilot cannot talk to nobody");
    Check(d.StartExchange(0f, new[] { ice, rook, vet }) && d.ExchangeRunning, "exchange starts");
    float t = 0f;
    for (int i = 0; i < 400 && d.ExchangeRunning; i++) { d.Tick(t); t += 0.1f; }
    Check(aired.Count == 3 && aired[0].Text == "First." && aired[0].SpeakerId == vet.Id && aired[1].SpeakerId == rook.Id && aired[2].Text == "Third.",
        "exchange lines go out in order, each role its own pilot");
    Check(aired[1].Time >= aired[0].Time + aired[0].Seconds + ChatterDirector.ExchangePauseSeconds - 0.11f, "a pause between exchange lines");
    t += 10f;
    Check(!d.StartExchange(t, new[] { ice, vet }), "an exchange that cannot be cast does not start");
    d.Conditions.Add("night");
    aired.Clear();
    Check(d.StartExchange(t, new[] { ice, vet }), "a scene exchange starts when its condition holds");
    d.Tick(t);
    Check(aired.Count == 1 && aired[0].Text == "Night first.", "scene exchange opening");
    d.Raise(t + 0.2f, "t.amb", ice, "other-ambient");
    d.Raise(t + 0.3f, "t.tac", vet);
    Check(!d.ExchangeRunning && d.Pending == 2, "an urgent call drops the exchange and nothing else");
    d.Level = ChatterLevel.Essential;
    Check(!d.StartExchange(t + 60f, new[] { ice, vet }), "no exchanges below Full");
}

// --- parser errors are reported, the rest still loads
{
    var lib = new ChatterLibrary();
    lib.Load("bad", "stray\n@ok status\nno bracket\n[*] Fine.\n[*] Open <token.\n@x loud\n[*] Kept.\n>empty\n@ok\n!replace\n[*] Only.\n");
    Check(lib.Errors.Count == 5, "five bad statements reported, got " + lib.Errors.Count + ": " + string.Join("; ", lib.Errors));
    Check(lib.Events["ok"].Lines.Count == 1 && lib.Events["ok"].Lines[0].Text == "Only." && lib.Events["x"].Lines.Count == 1, "good lines survive, !replace drops earlier ones");
}

// --- persona names
{
    Check(ChatterPersonas.Parse("Professional") == ChatterPersona.Veteran && ChatterPersonas.Parse("aggressive") == ChatterPersona.Hothead &&
          ChatterPersonas.Parse("Calm") == ChatterPersona.Ice && ChatterPersonas.Parse("Dry") == ChatterPersona.Joker, "saved pilots of the old four personas map across");
    Check(ChatterPersonas.Parse("book") == ChatterPersona.Book && ChatterPersonas.Parse("???") == ChatterPersona.Veteran, "new names parse, junk falls back");
    Check(ChatterPersonas.Key(ChatterPersona.Hothead) == "hothead" && ChatterPersonas.Count == 6, "pack keys");
}

// --- the built-in packs
{
    string dir = AppContext.BaseDirectory;
    while (dir != null && !Directory.Exists(Path.Combine(dir, "modules", "Chatter", "Assets", "Packs"))) dir = Path.GetDirectoryName(dir);
    Check(dir != null, "packs folder found");
    var d = new ChatterDirector();
    string[] files = Directory.GetFiles(Path.Combine(dir, "modules", "Chatter", "Assets", "Packs"), "*.chatter");
    Array.Sort(files, StringComparer.Ordinal);
    foreach (string file in files) d.Library.Load(Path.GetFileName(file), File.ReadAllText(file));
    Check(d.Library.Errors.Count == 0, "built-in packs read clean:\n" + string.Join("\n", d.Library.Errors));

    var problems = new List<string>();
    string[] conditions = { "night", "rain", "aftercombat", "loss", "recovery" };
    foreach (WingCue cue in WingCues.All)
    {
        if (!d.Library.Events.TryGetValue(cue.Event, out ChatterEventDef def)) { problems.Add(cue.Event + ": no pack has it"); continue; }
        if (def.Priority != cue.Priority) problems.Add(cue.Event + ": priority " + def.Priority + ", the code expects " + cue.Priority);
        for (int p = 0; p < ChatterPersonas.Count; p++)
        {
            string persona = ChatterPersonas.Key((ChatterPersona)p);
            int plain = def.Lines.Count(l => l.Who == persona && l.Need.Length == 0 && l.Forbid.Length == 0 && l.Tokens.All(tk => tk == "lead"));
            int any = def.Lines.Count(l => l.Who == persona && l.Need.Length == 0 && l.Forbid.Length == 0);
            if (any < 4) problems.Add(cue.Event + " [" + persona + "]: " + any + " unconditioned lines, want 4");
            // A fact may be missing at run time (an unknown type name): somebody must still be able to speak.
            if (plain == 0 && !cue.FactsAlways) problems.Add(cue.Event + " [" + persona + "]: no line without fact tokens");
        }
        foreach (ChatterLineDef line in def.Lines)
        {
            foreach (string token in line.Tokens)
                if (token != "lead" && Array.IndexOf(cue.Facts, token) < 0) problems.Add(cue.Event + " " + line.Id + ": token <" + token + "> is not a fact of this event");
            foreach (string c in line.Need.Concat(line.Forbid))
                if (Array.IndexOf(conditions, c) < 0) problems.Add(cue.Event + " " + line.Id + ": unknown condition '" + c + "'");
            if (line.Text.Length > 110) problems.Add(cue.Event + " " + line.Id + ": " + line.Text.Length + " characters");
            if (line.Text.Any(ch => ch > 126)) problems.Add(cue.Event + " " + line.Id + ": non-ASCII character");
        }
    }
    foreach (string id in d.Library.Events.Keys)
        if (id.StartsWith("wing.") && !WingCues.All.Any(c => c.Event == id)) problems.Add(id + ": in a pack but never raised");

    var six = new List<ChatterSpeaker>();
    for (int p = 0; p < ChatterPersonas.Count; p++) six.Add(Pilot("p" + p, ChatterPersonas.Key((ChatterPersona)p)));
    foreach (ChatterExchangeDef x in d.Library.Exchanges)
    {
        foreach (string c in x.When) if (Array.IndexOf(conditions, c) < 0) problems.Add(">" + x.Id + ": unknown condition '" + c + "'");
        foreach (string who in x.RoleWho)
            if (who != "*" && !six.Any(s => s.Persona == who)) problems.Add(">" + x.Id + ": nobody is '" + who + "'");
        if (x.RoleWho.Count < 2) problems.Add(">" + x.Id + ": one speaker");
        foreach (ChatterStepDef step in x.Steps)
            if (step.Text.Length > 110 || step.Text.Any(ch => ch > 126) || step.Tokens.Any(tk => tk != "lead")) problems.Add(">" + x.Id + ": bad line '" + step.Text + "'");
    }
    Check(problems.Count == 0, problems.Count + " pack problems:\n" + string.Join("\n", problems));

    // Every exchange is reachable with the six personas airborne: run rounds until each was heard.
    d.Reset(11);
    var heard = new HashSet<string>();
    d.Aired = a => heard.Add(a.Event);
    float t = 0f;
    foreach (string scene in new[] { "", "night", "rain", "aftercombat", "loss", "recovery" })
    {
        d.Conditions.Clear();
        if (scene.Length > 0) d.Conditions.Add(scene);
        for (int i = 0; i < 200; i++)
        {
            d.StartExchange(t, six);
            for (int k = 0; k < 600 && d.ExchangeRunning; k++) { d.Tick(t); t += 0.1f; }
            t += 20f;
        }
    }
    var silent = d.Library.Exchanges.Where(x => !heard.Contains("exchange." + x.Id)).Select(x => x.Id).ToList();
    Check(silent.Count == 0, "exchanges never heard: " + string.Join(", ", silent));

    int lines = d.Library.Events.Values.Sum(e => e.Lines.Count);
    Console.WriteLine($"packs: {files.Length} files, {d.Library.Events.Count} events, {lines} lines, {d.Library.Exchanges.Count} exchanges");
}

Console.WriteLine($"chatter: {checks} checks passed");
