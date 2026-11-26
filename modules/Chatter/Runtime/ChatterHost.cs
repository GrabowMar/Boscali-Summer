using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Chatter.Presentation;
using UnityEngine;

namespace BoscaliSummer.Modules.Chatter.Runtime
{
    /// <summary>
    /// The radio in the game: loads the packs (the built-in ones from the DLL, then the pilot's own from <c>BepInEx/config/BoscaliSummer/chatter</c>), runs the
    /// <see cref="ChatterDirector"/> on the mission clock, keeps the conditions that come from the world (night, rain), and hands every line that goes on air to the
    /// strip and the earcons. It writes no words of its own.
    /// </summary>
    internal sealed class ChatterHost : MonoBehaviour, ISceneService, IChatter
    {
        private const string ResourcePrefix = "BoscaliSummer.Chatter.", PackExtension = ".chatter";
        private const long MaxPackBytes = 1024 * 1024;

        private readonly ChatterDirector director = new ChatterDirector();
        private readonly ChatterQuestions questions = new ChatterQuestions();
        private readonly RadioAsks asks = new RadioAsks();
        private readonly Dictionary<string, ChatterSpeaker> stations = new Dictionary<string, ChatterSpeaker>(StringComparer.Ordinal);
        private readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        private readonly CommandNet command = new CommandNet();
        private FlightNets flight;
        private ChatterVoices voices;
        private readonly Func<ChatterSpeaker, bool> canTalk = s => s.CanTalk == null || s.CanTalk();
        private ChatterStrip strip;
        private IChatterSettings settings;
        private ManualLogSource log;
        private float nextWorld;
        private bool receiverWasOn = true;
        private int aftermathRevision;
        private bool aftermathWasActive;
        private bool aftermathWasSilent;

        public static ChatterHost Instance { get; private set; }

        public ChatterDirector Director => director;
        /// <summary>The player's own nets (AWACS, tower, enemy); the patches hand it the game's kill messages and centre notices.</summary>
        public FlightNets Flight => flight;
        public float Now => Time.time;

        public void Configure(IChatterSettings chatterSettings, ManualLogSource logger, Func<ChatterChannel, bool> channelEnabled = null)
        {
            settings = chatterSettings;
            log = logger;
            flight = new FlightNets(this);
            voices = new ChatterVoices(logger, chatterSettings.DataRoot);
            director.Ready = () => !voices.Loading;
            director.ChannelEnabled = channelEnabled;
            BoscaliSummer.Core.Fx.VisorSound.AddVoiceProbe(VoiceSounding);
            Instance = this;
            director.Aired += OnAired;
            LoadPacks();
        }

        internal static bool Receiving => !ModuleServices.TryGet<IRadioComms>(out var receiver) || receiver.ReceiveMilitary;
        private bool PrepareReceiver()
        {
            if (!Receiving || RadioAftermath.Blocking) return false;
            // A callback can arrive in the same frame the receiver is enabled, before Update refreshes its level.
            director.Level = settings?.Radio ?? ChatterLevel.Full;
            return true;
        }

        public bool Raise(string evt, ChatterSpeaker speaker, string key = null, params (string Name, string Value)[] facts) =>
            PrepareReceiver() && director.Raise(Time.time, evt, speaker, key, facts);

        public bool Say(ChatterSpeaker speaker, string key, string text, ChatterPriority priority = ChatterPriority.Status) =>
            PrepareReceiver() && director.Say(Time.time, speaker, key, text, priority);

        public bool Ask(ChatterSpeaker speaker, string evt, string key, Action yes, Action no = null, params (string Name, string Value)[] facts)
        {
            // Held before the line is raised: a free channel airs it inside Raise, and the question must already be waiting for it.
            if (!PrepareReceiver() || speaker == null || Application.isBatchMode || !questions.Ask(Time.time, key, evt, speaker.Id, Guard(yes), Guard(no))) return false;
            if (director.Raise(Time.time, evt, speaker, key, facts)) return true;
            questions.Withdraw(key);
            return false;
        }

        /// <summary>The pilot's answer (the sim hook and the keys). False when no question is open.</summary>
        public bool Answer(bool yes) => Acked(questions.Answer(Time.time, yes), yes);

