using BoscaliSummer.Modules.Wing.Domain;

int checks = 0;
void Check(bool condition, string label)
{
    checks++;
    if (!condition) throw new InvalidOperationException(label);
}
RadioLine Line(string key, RadioClass priority, int speaker = 0) => new RadioLine
{ Key = key, Class = priority, Speaker = speaker, Text = "Test transmission." };

var queue = new RadioQueue();
queue.Enqueue(Line("AMBIENT0", RadioClass.Chatter), 0);
Check(queue.Next(0, out var sent) && sent.Class == RadioClass.Chatter, "quiet opening begins");
queue.Enqueue(Line("reply", RadioClass.Chatter, 1), 0);
queue.Enqueue(Line("contact", RadioClass.Tactical, 2), 0);
queue.CancelChatter();
Check(!queue.Holds("reply") && queue.Holds("contact"), "interruption removes orphaned reply, preserves contact");
queue.Enqueue(Line("missile", RadioClass.Emergency), 0.1f);
Check(queue.Next(0.1f, out sent, held: true) && sent.Key == "missile", "emergency cuts speaking chatter");
Check(!queue.Next(0.2f, out _, held: true), "tactical cannot overlap emergency");
Check(queue.Next(3, out sent) && sent.Key == "contact", "blocked contact survives urgent traffic");
Check(!queue.Enqueue(Line("contact", RadioClass.Tactical, 2), 3.1f), "repeat suppressed");
queue = new RadioQueue();
queue.Enqueue(Line("voice", RadioClass.Status), 0);
queue.Next(0, out _);
queue.Enqueue(Line("second", RadioClass.Status, 1), 1);
Check(!queue.Next(19, out _, held: true), "twenty-second voice cannot be overlapped");
Check(queue.Next(21, out sent) && sent.Key == "second", "blocked status survives long clip");
queue = new RadioQueue();
for (int i = 0; i < RadioQueue.Capacity; i++) Check(queue.Enqueue(Line("quiet" + i, RadioClass.Chatter), 0), "bounded queue filling");
Check(!queue.Enqueue(Line("overflow", RadioClass.Chatter), 0), "full ambient queue refuses overflow");
Check(queue.Enqueue(Line("urgent", RadioClass.Emergency), 0), "emergency evicts ambient at capacity");
Check(queue.Queued == RadioQueue.Capacity && queue.Next(0, out sent) && sent.Key == "urgent", "capacity and priority hold");

var pacing = new ChatterPacing();
pacing.Reset(0, 0);
pacing.Suppress(1);
Check(pacing.Scene == ChatterScene.Transit, "priority traffic does not invent a battle aftermath");
Check(!pacing.Ready(54, true, true, false, false), "opening silence");
Check(pacing.Ready(55, true, true, false, false), "quiet flight permits exchange");
Check(!pacing.Ready(55, false, true, false, false), "essential suppresses banter");
Check(!pacing.Ready(55, true, false, false, false), "no airborne listener suppresses banter");
Check(!pacing.Ready(55, true, true, true, false), "combat suppresses banter");
Check(!pacing.Ready(55, true, true, false, true), "occupied channel suppresses banter");
pacing.Observe(100);
Check(pacing.Scene == ChatterScene.AfterCombat && !pacing.Ready(124, true, true, false, false), "combat aftermath breath");
pacing.Observe(125, loss: true);
pacing.Observe(130);
Check(pacing.Scene == ChatterScene.Loss, "combat does not erase grief scene");
Check(!pacing.Ready(169, true, true, false, false), "loss leaves longer silence");
pacing.Scheduled(170, 0);
Check(pacing.Scene == ChatterScene.Transit && pacing.NextExchange == 255, "scene consumed once and cadence bounded");
pacing.Reset(500, int.MinValue);
Check(pacing.NextExchange >= 555 && pacing.NextExchange <= 584, "reset clears previous pacing, signed seed safe");

