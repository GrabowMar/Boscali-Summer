using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Scenario;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Scenario.Configuration;
using BoscaliSummer.Modules.Scenario.Presentation;
using NOAvionics;
using NuclearOption.SavedMission;
using UnityEngine;
using UnityEngine.Networking;

namespace BoscaliSummer.Modules.Scenario.Runtime
{
    /// <summary>
    /// Runs the script of the mission being played, for this pilot. It hears the game's own objective events (which the game already sends to every multiplayer
    /// client), watches what the local pilot is doing (joined a side, has an aircraft, took off, landed), and plays the matching beats on the stage view. Local
    /// presentation only: the world a script changes is written into the mission itself by <see cref="MissionPatcher"/> and run by the game.
    /// </summary>
    internal sealed class ScenarioHost : MonoBehaviour, ISceneService, IScenarioStage
    {
        private const float StartDelaySeconds = 1.5f, SettleSeconds = 2f;
        private const int MaxEarlyEvents = 256;

        private readonly List<KeyValuePair<TriggerKind, string>> early = new List<KeyValuePair<TriggerKind, string>>();
        private readonly List<StageShot> stageShots = new List<StageShot>();
        private readonly List<ScenarioCut> playing = new List<ScenarioCut>();
        private readonly Dictionary<ScenarioPicture, Texture2D> pictures = new Dictionary<ScenarioPicture, Texture2D>();
        private ScenarioLibrary library;
        private ScenarioSettings settings;
        private ManualLogSource log;
        private ScenarioStageView view;
        private ScenarioRunner runner;
        private ScenarioDef script;
        private Mission bound;
        private ICinematicStage camera;
        private float boundAt, startedAt, changedAt;
        private bool started, joined, hadAircraft, airborne, cutPlaying, collecting, choosing, ownJoin;
        private const int MaxPictureBytes = 8 * 1024 * 1024, MaxMusicBytes = 24 * 1024 * 1024;
        private const float MusicFadeSeconds = 3.5f;
        private AudioSource music;
        private AudioClip musicClip;
        private float musicVolume, musicFadeFrom = -1f, musicEndsAt;
        private int musicAsked;
        private int shotsShown, skippedShots;
        private GameplayUI.ActiveCanvas? hidden;

#if !PUBLIC_RELEASE
        private int linesSaid, cutsPlayed, shotsPlayed, heard;
        private string lastLine = "";

        internal static ScenarioHost Instance { get; private set; }

        /// <summary>For unattended in-game tests: what the runner has done so far.</summary>
        internal Dictionary<string, object> Report() => new Dictionary<string, object>
        {
            ["ok"] = true, ["script"] = script?.Id ?? "", ["started"] = started ? 1 : 0, ["joined"] = joined ? 1 : 0,
            ["side"] = runner == null ? "" : runner.Side.ToString(), ["airborne"] = airborne ? 1 : 0, ["cut_playing"] = cutPlaying ? 1 : 0,
            ["cuts_played"] = cutsPlayed, ["shots_played"] = shotsPlayed, ["lines_said"] = linesSaid, ["last_line"] = lastLine,
            ["objective_events"] = heard, ["flags"] = runner == null ? "" : string.Join(",", runner.Flags),
            ["finished"] = runner != null && runner.Finished ? 1 : 0, ["idle"] = runner != null && runner.Idle ? 1 : 0,
            ["music_playing"] = music != null && music.isPlaying ? 1 : 0, ["game_music_muted"] = gameMusicMuted ? 1 : 0
        };
#endif

        private Func<Mission, ScenarioDef> scriptOf;

        internal void Configure(ScenarioLibrary scripts, Func<Mission, ScenarioDef> resolve, ScenarioSettings configuration, ManualLogSource logger)
        {
            scriptOf = resolve;
#if !PUBLIC_RELEASE
            Instance = this;
#endif
            library = scripts;
            settings = configuration;
            log = logger;
            MissionProfile.Preparing += Listen;
        }

        /// <summary>
        /// A mission is about to load. Listen before it starts: the first objectives start in the same call that starts the mission, earlier than any Update here
        /// can notice it runs. What is heard before the script is bound waits in <see cref="early"/>.
        /// </summary>
        private void Listen()
        {
            early.Clear();
            collecting = true;
            MissionManager.onObjectiveStarted -= ObjectiveStarted;
            MissionManager.onObjectiveComplete -= ObjectiveDone;
            MissionManager.onObjectiveStarted += ObjectiveStarted;
            MissionManager.onObjectiveComplete += ObjectiveDone;
        }

