using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain;

// The chatter engine's rules and a lint of the built-in packs. Run: dotnet run --project tools/ChatterCheck
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

// --- required battlefield reports survive loading, traffic bursts and long waits
{
    ChatterDirector d = Director(Pack + @"
@command.basetaken tactical
[*] The base is ours. Hold the line.
@command.baselost tactical
[*] Fall back. Get the wounded clear.
@command.superevent emergency
[*] Break, break. Major theater emergency.
@command.event status
[*] Theater conditions have changed.
@command.shiplost tactical
[*] Friendly vessel lost.
@awacs.splash tactical
[*] Hostile aircraft down.
");
    ChatterSpeaker command = new ChatterSpeaker("control", ChatterNet.Command, "COMMAND");
    var aired = new List<ChatterAired>();
    d.Aired = aired.Add;
    bool ready = false;
    d.Ready = () => ready;
    for (int i = 0; i < 70; i++) Check(d.Raise(0f, "command.baselost", command, "base:" + i, ("base", "Field " + i)), "each base transition is accepted");
    Check(d.Raise(0f, "command.superevent", command, "super:1"), "a super joins the protected queue");
    for (int i = 0; i < 70; i++) d.Raise(0f, "t.amb", Pilot("n" + i, "veteran"), "optional:" + i);
    Check(d.Pending == 71 + ChatterDirector.Capacity, "discretionary backlog is bounded separately from required reports");
    d.Tick(100f);
    Check(aired.Count == 0, "a cold voice bank does not consume a required report silently");
    ready = true;
    d.Tick(100f);
    Check(aired.Count == 1 && aired[0].Event == "command.superevent", "the superevent outranks base and optional traffic after loading");
    for (int i = 1; i <= 70; i++) d.Tick(100f + i * 12f);
    Check(aired.Count == 71 && aired.Count(x => x.Event == "command.baselost") == 70 && aired[1].Role == "Field 0", "every distinct ownership report finishes with its real location");
    d.Reset(2);
    Check(d.Raise(0f, "command.event", command, "e1") && !d.Raise(5f, "command.event", command, "e2"), "ordinary events share a sixty second cooldown");
    Check(d.Raise(5f, "command.superevent", command, "s1") && d.Raise(6f, "command.superevent", command, "s2"), "superevents bypass the event cooldown");
    Check(d.Raise(6f, "command.shiplost", command, "ship1") && !d.Raise(7f, "command.shiplost", command, "ship2"), "ship losses share a forty second cooldown");
    Check(d.Raise(6f, "awacs.splash", command, "air1") && !d.Raise(7f, "awacs.splash", command, "air2"), "aircraft losses share an eighteen second cooldown");
    d.Silence();
    Check(d.Pending == 0 && !d.Busy(10f), "receiver off clears waiting traffic without replay");
    d.Reset(3);
    Check(d.Raise(0f, "command.event", command, "e3"), "mission reset releases category cooldowns");
}

// --- mission dialogue shares COMMS without queue delay or an optional-chatter gate
{
    ChatterDirector d = Director(Pack);
    var aired = new List<ChatterAired>();
    d.Aired = aired.Add;
    ChatterSpeaker a = Pilot("a", "veteran"), b = Pilot("b", "rookie");
    Check(d.StartExchange(0f, new[] { a, b }), "an ambient exchange is waiting before the script");
    d.Level = ChatterLevel.Off;
    var story = new ChatterAired
    {
        Net = ChatterNet.Command, Speaker = "BROKER", SpeakerId = "scenario:broker", Role = "TRADEWIND AIR SERVICES",
        Text = "You have the flight.", Seconds = 44f
    };
    Check(d.Scripted(1f, story), "mission dialogue airs with optional chatter off");
    Check(!d.ExchangeRunning && d.Pending == 0, "mission dialogue cancels the ambient exchange");
    Check(aired.Count == 1 && d.AiredCount == 1 && d.Log.Count == 1, "mission dialogue reaches the readout and radio history exactly once");
    Check(aired[0].Scripted && aired[0].Event == "scenario" && aired[0].Time == 1f && aired[0].Role == story.Role,
        "the readout receives the story clock and station identity");
    Check(aired[0].Seconds == 44f && d.Busy(44.99f) && !d.Busy(45f), "the full slow-reading duration survives the normal radio hold ceiling");
    d.Tick(1f);
    Check(aired.Count == 1 && d.Busy(1f), "a paused mission clock cannot advance its scripted hold");
    d.Level = ChatterLevel.Full;
    Check(d.Raise(2f, "t.tac", b), "a tactical call can wait behind the script");
    d.Tick(2f);
    Check(aired.Count == 1 && d.Pending == 1, "ordinary tactical traffic cannot interrupt mission dialogue");
    Check(d.Raise(3f, "t.emg", a), "a live emergency is accepted during the script");
    d.Tick(3f);
    Check(aired.Count == 2 && aired[1].Priority == ChatterPriority.Emergency && aired[1].Cut, "a live emergency still takes the radio");
    Check(d.Scripted(3.1f, story) && aired.Count == 3 && !aired[2].Cut, "mission dialogue leaves an emergency voice already playing intact");
    Check(d.Raise(3.2f, "t.emg", b, "second-emergency"), "a second emergency can wait while the first speaks");
    d.Tick(3.2f);
    Check(aired.Count == 3, "scripted dialogue preserves the active emergency's priority until its own airtime ends");
    d.Tick(3f + aired[1].Seconds + 0.01f);
    Check(aired.Count == 4 && aired[3].Event == "t.emg", "the next emergency can interrupt the remaining long story hold");
    Check(d.Busy(47f), "short emergency transmissions cannot shorten the remaining story hold");
    d.Reset(9);
    Check(d.Log.Count == 0 && d.Pending == 0 && !d.Busy(0f), "scene reset releases the story and clears its history");
    Check(d.Scripted(0f, story, present: false) && aired.Count == 4 && d.Log.Count == 1 && d.Busy(40f),
        "a cutscene fallback keeps history and pacing without duplicating the COMMS readout");
    story.Seconds = float.NaN;
    Check(!d.Scripted(1f, story) && d.Log.Count == 1, "invalid story duration cannot leave an immortal readout");

    d.Reset(10);
    aired.Clear();
    Check(d.Raise(0f, "t.emg", a), "an emergency starts before a short story line");
    d.Tick(0f);
    story.Seconds = 0.2f;
    Check(d.Scripted(0.1f, story) && !aired[1].Cut && d.Busy(1f), "a short story line cannot shorten a longer emergency");
    Check(d.Raise(0.4f, "t.tac", b), "tactical traffic queues after the short story line ends");
    d.Tick(0.4f);
    Check(aired.Count == 2 && d.Pending == 1, "the surviving emergency priority still holds tactical traffic");
    d.EndScripted();
    Check(d.Busy(1f) && !d.Busy(aired[0].Seconds + 0.01f), "ending a script releases only its own hold and preserves a real emergency");
    d.Reset(11);
    story.Seconds = 44f;
    Check(d.Scripted(0f, story), "a fresh mission line starts before mission-end cleanup");
    d.EndScripted();
    Check(!d.Busy(0.1f) && d.Log.Count == 1, "mission end clears the scripted hold immediately while retaining radio history");
    Check(d.Raise(0.2f, "t.status", a), "routine traffic is accepted after mission dialogue ends");
    d.Tick(0.2f);
    Check(d.Log.Count == 2, "mission dialogue cannot hold routine traffic after its script has ended");
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

// --- which lines a recorded clip serves
{
    var known = new HashSet<string> { "wing.fox3", "wing.splash", "awacs.checkin" };
    List<string> exact = new(), fallback = new();
    void Keys(string file, string net) { exact.Clear(); fallback.Clear(); ChatterVoiceKeys.Of(file, net, known.Contains, exact, fallback); }
    Keys("003_fox3_fireFox3", "wing");
    Check(exact.Count == 1 && exact[0] == "wing.fox3" && fallback.Count == 0, "number ignored, event and Yappinator alias agree");
    Keys("fireMissile_01", "wing");
    Check(exact.Count == 0 && fallback.Contains("wing.fox3") && fallback.Contains("wing.rifle"), "a Yappinator catch-all is a fallback");
    Keys("9f3a00c1", "wing");
    Check(exact.Count == 1 && exact[0] == ChatterVoiceKeys.Line("9f3a00c1"), "eight hex digits name one exact line");
    Keys("checkin-take2", "awacs");
    Check(exact.Count == 1 && exact[0] == "awacs.checkin", "an event name belongs to the pack's net; unknown words are ignored");
    Keys("checkin", "wing");
    Check(exact.Count == 0, "another net's event is not served");
    Keys("KillGeneric SPLASH", "wing");
    Check(exact.Count == 1 && exact[0] == "wing.splash", "one clip, one key, however it is named twice");
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
    using var production = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "modules", "Chatter", "Assets", "Voices", "production.json")));
    var recordedEvents = new HashSet<string>();
    foreach (var line in production.RootElement.GetProperty("lines").EnumerateArray())
    {
        string evt = line.GetProperty("event").GetString(), id = line.GetProperty("id").GetString(), text = line.GetProperty("text").GetString();
        recordedEvents.Add(evt);
        Check(d.Library.Events.TryGetValue(evt, out var def) && def.Lines.Any(l => l.Id == id && l.Text == text && l.Tokens.Length == 0),
            "recorded script has exact token-free subtitle: " + evt + " " + id);
    }
    string[] conditions = { "night", "rain", "aftercombat", "loss", "recovery" };
    foreach (WingCue cue in WingCues.All)
    {
        if (!d.Library.Events.TryGetValue(cue.Event, out ChatterEventDef def)) { problems.Add(cue.Event + ": no pack has it"); continue; }
        if (def.Priority != cue.Priority) problems.Add(cue.Event + ": priority " + def.Priority + ", the code expects " + cue.Priority);
        for (int p = 0; p < ChatterPersonas.Count; p++)
        {
            string persona = ChatterPersonas.Key((ChatterPersona)p);
            int plain = def.Lines.Count(l => (l.Who == persona || recordedEvents.Contains(cue.Event) && l.Who == "*") && l.Need.Length == 0 && l.Forbid.Length == 0 && l.Tokens.All(tk => tk == "lead"));
            int any = def.Lines.Count(l => (l.Who == persona || recordedEvents.Contains(cue.Event) && l.Who == "*") && l.Need.Length == 0 && l.Forbid.Length == 0);
            int minimum = recordedEvents.Contains(cue.Event) ? 1 : 4;
            if (any < minimum) problems.Add(cue.Event + " [" + persona + "]: " + any + " unconditioned lines, want " + minimum);
            // A fact may be missing at run time (an unknown type name): somebody must still be able to speak.
            if (plain == 0 && !cue.FactsAlways) problems.Add(cue.Event + " [" + persona + "]: no line without fact tokens");
        }
        foreach (ChatterLineDef line in def.Lines)
        {
            foreach (string token in line.Tokens)
                if (token != "lead" && Array.IndexOf(cue.Facts, token) < 0) problems.Add(cue.Event + " " + line.Id + ": token <" + token + "> is not a fact of this event");
            foreach (string c in line.Need.Concat(line.Forbid))
                if (Array.IndexOf(conditions, c) < 0) problems.Add(cue.Event + " " + line.Id + ": unknown condition '" + c + "'");
            if (line.Text.Length > (recordedEvents.Contains(cue.Event) ? ChatterLibrary.MaxLineChars : 110)) problems.Add(cue.Event + " " + line.Id + ": " + line.Text.Length + " characters");
            if (line.Text.Any(ch => ch > 126)) problems.Add(cue.Event + " " + line.Id + ": non-ASCII character");
        }
    }
    foreach (string id in d.Library.Events.Keys)
        if (id.StartsWith("wing.") && id != "wing.survivor" && !WingCues.All.Any(c => c.Event == id)) problems.Add(id + ": in a pack but never raised");

    // The other nets, as FlightNets, CommandNet and WingChatter raise them (docs/CHATTER_PACKS.md): event, priority, facts, who must have lines.
    string[] anybody = { "*" }, enemyVoices = { "zealot", "merc", "conscript", "*" };
    var stations = new (string Event, ChatterPriority Priority, string[] Facts, string[] Who)[]
    {
        ("command.event", ChatterPriority.Status, new[] { "title" }, anybody), ("command.operation", ChatterPriority.Status, new[] { "name", "phase" }, anybody),
        ("command.insertion", ChatterPriority.Status, new string[0], anybody),
        ("command.basetaken", ChatterPriority.Tactical, new[] { "base" }, anybody), ("command.baselost", ChatterPriority.Tactical, new[] { "base" }, anybody),
        ("awacs.checkin", ChatterPriority.Status, new string[0], anybody),
        ("awacs.bandit", ChatterPriority.Tactical, new[] { "bra", "type" }, anybody), ("awacs.bogeydope", ChatterPriority.Tactical, new[] { "bra", "type" }, anybody),
        ("awacs.clean", ChatterPriority.Tactical, new string[0], anybody), ("awacs.missile", ChatterPriority.Emergency, new[] { "clock" }, anybody),
        ("awacs.gear", ChatterPriority.Status, new string[0], anybody), ("awacs.fuel", ChatterPriority.Status, new[] { "n" }, anybody),
        ("awacs.fuelcritical", ChatterPriority.Tactical, new[] { "n" }, anybody),
        ("awacs.missileclear", ChatterPriority.Tactical, new string[0], anybody), ("awacs.splash", ChatterPriority.Tactical, new[] { "target" }, anybody),
        ("tower.taxi", ChatterPriority.Status, new[] { "runway" }, anybody), ("tower.landing", ChatterPriority.Status, new[] { "runway" }, anybody),
        ("tower.goaround", ChatterPriority.Status, new string[0], anybody), ("tower.airborne", ChatterPriority.Status, new string[0], anybody),
        ("tower.welcome", ChatterPriority.Status, new string[0], anybody), ("tower.wingrolling", ChatterPriority.Status, new[] { "who" }, anybody),
        ("tower.wingdown", ChatterPriority.Ambient, new[] { "who" }, anybody), ("tower.alarm", ChatterPriority.Emergency, new string[0], anybody),
        ("enemy.spiked", ChatterPriority.Tactical, new string[0], enemyVoices), ("enemy.fox", ChatterPriority.Tactical, new string[0], enemyVoices),
        ("enemy.lastwords", ChatterPriority.Tactical, new string[0], enemyVoices), ("enemy.matedown", ChatterPriority.Tactical, new[] { "who" }, enemyVoices),
        // 2026-11-15: more AWACS and tower traffic, the allied net, and the YES/NO questions with their replies.
        ("awacs.sam", ChatterPriority.Emergency, new[] { "clock" }, anybody), ("awacs.picture1", ChatterPriority.Status, new[] { "bra", "type" }, anybody),
        ("awacs.picturemany", ChatterPriority.Status, new[] { "n", "bra" }, anybody), ("awacs.friendlydown", ChatterPriority.Tactical, new[] { "type" }, anybody),
        ("tower.startup", ChatterPriority.Ambient, new string[0], anybody),
        ("allied.splash", ChatterPriority.Ambient, new[] { "type" }, anybody), ("allied.lastwords", ChatterPriority.Tactical, new string[0], anybody),
        ("allied.thanks", ChatterPriority.Status, new string[0], anybody),
        ("command.askcontract", ChatterPriority.Tactical, new[] { "title" }, anybody), ("command.contractyes", ChatterPriority.Status, new string[0], anybody),
        ("command.contractno", ChatterPriority.Status, new string[0], anybody), ("command.contractdone", ChatterPriority.Status, new[] { "title" }, anybody),
        ("command.askperk", ChatterPriority.Tactical, new[] { "perk" }, anybody), ("command.perkyes", ChatterPriority.Status, new string[0], anybody),
        ("command.perkno", ChatterPriority.Status, new string[0], anybody), ("command.askfire", ChatterPriority.Tactical, new[] { "perk" }, anybody),
        ("command.fireyes", ChatterPriority.Status, new string[0], anybody), ("command.fireno", ChatterPriority.Status, new string[0], anybody),
        ("command.askdrones", ChatterPriority.Tactical, new string[0], anybody), ("command.dronesyes", ChatterPriority.Status, new string[0], anybody),
        ("command.dronesno", ChatterPriority.Status, new string[0], anybody),
        ("wingask.engage", ChatterPriority.Tactical, new[] { "lead" }, anybody), ("wingask.engageyes", ChatterPriority.Tactical, new string[0], anybody),
        ("wingask.engageno", ChatterPriority.Status, new string[0], anybody), ("wingask.six", ChatterPriority.Emergency, new[] { "lead" }, anybody),
        ("wingask.sixyes", ChatterPriority.Tactical, new string[0], anybody), ("wingask.sixno", ChatterPriority.Status, new string[0], anybody),
    };
    foreach (var s in stations)
    {
        if (!d.Library.Events.TryGetValue(s.Event, out ChatterEventDef def)) { problems.Add(s.Event + ": no pack has it"); continue; }
        if (def.Priority != s.Priority) problems.Add(s.Event + ": priority " + def.Priority + ", the code expects " + s.Priority);
        foreach (string who in s.Who)
            if (def.Lines.Count(l => (l.Who == who || recordedEvents.Contains(s.Event) && l.Who == "*") && l.Need.Length == 0 && l.Forbid.Length == 0) < (recordedEvents.Contains(s.Event) ? 1 : 3)) problems.Add(s.Event + " [" + who + "]: missing unconditioned lines");
        foreach (ChatterLineDef line in def.Lines)
        {
            if (Array.IndexOf(s.Who, line.Who) < 0) problems.Add(s.Event + " " + line.Id + ": nobody on this net is '" + line.Who + "'");
            foreach (string token in line.Tokens)
                if (Array.IndexOf(s.Facts, token) < 0) problems.Add(s.Event + " " + line.Id + ": token <" + token + "> is not a fact of this event");
            foreach (string c in line.Need.Concat(line.Forbid))
                if (c != "night" && c != "rain") problems.Add(s.Event + " " + line.Id + ": condition '" + c + "' is not set for this net");
            if (line.Text.Length > 110 || line.Text.Any(ch => ch > 126)) problems.Add(s.Event + " " + line.Id + ": too long or non-ASCII");
        }
    }
    foreach (string id in d.Library.Events.Keys)
        if (!id.StartsWith("wing.") && !recordedEvents.Contains(id) && !stations.Any(s => s.Event == id)) problems.Add(id + ": in a pack but never raised");

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

    // Every built-in voice clip (modules/Chatter/Assets/Voices/<pack>/<line id>.mp3) still names a line: a reworded line orphans its clip.
    string voiceRoot = AppContext.BaseDirectory;
    while (voiceRoot != null && !Directory.Exists(Path.Combine(voiceRoot, "modules", "Chatter", "Assets", "Voices"))) voiceRoot = Path.GetDirectoryName(voiceRoot);
    Check(voiceRoot != null, "the built-in voices folder is found");
    var lineIds = new HashSet<string>(d.Library.Events.Values.SelectMany(e => e.Lines).Select(l => l.Id));
    string[] recordings = Directory.GetFiles(Path.Combine(voiceRoot, "modules", "Chatter", "Assets", "Voices"), "*.mp3", SearchOption.AllDirectories);
    var orphans = recordings
        .Where(f => !Path.GetFileNameWithoutExtension(f).Split('_', '-').Any(lineIds.Contains)).ToList();
    Check(orphans.Count == 0, orphans.Count + " voice clips name no line: " + string.Join(", ", orphans.Take(5)));
    var recordedIds = new HashSet<string>(recordings.SelectMany(f => Path.GetFileNameWithoutExtension(f).Split('_', '-')));
    foreach (var line in production.RootElement.GetProperty("lines").EnumerateArray())
        Check(recordedIds.Contains(line.GetProperty("id").GetString()), "every produced subtitle has a playable recording: " + line.GetProperty("id").GetString());

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

