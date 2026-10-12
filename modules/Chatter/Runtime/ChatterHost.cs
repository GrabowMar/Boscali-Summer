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
using BoscaliSummer.Modules.Wing.Configuration;
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
        private readonly Dictionary<string, ChatterSpeaker> stations = new Dictionary<string, ChatterSpeaker>(StringComparer.Ordinal);
        private readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        private readonly CommandNet command = new CommandNet();
        private readonly Func<ChatterSpeaker, bool> canTalk = s => s.CanTalk == null || s.CanTalk();
        private ChatterStrip strip;
        private WingConfig settings;
        private ManualLogSource log;
        private float nextWorld;

        public ChatterDirector Director => director;
        public float Now => Time.time;

        public void Configure(WingConfig wing, ManualLogSource logger)
        {
            settings = wing;
            log = logger;
            director.Aired += OnAired;
            LoadPacks();
        }

        public bool Raise(string evt, ChatterSpeaker speaker, string key = null, params (string Name, string Value)[] facts) =>
            director.Raise(Time.time, evt, speaker, key, facts);

        public bool Say(ChatterSpeaker speaker, string key, string text, ChatterPriority priority = ChatterPriority.Status) =>
            director.Say(Time.time, speaker, key, text, priority);

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

        public void ResetForScene()
        {
            director.Reset(UnityEngine.Random.Range(1, int.MaxValue));
            stations.Clear();
            command.Reset();
            ChatterAudio.Reset();
            strip?.Clear();
            nextWorld = 0f;
        }

        private void OnDestroy()
        {
            director.Aired -= OnAired;
            command.Reset();
            strip?.Destroy();
            strip = null;
        }

        private void Update()
        {
            if (settings == null) return;
            director.Level = (ChatterLevel)Mathf.Clamp((int)settings.Radio.Value, 0, 2);
            bool flying = GameManager.GetLocalAircraft(out Aircraft me) && me != null;
            if (flying)
            {
                if (Time.time >= nextWorld)
                {
                    nextWorld = Time.time + 2f;
                    ReadWorld(me);
                    command.Tick(this, me);
                    foreach (string evt in director.Unknown)
                        if (warned.Add(evt)) log?.LogWarning("[Chatter] no pack has the event '" + evt + "'; nothing is said for it.");
                }
                director.Tick(Time.time, canTalk);
            }
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
        }

        private void Set(string condition, bool on)
        {
            if (on) director.Conditions.Add(condition); else director.Conditions.Remove(condition);
        }

        private void OnAired(ChatterAired line)
        {
            ChatterAudio.Transmit(line.Seconds);
            if (strip == null && !Application.isBatchMode) strip = new ChatterStrip(transform);
            strip?.Show(line);
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
