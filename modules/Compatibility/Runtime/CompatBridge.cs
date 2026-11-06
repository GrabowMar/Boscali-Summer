using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Compatibility.Configuration;
using UnityEngine;

namespace BoscaliSummer.Modules.Compatibility.Runtime
{
    /// <summary>
    /// Shares the game with NoAtmoFX and Kelly's Client Effects (2026-11-06). Twice a second: finds the plugins
    /// (they may load after us), sets the <see cref="ForeignMods"/> yields our own effects read, and - only when the
    /// picture changes (a mod appears, a SET choice moves) - writes the other side's switches so an effect is never
    /// drawn twice. Every five seconds it drives NoAtmoFX's weather from ours. Their config entries are written
    /// through their own BepInEx files, by key name: a renamed key is skipped with a log line, never an error.
    /// </summary>
    internal sealed class CompatBridge : MonoBehaviour, ISceneService
    {
        private CompatibilitySettings settings;
        private ClientSettingsBoard board;
        private ManualLogSource log;
        private string applied;
        private float nextTick, nextLink;
        private bool linkWritten;

        internal void Configure(CompatibilitySettings compat, ClientSettingsBoard keys, ManualLogSource logger)
        {
            settings = compat;
            board = keys;
            log = logger;
        }

        internal static bool KellyInstalled =>
            ForeignMods.Find(ForeignMods.KellyKrop) != null || ForeignMods.Find(ForeignMods.KellyAirfield) != null ||
            ForeignMods.Find(ForeignMods.KellyMissileBoom) != null || ForeignMods.Find(ForeignMods.KellyControls) != null;

        /// <summary>NoAtmoFX's 0 (fair) .. 1 (storm) conditions from our sky cover and rain at a point.</summary>
        internal static float Conditions(float cover01, float rain01) => Mathf.Clamp01(cover01 * .72f + rain01 * .4f);