var pack = new VoicePackIndex();
pack.Add(0, "001_FOX2_fireFox3");
pack.Add(1, "damage-02");
pack.Add(2, "fireARM-01");
pack.Add(3, "AMBIENT0_01");
pack.Add(4, "AMBIENTREPLY0_01");
pack.Add(5, "001_1234_unknown");
pack.Add(6, "Idle-01");
Check(pack.Count == 5, "unrelated and numeric filename tokens ignored");
Check(pack.Clips("fireFox2").SequenceEqual(new[] { 0 }) && pack.Clips("fireFox3").SequenceEqual(new[] { 0 }), "Yappinator multievent tags");
Check(pack.Clips("takeDamage").SequenceEqual(new[] { 1 }), "upstream damage alias");
Check(VoicePackIndex.EventsFor("MAGNUM").Contains("fireARM"), "ARM compatibility");
Check(VoicePackIndex.EventsFor("RIFLE").Contains("fireAGM"), "AGM compatibility");
Check(VoicePackIndex.EventsFor("PANIC").Contains("RwrOn") && !VoicePackIndex.EventsFor("PANIC").Contains("RwrOnFox3"), "unknown missile never invents seeker type");
Check(!VoicePackIndex.EventsFor("CRITICAL").Contains("engineDamage"), "critical damage does not invent engine failure");
Check(!VoicePackIndex.EventsFor("SPLASH").Contains("killAircraft"), "generic kill does not invent victim type");
Check(pack.Clips("AMBIENT0").SequenceEqual(new[] { 3 }), "exact cinematic clip indexed");
Check(VoicePackIndex.EventsFor("AMBIENT0").SequenceEqual(new[] { "AMBIENT0" }), "bespoke dialogue cannot play unrelated Idle speech");
Check(VoicePackIndex.PackFor(2, 3) == 0 && VoicePackIndex.PackFor(6, 3) == 1 && VoicePackIndex.PackFor(2, 0) == -1, "stable pack assignment");

foreach (ChatterPersona persona in Enum.GetValues<ChatterPersona>())
    foreach (string key in ChatterDialogue.EventKeys)
        foreach (int seed in new[] { 0, 1, 2, int.MinValue, int.MaxValue })
        {
            string text = ChatterDialogue.Event(persona, key, "Test", seed);
            Check(text.Length > 0 && text.Length <= 240 && !text.Contains('{'), "authored event renders without placeholders: " + key);
        }
Check(ChatterDialogue.Event(ChatterPersona.Dry, "BINGO", null, 1).Contains("Bingo"), "dry persona keeps safety information");
Check(ChatterDialogue.Event(ChatterPersona.Professional, "OUTOFAMMO", null, 1).Contains("Rejoining"), "formation winchester does not invent RTB");
Check(!ChatterDialogue.Event(ChatterPersona.Calm, "DAMAGED", null, 1).Contains("responding"), "damage does not invent healthy controls");
foreach (ChatterScene scene in Enum.GetValues<ChatterScene>())
    Check(Enumerable.Range(0, ChatterDialogue.AmbientCount).Any(i => ChatterDialogue.AmbientAt(i).Scene == scene), "authored scene exists: " + scene);
for (int i = 0; i < ChatterDialogue.AmbientCount; i++)
{
    var exchange = ChatterDialogue.AmbientAt(i);
    Check(exchange.Opening.Length <= 240 && (exchange.Reply == null || exchange.Reply.Length <= 240), "bounded dialogue");
    Check(exchange.ReplyTag == null || exchange.Reply != null, "named reply has dialogue");
}
Check(ChatterDialogue.Ambient(int.MinValue, false).Reply == null, "solo exchange has no unanswered prompt");
var log = new RadioLog();
for (int i = 0; i < 100; i++) log.Push(i, i.ToString());
Check(log.Count == RadioLog.Capacity && log.TextAt(0) == "36", "bounded transmitted history");
log.Clear();
Check(log.Count == 0, "history reset");
Console.WriteLine($"PASS: {checks} chatter checks (scheduling, interruptions, dialogue, Yappinator mappings).");