        /// <summary>The pilot's YES or NO was taken: the visor's ack (HMD-9).</summary>
        private static bool Acked(bool taken, bool yes)
        {
            if (taken) BoscaliSummer.Core.Fx.VisorSound.Play(yes ? BoscaliSummer.Core.Fx.VisorClip.Yes : BoscaliSummer.Core.Fx.VisorClip.No);
            return taken;
        }

        public ChatterQuestions Questions => questions;

        internal ChatterVoices Voices => voices;

        private Action Guard(Action action) => action == null ? null : () =>
        {
            try { action(); }
            catch (Exception e) { log?.LogWarning("[Chatter] a radio answer failed: " + e.Message); }
        };

        public bool Scripted(in ChatterAired line)
        {
            // COMMS stands down under the maximised map, so the script's own plate presents the line there.
            if (!isActiveAndEnabled || Application.isBatchMode || DynamicMap.mapMaximized || RadioAftermath.Blocking) return false;
            if (strip == null) strip = new ChatterStrip(transform);
            return director.Scripted(Time.time, line);
        }

        public void EndScripted()
        {
            director.EndScripted();
            strip?.ClearScripted();
        }

        public ChatterSpeaker Station(ChatterNet net, string callsign)
        {
            callsign = ChatterLibrary.Clean(callsign);
            if (callsign.Length > 32) callsign = callsign.Substring(0, 32);
            if (callsign.Length == 0) callsign = "CONTROL";
            string id = "station:" + (int)net + ":" + callsign;
            if (!stations.TryGetValue(id, out ChatterSpeaker speaker))
                stations[id] = speaker = new ChatterSpeaker(id, net, callsign, null, callsign.ToLowerInvariant().Replace(' ', '-'));
            return speaker;
        }

        public ChatterSpeaker NearTower(Vector3 at) => flight?.NearTower(at);

        public void Chime() => ChatterAudio.Chime();

        public void ResetForScene()
        {
            director.Reset(UnityEngine.Random.Range(1, int.MaxValue));
            questions.Reset();
            asks.Reset();
            stations.Clear();
            command.Reset();
            flight?.Reset();
            ChatterAudio.Reset();
            if (voices != null && settings != null && !Application.isBatchMode) voices.Activate(settings.VoicePacks, director.Library);
            strip?.Clear();
            nextWorld = 0f;
            receiverWasOn = Receiving;
            aftermathRevision = -1;
            aftermathWasActive = RadioAftermath.Blocking;
            aftermathWasSilent = false;
        }

        private void OnDisable()
        {
            strip?.Clear();
            voices?.Stop();
            ChatterAudio.Reset();
        }

        private bool VoiceSounding() => voices != null && voices.Sounding;
        private float MilitaryVolume => settings.VoicePackVolume *
            (ModuleServices.TryGet<IRadioComms>(out var receiver) ? receiver.MilitaryVolume : 1f);

        private void OnDestroy()
        {
            BoscaliSummer.Core.Fx.VisorSound.RemoveVoiceProbe(VoiceSounding);
            director.Aired -= OnAired;
            command.Reset();
            flight?.Reset();
            voices?.Deactivate();
            if (ReferenceEquals(Instance, this)) Instance = null;
            strip?.Destroy();
            strip = null;
        }