        private void ObjectiveStarted(Objective objective) => Heard(TriggerKind.Started, objective);
        private void ObjectiveDone(Objective objective) => Heard(TriggerKind.Done, objective);

        private void Heard(TriggerKind kind, Objective objective)
        {
            string name = objective?.SavedObjective?.UniqueName;
            if (name == null) return;
#if !PUBLIC_RELEASE
            heard++;
#endif
            if (runner != null) runner.Raise(kind, name);
            else if (collecting && early.Count < MaxEarlyEvents) early.Add(new KeyValuePair<TriggerKind, string>(kind, name));
        }

        private void Update()
        {
            if (library == null) return;
            Mission mission = MissionManager.IsRunning ? MissionManager.CurrentMission : null;
            if (!ReferenceEquals(mission, bound)) Bind(mission);
            if (runner == null) return;

            float now = Time.time;
            if (!started)
            {
                // The join screen comes up a moment after the mission starts; a cutscene takes the camera from behind it.
                if (now - boundAt < StartDelaySeconds || SceneSingleton<CameraStateManager>.i == null || SceneSingleton<GameplayUI>.i == null) return;
                started = true;
                startedAt = now;
                runner.Raise(TriggerKind.Start);
            }
            Watch(now);
            // A script that puts the pilot on a side itself keeps the game's join screen out of the way until it has.
            if (ownJoin && !joined && !cutPlaying) HideGameUi();
            if (choosing)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) Enlist(ScenarioSide.Bdf);
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) Enlist(ScenarioSide.Pala);
            }
            ShowFeed(!cutPlaying);
            if (cutPlaying)
            {
                HideGameUi();
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Escape))
                {
                    log?.LogInfo("[Scenario] cutscene skipped by the pilot.");
                    camera?.Stop();
                }
            }
            runner.TextSpeed = settings.TextSpeed.Value;
            runner.Tick(now - startedAt);
            if (music == null || musicClip == null) return;
            // The game's own music keeps quiet for as long as the script's track plays, and comes back when the track has run out.
            if (music.isPlaying) MuteGameMusic(true);
            // Paused audio (an unfocused game) also reads as "not playing"; only the track's own length says it has run out.
            else if (Time.unscaledTime >= musicEndsAt) { StopMusic(); return; }
            if (musicFadeFrom < 0f) return;
            float faded = (Time.unscaledTime - musicFadeFrom) / MusicFadeSeconds;
            music.volume = musicVolume * Mathf.Clamp01(1f - faded);
            if (faded >= 1f) StopMusic();
        }

        private void Bind(Mission mission)
        {
            Unbind();
            bound = mission;
            if (mission == null) return;
            script = scriptOf(mission);
            if (script == null)
                foreach (ScenarioDef named in library.Scripts)
                    if (named.Title == mission.Name)
                        log?.LogWarning("[Scenario] the mission '" + mission.Name + "' was loaded from the group '" +
                            (mission.LoadKey.HasValue && mission.LoadKey.Value.Group != null ? mission.LoadKey.Value.Group.Name : "?") +
                            "' and is not the scripted one: start the scripted mission from its own group '" + named.Group + "'.");
            // A mission without a script needs nothing kept.
            collecting = false;
            if (script == null) { early.Clear(); return; }
            ScenarioDef running = script;
            runner = new ScenarioRunner(running, this, flag => library.Progress.Has(running.Saga, flag));
            foreach (KeyValuePair<TriggerKind, string> heard in early) runner.Raise(heard.Key, heard.Value);
            // A session that never loaded through the mission-preparation seam still needs the events.
            Listen();
            collecting = false;
            view = ScenarioStageView.Create(transform);
            boundAt = Time.time;
            started = joined = hadAircraft = airborne = choosing = ownJoin = false;
            foreach (ScenarioBeat beat in running.Beats)
                foreach (ScenarioAction action in beat.Actions)
                    if (action.Kind == ActionKind.Enlist || action.Kind == ActionKind.Choose) ownJoin = true;
            log?.LogInfo("[Scenario] '" + running.Id + "' is running: " + running.Beats.Count + " beat(s).");
        }

        private void Unbind()
        {
            if (cutPlaying) camera?.Stop();
            cutPlaying = false;
            ShowGameUi();
            ShowFeed(true);
            runner = null;
            script = null;
            bound = null;
            camera = null;
            if (view != null) Destroy(view.gameObject);
            view = null;
            foreach (Texture2D texture in pictures.Values)
                if (texture != null) Destroy(texture);
            pictures.Clear();
            StopMusic();
        }

        public void Music(string file, float volume)
        {
            if (file == null)
            {
                if (music != null && music.isPlaying && musicFadeFrom < 0f) musicFadeFrom = Time.unscaledTime;
                else musicAsked++;   // a track still loading must not start after it was told to stop
                return;
            }
            StartCoroutine(PlayMusic(file, volume, ++musicAsked));
        }

        private bool gameMusicMuted;

        private void MuteGameMusic(bool mute)
        {
            if (gameMusicMuted == mute || SoundManager.i == null || MusicManager.i == null) return;
            gameMusicMuted = mute;
            AudioSource now = GameAccess.GetCurrentMusicSource(MusicManager.i), next = GameAccess.GetFadeMusicSource(MusicManager.i);
            if (now != null) now.mute = mute;
            if (next != null) next.mute = mute;
        }

        private void StopMusic()
        {
            MuteGameMusic(false);
            musicAsked++;
            musicFadeFrom = -1f;
            if (music != null) music.Stop();
            if (musicClip != null) Destroy(musicClip);
            musicClip = null;
        }

        /// <summary>The file beside the pilot's scripts, or a built-in track unpacked once into the BepInEx cache (Unity decodes music from a file).</summary>
        private string MusicFile(string file)
        {
            string own = Path.Combine(library.Folder, file);
            if (File.Exists(own)) return new FileInfo(own).Length <= MaxMusicBytes ? own : null;
            byte[] bytes = EmbeddedResources.ReadAll(typeof(ScenarioHost).Assembly, "BoscaliSummer.Scenario." + file, MaxMusicBytes);
            if (bytes == null) return null;
            string folder = Path.Combine(BepInEx.Paths.CachePath, "BoscaliSummer");
            string cached = Path.Combine(folder, file);
            if (File.Exists(cached) && new FileInfo(cached).Length == bytes.Length) return cached;
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(cached, bytes);
            return cached;
        }

        private IEnumerator PlayMusic(string file, float volume, int asked)
        {
            string path = null;
            try { path = MusicFile(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (path == null) { log?.LogWarning("[Scenario] music '" + file + "' could not be read."); yield break; }
            AudioType type = file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : AudioType.MPEG;
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
            {
                yield return request.SendWebRequest();
                AudioClip clip = request.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(request) : null;
                if (clip == null) { log?.LogWarning("[Scenario] music '" + file + "' could not be decoded."); yield break; }
                // Told to stop, or the mission ended, while the file was loading.
                if (asked != musicAsked || runner == null) { Destroy(clip); yield break; }
                if (music == null)
                {
                    music = gameObject.AddComponent<AudioSource>();
                    music.playOnAwake = false;
                    music.spatialBlend = 0f;
                    music.priority = 0;
                    music.ignoreListenerPause = true;
                }
                if (musicClip != null) Destroy(musicClip);
                musicClip = clip;
                musicVolume = volume;
                musicFadeFrom = -1f;
                music.clip = clip;
                music.volume = volume;
                music.Play();
                musicEndsAt = Time.unscaledTime + clip.length;
                log?.LogInfo("[Scenario] music '" + file + "' playing, " + clip.length.ToString("0") + " s.");
            }
        }

        /// <summary>The local pilot's state, turned into the triggers a script can wait for.</summary>
        private void Watch(float now)
        {
            if (!joined && GameAccess.TryGetLocalFaction(out FactionHQ hq))
            {
                int faction = PortraitFactions.Of(hq);
                if (faction >= 0)
                {
                    joined = true;
                    runner.Side = faction == 0 ? ScenarioSide.Bdf : ScenarioSide.Pala;
                    runner.Raise(TriggerKind.Join);
                }
            }
            bool flying = GameManager.GetLocalAircraft(out Aircraft own) && own != null && !own.disabled;
            if (flying && !hadAircraft)
            {
                runner.Raise(TriggerKind.Spawn);
                airborne = false;
                changedAt = now;
            }
            hadAircraft = flying;
            if (!flying) return;
            // Wheels have to stay off (or on) the ground for a moment: a bounce is not a take-off.
            if (own.IsLanded() == !airborne) { changedAt = now; return; }
            if (now - changedAt < SettleSeconds) return;
            airborne = !airborne;
            changedAt = now;
            runner.Raise(airborne ? TriggerKind.Airborne : TriggerKind.Landed);
        }

        // ------------------------------------------------------------------ IScenarioStage

        public bool CutPlaying => cutPlaying || choosing;

        public void Choose()
        {
            if (joined) return;
            choosing = true;
            view.Card("Choose your contract", "Press 1 to fly for the Republic.     Press 2 to fly for PALA.", 100000f);
        }

        /// <summary>
        /// Spawns the pilot's aircraft in the air, armed with the type's first standard loadout. Only the host can spawn; anybody else picks an
        /// aircraft the usual way.
        /// </summary>
        public void Seat(string aircraft, float x, float y, float z, float heading, float speed)
        {
            if (!GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player) || player.HQ == null || player.Aircraft != null) return;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer || Encyclopedia.i == null) return;
            AircraftDefinition type = null;
            foreach (AircraftDefinition known in Encyclopedia.i.aircraft)
                if (known != null && known.unitPrefab != null && string.Equals(known.unitName, aircraft, StringComparison.OrdinalIgnoreCase)) type = known;
            if (type == null) { log?.LogWarning("[Scenario] no aircraft is called '" + aircraft + "'."); return; }
            Loadout loadout = null;
            float fuel = 1f;
            StandardLoadout[] standard = type.aircraftParameters != null ? type.aircraftParameters.StandardLoadouts : null;
            if (standard != null)
                foreach (StandardLoadout option in standard)
                    if (option != null && !option.disabled && option.loadout != null) { loadout = option.loadout; fuel = option.FuelRatio > 0f ? option.FuelRatio : 1f; break; }
            Quaternion rotation = Quaternion.Euler(0f, heading, 0f);
            spawner.SpawnAircraft(player, type.unitPrefab, loadout, fuel, default, new GlobalPosition(x, y, z), rotation, rotation * Vector3.forward * speed, null,
                player.HQ, "Scenario_pilot_" + player.NetId, 0.5f, 0.5f);
            log?.LogInfo("[Scenario] the pilot is seated in a " + type.unitName + ".");
        }

        /// <summary>Joins a side the way the game's join screen does, and closes that screen if it is up.</summary>
        public void Enlist(ScenarioSide side)
        {
            if (choosing) view.Card(null, null, 0.01f);
            choosing = false;
            if (joined || !GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player)) return;
            FactionHQ wanted = null;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                if (PortraitFactions.Of(hq) == (side == ScenarioSide.Bdf ? 0 : 1)) wanted = hq;
            if (wanted == null) { log?.LogWarning("[Scenario] this mission has no " + side + " side to join."); return; }
            player.SetFaction(wanted);
            if (SceneSingleton<DynamicMap>.i != null) SceneSingleton<DynamicMap>.i.SetFaction(wanted);
            hidden = null;
            NuclearOption.UI.LeaderboardMenu menu = FindObjectOfType<NuclearOption.UI.LeaderboardMenu>();
            if (menu != null) menu.Close();
            else if (SceneSingleton<GameplayUI>.i != null) SceneSingleton<GameplayUI>.i.CloseJoinMenu();
        }

        public void Say(ScenarioCast cast, string text, float seconds)
        {
            ChatterNet net = Net(cast.Net);
            view.Say(cast.Callsign, cast.Role, Tint(net), text, seconds);
#if !PUBLIC_RELEASE
            linesSaid++;
            lastLine = cast.Callsign + ": " + text;
#endif
            if (GameAssets.i != null && GameAssets.i.radioStatic != null) SoundManager.PlayInterfaceOneShot(GameAssets.i.radioStatic);
            if (!ModuleServices.TryGet(out IChatter chatter) || chatter.Director == null) return;
            // The radio keeps quiet under a scripted line, and the line goes in its log like any other.
            chatter.Director.Hold(chatter.Now, seconds);
            chatter.Director.Log.Push(new ChatterAired
            {
                Time = chatter.Now, Net = net, Priority = ChatterPriority.Tactical, Speaker = cast.Callsign, SpeakerId = "scenario:" + cast.Id,
                Event = "scenario", Text = text, Seconds = seconds
            });
        }

        public void Card(string title, string text, float seconds) => view.Card(title, text, seconds);

        public void Hint(string text, float seconds) => view.Hint(text, seconds);

        public bool Cut(IReadOnlyList<ScenarioCut> cuts)
        {
            if (!settings.Cutscenes.Value || !ModuleServices.TryGet(out camera)) return false;
            stageShots.Clear();
            playing.Clear();
            foreach (ScenarioCut cut in cuts)
            {
                ScenarioShot shot = cut.Shot;
                stageShots.Add(new StageShot
                {
                    Kind = shot.Kind == ShotKind.OrbitAirbase ? StageShotKind.OrbitAirbase : shot.Kind == ShotKind.OrbitUnit ? StageShotKind.OrbitUnit :
                        shot.Kind == ShotKind.OrbitPoint ? StageShotKind.OrbitPoint : shot.Kind == ShotKind.Chase ? StageShotKind.Chase : StageShotKind.Fly,
                    Target = shot.Target, X = shot.X, Y = shot.Y, Z = shot.Z, ToX = shot.ToX, ToY = shot.ToY, ToZ = shot.ToZ,
                    LookX = shot.LookX, LookY = shot.LookY, LookZ = shot.LookZ, Radius = shot.Radius, Height = shot.Height,
                    FromDegrees = shot.From, ToDegrees = shot.To, Seconds = shot.Seconds, Fov = shot.Fov
                });
                playing.Add(cut);
            }
            // The stage may finish inside Play (every shot blocked): the flags are set first so that end is seen.
            cutPlaying = true;
            shotsShown = 0;
            skippedShots = 0;
            hidden = null;
            if (camera.Play(stageShots.ToArray(), ShotStarted, CutEnded) && shotsShown > 0) return true;
            if (cutPlaying) CutEnded();
            return false;
        }

        private void ShotStarted(int index)
        {
            if (shotsShown++ == 0)
            {
                view.BeginCut();
#if !PUBLIC_RELEASE
                cutsPlayed++;
#endif
            }
#if !PUBLIC_RELEASE
            shotsPlayed++;
#endif
            if (index < 0 || index >= playing.Count) return;
            ScenarioCut cut = playing[index];
            if (shotsShown + skippedShots <= index)
            {
                // The stage starts the shots in order, so a gap is a shot it could not play.
                log?.LogInfo("[Scenario] " + (index + 1 - shotsShown - skippedShots) + " shot(s) before '" + cut.Shot.Id + "' were skipped: path blocked or target missing.");
                skippedShots = index + 1 - shotsShown;
            }
            if (!view.Caption(cut.Title, cut.Caption, cut.Stamp, Picture(cut.Photo))) return;
            AudioClip flip = SfxClips.Get("page-flip");
            if (flip != null) SoundManager.PlayInterfaceOneShot(flip);
        }

        /// <summary>A script's picture as a texture, read once a mission: one of the mod's event paintings, or an image file in the scripts folder.</summary>
        private Texture2D Picture(ScenarioPicture picture)
        {
            if (picture == null) return null;
            if (pictures.TryGetValue(picture, out Texture2D texture)) return texture;
            try
            {
                byte[] bytes = null;
                if (picture.Poster != null)
                    bytes = EmbeddedResources.ReadAll(typeof(ScenarioHost).Assembly, "BoscaliSummer.EventsArt." + picture.Poster + ".png", MaxPictureBytes);
                else
                {
                    string file = Path.Combine(library.Folder, picture.File);
                    if (File.Exists(file) && new FileInfo(file).Length <= MaxPictureBytes) bytes = File.ReadAllBytes(file);
                }
                if (bytes != null && bytes.Length > 0)
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                    if (!texture.LoadImage(bytes, true)) { Destroy(texture); texture = null; }
                }
            }
            catch (IOException) { texture = null; }
            catch (UnauthorizedAccessException) { texture = null; }
            if (texture == null) log?.LogWarning("[Scenario] picture '" + picture.Id + "' could not be read.");
            pictures[picture] = texture;
            return texture;
        }

        public void Dip() => view.Dip();

        public void Sky(string name)
        {
            // The weather is the host's: it reaches every player the way the weather console's does.
            if (!GameAccess.IsServer()) return;
            if (ModuleServices.TryGet(out IWeatherDirector weather))
            {
                if (name == "clear") weather.Resume();
                else if (!weather.Hold(name)) log?.LogInfo("[Scenario] the weather did not take the sky '" + name + "'.");
                return;
            }
            // Without the weather module there is only the game's one weather value.
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null) return;
            float mission = MissionManager.CurrentMission?.environment?.weatherIntensity ?? 0.2f;
            level.Networkconditions = name == "clear" ? mission : name == "fog" || name == "wave" ? 0.5f : 0.9f;
        }

        public void Clock(float hour)
        {
            if (GameAccess.IsServer() && NetworkSceneSingleton<LevelInfo>.i != null) NetworkSceneSingleton<LevelInfo>.i.SetTimeOfDay(hour);
        }

        private void CutEnded()
        {
            cutPlaying = false;
            if (shotsShown > 0 && shotsShown < playing.Count)
                log?.LogInfo("[Scenario] cutscene ended after " + shotsShown + " of " + playing.Count + " shots.");
            if (shotsShown > 0 && view != null) view.EndCut();
            ShowGameUi();
        }

        public void Remember(string flag)
        {
            if (script != null && library.Progress.Set(script.Saga, flag)) library.SaveProgress();
        }

        public void Finish()
        {
            if (script == null || !library.Progress.MarkDone(script.Id)) return;
            library.SaveProgress();
            log?.LogInfo("[Scenario] '" + script.Id + "' finished.");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// A cutscene plays with the game's own screens out of the picture. The game shows exactly one of its two canvases at a time, so the one seen switched on
        /// is the one to give back; it is checked every frame because the join screen may come up while the cutscene runs.
        /// </summary>
        private void HideGameUi()
        {
            GameplayUI ui = SceneSingleton<GameplayUI>.i;
            if (ui == null) return;
            if (ui.menuCanvas != null && ui.menuCanvas.enabled) { ui.menuCanvas.enabled = false; hidden = GameplayUI.ActiveCanvas.Menu; }
            if (ui.gameplayCanvas != null && ui.gameplayCanvas.enabled) { ui.gameplayCanvas.enabled = false; hidden = GameplayUI.ActiveCanvas.Gameplay; }
        }

        private bool feedHidden;

        /// <summary>The game's kill and message feed sits on its own layer, over a cutscene; it is faded out for the length of one.</summary>
        private void ShowFeed(bool show)
        {
            if (feedHidden == !show) return;
            MessageUI feed = SceneSingleton<MessageUI>.i;
            if (feed == null) return;
            feedHidden = !show;
            CanvasGroup group = feed.GetComponent<CanvasGroup>();
            if (group == null) group = feed.gameObject.AddComponent<CanvasGroup>();
            group.alpha = show ? 1f : 0f;
        }

        private void ShowGameUi()
        {
            if (!hidden.HasValue) return;
            GameplayUI ui = SceneSingleton<GameplayUI>.i;
            if (ui != null && ui.menuCanvas != null && ui.gameplayCanvas != null && !ui.menuCanvas.enabled && !ui.gameplayCanvas.enabled)
                ui.SetActiveCanvas(hidden.Value);
            hidden = null;
        }

        private static ChatterNet Net(string net)
        {
            switch (net)
            {
                case "awacs": return ChatterNet.Awacs;
                case "tower": return ChatterNet.Tower;
                case "wing": return ChatterNet.Wing;
                case "enemy": return ChatterNet.Enemy;
                default: return ChatterNet.Command;
            }
        }

        private static Color Tint(ChatterNet net)
        {
            switch (net)
            {
                case ChatterNet.Awacs: return AvTheme.RailInfo;
                case ChatterNet.Tower: return AvTheme.Friendly;
                case ChatterNet.Wing: return AvTheme.Selected;
                case ChatterNet.Enemy: return AvTheme.Hostile;
                default: return AvTheme.Accent;
            }
        }

        public void ResetForScene() => Unbind();

        private void OnDisable() => Unbind();

        private void OnDestroy()
        {
#if !PUBLIC_RELEASE
            if (ReferenceEquals(Instance, this)) Instance = null;
#endif
            MissionProfile.Preparing -= Listen;
            MissionManager.onObjectiveStarted -= ObjectiveStarted;
            MissionManager.onObjectiveComplete -= ObjectiveDone;
            Unbind();
        }
    }
}