// YES/NO questions (2026-11-15): open on air only, one at a time, timeout is NO once, a key cools down, a line that never airs is dropped.
{
    var q = new ChatterQuestions();
    int yes = 0, no = 0;
    ChatterAired Line(string evt, string speaker, float time, float seconds) => new ChatterAired { Event = evt, SpeakerId = speaker, Time = time, Seconds = seconds };
    Check(q.Ask(0f, "k1", "command.askcontract", "station:3:COMMAND", () => yes++, () => no++), "a first question is held");
    Check(!q.Ask(0f, "k2", "command.askperk", "station:3:OPS", null, null), "a second question waits for the first");
    Check(!q.Answer(1f, true) && yes == 0, "a key before the line aired does nothing");
    Check(!q.OnAired(Line("command.event", "station:3:COMMAND", 2f, 3f)), "another line does not open it");
    Check(q.OnAired(Line("command.askcontract", "station:3:COMMAND", 2f, 3f)) && q.Open, "its own line opens it");
    Check(Math.Abs(q.Until - (2f + 3f + ChatterQuestions.AnswerSeconds)) < 1e-4f, "open for the line plus the answer window");
    q.Tick(12.9f);
    Check(q.Open, "still open inside the window");
    q.Tick(13.1f);
    Check(!q.Open && no == 1 && yes == 0 && q.LastAnswer == "NO ANSWER", "no answer is NO, once");
    q.Tick(14f);
    Check(no == 1, "the NO runs only once");
    Check(!q.CanAsk(50f, "k1") && q.CanAsk(50f, "k2"), "the same key cools down, another may be asked");
    Check(q.CanAsk(13.1f + ChatterQuestions.CooldownSeconds, "k1"), "the key is free after the cooldown");
    Check(q.Ask(60f, "k3", "wingask.engage", "wing:7", () => yes++, () => no++), "next question held");
    q.Tick(60f + ChatterQuestions.PendingSeconds + 1f);
    Check(q.CanAsk(90f, "k4"), "a question whose line never aired is dropped");
    Check(!q.OnAired(Line("wingask.engage", "wing:7", 95f, 2f)), "and its late line does not open it");
    Check(q.Ask(100f, "k5", "wingask.six", "wing:7", () => yes++, () => no++) && q.OnAired(Line("wingask.six", "wing:7", 100f, 2f)), "six question opens");
    Check(q.Answer(101f, true) && yes == 1 && q.LastAnswer == "YES" && !q.Open, "YES runs the yes action and closes");
    Check(!q.Answer(101.5f, false) && no == 1, "a second key press does nothing");
    // An answer may ask the follow-up question at once (OPS: arm, then fire).
    bool followed = false;
    Check(q.Ask(200f, "arm", "command.askperk", "ops", () => followed = q.Ask(201f, "fire", "command.askfire", "ops", null, null), null), "arm question held");
    q.OnAired(Line("command.askperk", "ops", 200f, 2f));
    Check(q.Answer(201f, true) && followed, "the answer can ask the next question");
    q.Withdraw("fire");
    Check(q.CanAsk(202f, "other"), "a withdrawn question frees the radio");
}