        private void Update()
        {
            if (settings == null || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + .5f;
            BaseUnityPlugin noAtmo = settings.NoAtmoFxLink.Value ? ForeignMods.Find(ForeignMods.NoAtmoFx) : null;
            bool kelly = settings.KellyLink.Value;
            BaseUnityPlugin krop = kelly ? ForeignMods.Find(ForeignMods.KellyKrop) : null;
            BaseUnityPlugin field = kelly ? ForeignMods.Find(ForeignMods.KellyAirfield) : null;
            BaseUnityPlugin boom = kelly ? ForeignMods.Find(ForeignMods.KellyMissileBoom) : null;
            bool kellyDraws = settings.Overlaps.Value == OverlapOwner.Kelly;

            ForeignMods.SkyYielded = noAtmo != null && settings.NoAtmoFxSky.Value && Get(noAtmo, "General", "Enabled", true);
            ForeignMods.RainYielded = noAtmo != null && settings.Rain.Value == RainOwner.NoAtmoFx;
            ForeignMods.FrostYielded = noAtmo != null && Get(noAtmo, "Glass", "WetGlass", true);
            ForeignMods.WorldFxYielded = krop != null && kellyDraws;
            ForeignMods.MuffleYielded = krop != null && Get(krop, "01 Audio filters", "First-person cockpit muffling", true);
            ForeignMods.AirfieldYielded = field != null && kellyDraws;
            ForeignMods.MissileBoomYielded = boom != null && kellyDraws;

            string picture = (noAtmo != null) + "|" + settings.Rain.Value + "|" + settings.MoveKeys.Value + "|" +
                (krop != null) + (field != null) + (boom != null) + "|" + settings.Overlaps.Value;
            if (picture != applied)
            {
                applied = picture;
                Push(noAtmo, krop, field, boom, kellyDraws);
            }
            Link(noAtmo);
        }

        private void Push(BaseUnityPlugin noAtmo, BaseUnityPlugin krop, BaseUnityPlugin field, BaseUnityPlugin boom, bool kellyDraws)
        {
            if (noAtmo != null)
            {
                // 2 = never / the other source (Nuclear Rain, not installed): its glass keeps cloud mist and frost only.
                int rain = settings.Rain.Value == RainOwner.Boscali ? 2 : 0;
                Set(noAtmo, "Atmosphere", "NearRain", rain);
                Set(noAtmo, "Glass", "RainSource", rain);
                if (settings.MoveKeys.Value) MoveKeys(noAtmo);
            }
            Set(krop, "05 Contrails", "Aircraft contrails", kellyDraws);
            Set(krop, "04 Low-flight wake", "Air wake effects", kellyDraws);
            Set(krop, "02 Bullet near misses", "Bullet cracks", kellyDraws);
            Set(field, "Airfield lighting", "Enabled", kellyDraws);
            Set(boom, "Audio", "Missile sonic boom", kellyDraws);
        }

        /// <summary>A plain NoAtmoFX key on one of ours moves to Ctrl+key (Alt+F8: Kelly's panel owns Ctrl+F8).</summary>
        private void MoveKeys(BaseUnityPlugin plugin)
        {
            var ours = new HashSet<KeyCode>();
            if (board != null) foreach (ClientKeySetting k in board.Keys) ours.Add(k.Entry.Value);
            foreach (var (section, key) in new[] { ("General", "ToggleKey"), ("General", "MenuKey"), ("Nuclear", "TestBlastKey") })
            {
                if (!plugin.Config.TryGetEntry(section, key, out ConfigEntry<KeyboardShortcut> entry)) continue;
                KeyboardShortcut shortcut = entry.Value;
                if (shortcut.MainKey == KeyCode.None || shortcut.Modifiers.Any() || !ours.Contains(shortcut.MainKey)) continue;
                KeyCode modifier = shortcut.MainKey == KeyCode.F8 ? KeyCode.LeftAlt : KeyCode.LeftControl;
                entry.Value = new KeyboardShortcut(shortcut.MainKey, modifier);
                log?.LogInfo("[Compatibility] NoAtmoFX " + key + " clashed with a Boscali key; moved to " + entry.Value + ".");
            }
        }

        private void Link(BaseUnityPlugin noAtmo)
        {
            if (Time.unscaledTime < nextLink) return;
            nextLink = Time.unscaledTime + 5f;
            ConfigEntry<float> entry = null;
            float value = -1f;
            if (noAtmo != null && settings.WeatherLink.Value && noAtmo.Config.TryGetEntry("Weather", "ConditionsOverride", out entry))
            {
                Camera cam = SceneSingleton<CameraStateManager>.i?.mainCamera;
                if (cam != null && ModuleServices.TryGet(out IWeatherView view) && !(view is Object lost && lost == null))
                {
                    GlobalPosition at = cam.transform.position.ToGlobalPosition();
                    if (view.TrySample(at.x, at.z, out WeatherViewSample sky)) value = Conditions(sky.Cover, sky.Rain01);
                }
            }
            // The link may have just been switched off, so the entry is looked up whatever the settings say.
            if (value < 0f) { Unlink(entry ?? Entry(ForeignMods.Find(ForeignMods.NoAtmoFx))); return; }
            if (Mathf.Abs(entry.Value - value) < .02f) return;
            // Not saved on every nudge: NoAtmoFX smooths a change over minutes, and its file keeps the mission's -1.
            ConfigFile file = noAtmo.Config;
            bool save = file.SaveOnConfigSet;
            file.SaveOnConfigSet = false;
            try { entry.Value = value; }
            finally { file.SaveOnConfigSet = save; }
            linkWritten = true;
        }

        private static ConfigEntry<float> Entry(BaseUnityPlugin noAtmo) =>
            noAtmo != null && noAtmo.Config.TryGetEntry("Weather", "ConditionsOverride", out ConfigEntry<float> e) ? e : null;

        /// <summary>Hands NoAtmoFX back the mission's own weather (-1), saved, if we were driving it.</summary>
        private void Unlink(ConfigEntry<float> entry)
        {
            if (!linkWritten) return;
            linkWritten = false;
            if (entry != null) entry.Value = -1f;
        }

        private static bool Get(BaseUnityPlugin plugin, string section, string key, bool fallback) =>
            plugin != null && plugin.Config.TryGetEntry(section, key, out ConfigEntry<bool> e) ? e.Value : fallback;

        private void Set<T>(BaseUnityPlugin plugin, string section, string key, T value)
        {
            if (plugin == null) return;
            if (!plugin.Config.TryGetEntry(section, key, out ConfigEntry<T> entry))
            {
                log?.LogInfo("[Compatibility] " + plugin.Info.Metadata.GUID + " has no " + section + "/" + key + "; left alone.");
                return;
            }
            if (EqualityComparer<T>.Default.Equals(entry.Value, value)) return;
            entry.Value = value;
            log?.LogInfo("[Compatibility] " + plugin.Info.Metadata.GUID + " " + section + "/" + key + " = " + value + ".");
        }

        public void ResetForScene() => nextTick = nextLink = 0f;

        private void OnApplicationQuit() => Unlink(Entry(ForeignMods.Find(ForeignMods.NoAtmoFx)));

        private void OnDestroy()
        {
            Unlink(Entry(ForeignMods.Find(ForeignMods.NoAtmoFx)));
            ForeignMods.ClearYields();
        }
    }
}