        private void Update()
        {
            if (settings == null) return;
            RadioAftermath.Advance(Time.time);
            voices.Tick();
            bool receive = Receiving;
            if (aftermathRevision != RadioAftermath.Revision)
            {
                aftermathRevision = RadioAftermath.Revision;
                bool stop = RadioAftermath.Blocking || aftermathWasActive;
                aftermathWasActive = RadioAftermath.Blocking;
                if (stop)
                {
                    // The strategic strike ends the ordinary command net, including queued critical reports.
                    director.Silence(); voices.Stop(); ChatterAudio.Reset(); questions.Reset(); strip?.Clear();
                }
            }
            if (!receive && receiverWasOn)
            {
                director.Silence();
                voices.Stop();
                ChatterAudio.Reset();
                questions.Reset();
                strip?.Clear();
            }
            receiverWasOn = receive;
            voices.Volume(MilitaryVolume);
            ChatterAudio.Volume(MilitaryVolume);
            // Wing's optional exchanges reach the director directly; Off closes that path during the blackout too.
            director.Level = receive && !RadioAftermath.Blocking ? settings.Radio : ChatterLevel.Off;
            bool silent = RadioAftermath.Silent(Time.time);
            if (silent && !aftermathWasSilent)
            {
                director.Silence(); voices.Stop(); ChatterAudio.Reset(); strip?.Clear();
            }
            aftermathWasSilent = silent;
            if (RadioAftermath.Blocking)
            {
                if (receive && settings.Radio != ChatterLevel.Off && MilitaryVolume > 0f &&
                    RadioAftermath.TryEmergency(Time.time, !voices.Loading, director.Busy(Time.time), out int stage))
                    Emergency(stage);
                if (receive && settings.Radio != ChatterLevel.Off && MilitaryVolume > 0f &&
                    RadioAftermath.TrySurvivor(Time.time, !voices.Loading, director.Busy(Time.time), out int role))
                    Survivor(role);
                if (receive) director.Tick(Time.time, canTalk);
                strip?.Question(false, 0f, 0f, "", "", null);
                strip?.Tick(GameManager.GetLocalAircraft(out Aircraft survivor) && survivor != null);
                return;
            }
            bool flying = GameManager.GetLocalAircraft(out Aircraft me) && me != null;
            // Bind event publishers even while unseated: a superevent must not depend on entering a cockpit.
            if (Time.time >= nextWorld)
            {
                nextWorld = Time.time + 2f;
                command.Tick(this, flying ? me : null);
                flight.Tick(flying ? me : null);
                if (flying)
                {
                    ReadWorld(me);
                    asks.Tick(this, me);
                    foreach (string evt in director.Unknown)
                        if (warned.Add(evt)) log?.LogWarning("[Chatter] no pack has the event '" + evt + "'; nothing is said for it.");
                }
            }
            if (receive) director.Tick(Time.time, canTalk);
            // The answer keys are read only while a question is up, and never while the pilot types.
            if (questions.Open && !BoscaliSummer.Core.Ui.MapTool.Typing())
            {
                if (settings.AnswerDown(true)) Acked(questions.Answer(Time.time, true), true);
                else if (settings.AnswerDown(false)) Acked(questions.Answer(Time.time, false), false);
            }
            questions.Tick(Time.time);
            strip?.Question(questions.Open, questions.Until - Time.time, questions.Window, settings.AnswerKey(true), settings.AnswerKey(false),
                Time.time - questions.AnsweredAt < ChatterQuestions.EchoSeconds ? questions.LastAnswer : null);
            strip?.Tick(flying);
        }

        private void ReadWorld(Aircraft me)
        {
            bool night = false, rain = false;
            if (ModuleServices.TryGet<IWeatherView>(out var weather))
            {
                var at = me.GlobalPosition();
                if (weather.TrySample(at.x, at.z, out var sample)) { night = sample.Night; rain = sample.Rain01 > 0.35f; }
            }
            Set("night", night);
            Set("rain", rain);
            // The pilot's own switches for pack lines ("profanity", "banter"): true for as long as they are listed.
            foreach (string word in (settings.PackConditions ?? "").Split(','))
                if (word.Trim().Length > 0) director.Conditions.Add(word.Trim().ToLowerInvariant());
        }

        private void Survivor(int role)
        {
            string evt, character, callsign;
            ChatterNet net;
            switch (role)
            {
                case 1: evt = "awacs.survivor"; character = "skywatch"; callsign = "SKYWATCH / SURVIVOR"; net = ChatterNet.Awacs; break;
                case 2: evt = "wing.survivor"; character = "survivor-pilot"; callsign = "STRANDED FLIGHT"; net = ChatterNet.Wing; break;
                case 3: evt = "command.survivor.captain"; character = "captain"; callsign = "BRIDGE / SURVIVOR"; net = ChatterNet.Command; break;
                case 4: evt = "command.survivor.paratrooper"; character = "paratrooper"; callsign = "DROP LEAD / SURVIVOR"; net = ChatterNet.Command; break;
                default: evt = "command.survivor.ground"; character = "ground"; callsign = "GROUND / SURVIVOR"; net = ChatterNet.Command; break;
            }
            ChatterSpeaker speaker = Station(net, callsign);
            speaker.Character = character;
            director.Level = settings.Radio;
            try { director.Raise(Time.time, evt, speaker, "survivor:" + RadioAftermath.Revision + ":" + RadioAftermath.SurvivorCalls); }
            finally { director.Level = ChatterLevel.Off; }
        }