// The COMMS strip sheds routine rows by priority before any scripted line, even over an Emergency one.
Check(ChatterShed.Rank(false, ChatterPriority.Ambient) < ChatterShed.Rank(false, ChatterPriority.Status) &&
      ChatterShed.Rank(false, ChatterPriority.Status) < ChatterShed.Rank(false, ChatterPriority.Tactical) &&
      ChatterShed.Rank(false, ChatterPriority.Emergency) < ChatterShed.Rank(true, ChatterPriority.Ambient), "shed order: routine by priority, scripted last");

// A strategic strike silences this mission's radio, including aged recovery snapshots.
RadioAftermath.Reset();
Check(!RadioAftermath.Active, "aftermath reset is inactive");
RadioAftermath.Observe(2, 1, 100f, 0f);
Check(RadioAftermath.Active && RadioAftermath.StartedAt == 100f, "actual strike activates aftermath");
Check(RadioAftermath.NextSurvivorAt >= 118f && RadioAftermath.NextSurvivorAt <= 130f, "first survivor delayed 18 to 30 seconds");
float survivorAt = RadioAftermath.NextSurvivorAt;
int nuclearRevision = RadioAftermath.Revision;
RadioAftermath.Observe(2, 1, 120f, 20f);
Check(RadioAftermath.Revision == nuclearRevision && RadioAftermath.NextSurvivorAt == survivorAt, "snapshot heartbeat does not restart survivor schedule");
Check(!RadioAftermath.TrySurvivor(survivorAt - .01f, true, false, out _), "survivor never comes early");
Check(!RadioAftermath.TrySurvivor(survivorAt + 5f, false, false, out _) && RadioAftermath.SurvivorCalls == 0, "loading cannot consume survivor call");
Check(!RadioAftermath.TrySurvivor(survivorAt + 5f, true, true, out _), "survivor cannot overlap occupied channel");
for (int n = 0; n < 20; n++)
{
    float at = RadioAftermath.NextSurvivorAt;
    Check(RadioAftermath.TrySurvivor(at, true, false, out int role) && role == n % 5, "survivor role rotates without immediate repeat");
    Check(RadioAftermath.NextSurvivorAt >= at + 40f && RadioAftermath.NextSurvivorAt <= at + 75f, "survivor calls separated 40 to 75 seconds");
}
RadioAftermath.Reset();
RadioAftermath.Observe(3, 10, 500f, 400f);
Check(RadioAftermath.Active && RadioAftermath.StartedAt == 100f && RadioAftermath.NextSurvivorAt >= 518f, "late join inherits blackout without old voice replay");
RadioAftermath.Reset();
Check(!RadioAftermath.TrySurvivor(10000f, true, false, out _) && RadioAftermath.SurvivorCalls == 0, "mission reset clears survivor schedule");

Console.WriteLine($"chatter: {checks} checks passed");

// `dotnet run --project tests/Chatter.Tests -- ids` lists every built-in line with its id (the name of its recorded clip).
if (args.Contains("ids"))
{
    string root = AppContext.BaseDirectory;
    while (root != null && !Directory.Exists(Path.Combine(root, "modules", "Chatter", "Assets", "Packs"))) root = Path.GetDirectoryName(root);
    var lib = new ChatterLibrary();
    foreach (string file in Directory.GetFiles(Path.Combine(root, "modules", "Chatter", "Assets", "Packs"), "*.chatter").OrderBy(f => f, StringComparer.Ordinal))
        lib.Load(Path.GetFileName(file), File.ReadAllText(file));
    foreach (ChatterEventDef e in lib.Events.Values.OrderBy(e => e.Id, StringComparer.Ordinal))
        foreach (ChatterLineDef l in e.Lines) Console.WriteLine($"{l.Id}  {e.Id}  [{l.Who}]  {l.Text}");
    foreach (ChatterExchangeDef x in lib.Exchanges)
        foreach (ChatterStepDef s in x.Steps) Console.WriteLine($"{s.Id}  >{x.Id}  {(char)('A' + s.Role)}  {s.Text}");
}