        private void Emergency(int stage)
        {
            string[] events = { "release", "flash", "ground", "wing", "last" };
            string[] callsigns = { "THEATER COMMAND", "SKYWATCH / FLASH", "GROUND / PANIC", "DAGGER / PANIC", "GROUND / LAST SIGNAL" };
            ChatterNet net = stage == 1 ? ChatterNet.Awacs : stage == 3 ? ChatterNet.Wing : ChatterNet.Command;
            ChatterSpeaker speaker = Station(net, callsigns[stage]);
            speaker.Character = "sa74-emergency";
            director.Level = settings.Radio;
            try { director.Raise(Time.time, "command.sa74." + events[stage], speaker,
                "sa74-emergency:" + RadioAftermath.Revision + ":" + stage); }
            finally { director.Level = ChatterLevel.Off; }
        }

        /// <summary>Scenario-only (ChatterAutomation 'quiet'): while true, spoken lines still reach the voices, prompts and the MFD log, but the comms strip stays dark for a capture.</summary>
        public bool StripSuppressed { get; set; }

        private void Set(string condition, bool on)
        {
            if (on) director.Conditions.Add(condition); else director.Conditions.Remove(condition);
        }

        private void OnAired(ChatterAired line)
        {
            // A recorded voice holds the channel for as long as it really talks.
            ModuleServices.TryGet<IRadioComms>(out var receiver);
            float spoken = Receiving && MilitaryVolume > 0f ? voices.Play(line, transform, MilitaryVolume) : 0f;
            if (spoken > 0f)
            {
                director.Hold(Time.time, spoken);
                line.Seconds = Mathf.Max(line.Seconds, spoken);
                if (MilitaryVolume > 0f) receiver?.DuckMilitary(spoken);
            }
            if (Receiving && spoken > 0f) ChatterAudio.Transmit(spoken);
            // A question's line opens its answer window, and its row stays up for as long as the window is open.
            if (questions.OnAired(line)) line.Seconds += ChatterQuestions.AnswerSeconds;
            if (strip == null && !Application.isBatchMode) strip = new ChatterStrip(transform);
            if (!StripSuppressed) strip?.Show(line);
            // The MFD's tactical log keeps the radio with the game's own messages.
            if (ModuleServices.TryGet<ITacticalLog>(out var tactical))
                tactical.Radio(ChatterStrip.Tag(line.Net) + " " + (line.Speaker ?? "").ToUpperInvariant() + ": " + line.Text);
        }

        private void LoadPacks()
        {
            director.Library.Clear();
            Assembly assembly = typeof(ChatterHost).Assembly;
            string[] names = assembly.GetManifestResourceNames();
            Array.Sort(names, StringComparer.Ordinal);
            int packs = 0;
            foreach (string name in names)
            {
                if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !name.EndsWith(PackExtension, StringComparison.Ordinal)) continue;
                using (Stream stream = assembly.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream))
                    director.Library.Load(name.Substring(ResourcePrefix.Length), reader.ReadToEnd());
                packs++;
            }
            try
            {
                string dir = Path.Combine(Paths.ConfigPath, "BoscaliSummer", "chatter");
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "*" + PackExtension);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string file in files)
                    {
                        if (new FileInfo(file).Length > MaxPackBytes) { log?.LogWarning("[Chatter] " + Path.GetFileName(file) + " is over 1 MB and was skipped."); continue; }
                        director.Library.Load(Path.GetFileName(file), File.ReadAllText(file));
                        packs++;
                    }
                }
            }
            catch (Exception e)
            {
                log?.LogWarning("[Chatter] could not read the user packs: " + e.Message);
            }
            int shown = 0;
            foreach (string error in director.Library.Errors)
            {
                if (shown++ >= 20) { log?.LogWarning("[Chatter] ... and " + (director.Library.Errors.Count - 20) + " more pack errors."); break; }
                log?.LogWarning("[Chatter] " + error);
            }
            log?.LogInfo("[Chatter] " + packs + " packs: " + director.Library.Events.Count + " events, " + director.Library.Exchanges.Count + " exchanges.");
        }
    }
}
