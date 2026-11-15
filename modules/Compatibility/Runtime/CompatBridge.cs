using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private bool linkWritten, apiLooked;
        private MethodInfo apiConditions, apiBase, apiClear;

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
            BaseUnityPlugin expansion = settings.Expansion2082Link.Value ? ForeignMods.Find(ForeignMods.Expansion2082) : null;
            picture += "|" + (expansion != null) + settings.ShipHaze.Value + settings.ShipAaRules.Value;
            if (picture != applied)
            {
                applied = picture;
                Push(noAtmo, krop, field, boom, kellyDraws);
                Set(expansion, "Haze", "Sweat", settings.ShipHaze.Value);
                foreach (string key in new[] { "OneShotPerMissile", "MindYourOwn", "ShellsArentMissiles" })
                    Set(expansion, "Targeting", key, settings.ShipAaRules.Value);
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

        /// <summary>
        /// Every 5 s feeds NoAtmoFX (1.3+) our sky at the camera through its integration API (NoAtmoFX.NoAtmoFXApi,
        /// reflection, no DLL reference). The API input is a target its clouds glide to, ranks below the player's own
        /// F11 override and is sent from host to clients - the old ConditionsOverride cfg write sat in the player's
        /// menu slot and beat host sync. Older NoAtmoFX has no API and simply keeps the mission weather.
        /// </summary>
        private void Link(BaseUnityPlugin noAtmo)
        {
            if (Time.unscaledTime < nextLink) return;
            nextLink = Time.unscaledTime + 5f;
            if (noAtmo != null && settings.WeatherLink.Value && FindApi(noAtmo) && TrySky(out WeatherViewSample sky) &&
                Call(apiConditions, Conditions(sky.Cover, sky.Rain01)) && Call(apiBase, sky.CloudBase))
            {
                linkWritten = true;
                return;
            }
            Unlink();
        }

        private static bool TrySky(out WeatherViewSample sky)
        {
            sky = default;
            Camera cam = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (cam == null || !ModuleServices.TryGet(out IWeatherView view) || (view is Object lost && lost == null)) return false;
            GlobalPosition at = cam.transform.position.ToGlobalPosition();
            return view.TrySample(at.x, at.z, out sky);
        }

        private bool FindApi(BaseUnityPlugin noAtmo)
        {
            if (apiLooked) return apiConditions != null;
            apiLooked = true;
            System.Type api = noAtmo.GetType().Assembly.GetType("NoAtmoFX.NoAtmoFXApi");
            apiConditions = api?.GetMethod("SetConditions", new[] { typeof(float) });
            apiBase = api?.GetMethod("SetCloudBase", new[] { typeof(float) });
            apiClear = api?.GetMethod("ClearWeather", System.Type.EmptyTypes);
            if (apiConditions == null || apiBase == null || apiClear == null)
            {
                apiConditions = null;
                log?.LogInfo("[Compatibility] NoAtmoFX has no integration API (older than 1.3.0); weather link off.");
            }
            return apiConditions != null;
        }

        private bool Call(MethodInfo method, params object[] args)
        {
            try { method.Invoke(null, args); return true; }
            catch (System.Exception e)
            {
                log?.LogWarning("[Compatibility] NoAtmoFX " + method.Name + " failed; weather link off: " + (e.InnerException ?? e).Message);
                apiConditions = null;
                return false;
            }
        }

        /// <summary>Hands NoAtmoFX back the mission's own weather if we were driving it (its clouds glide back).</summary>
        private void Unlink()
        {
            if (!linkWritten) return;
            linkWritten = false;
            if (apiClear != null) Call(apiClear);
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

        private void OnApplicationQuit() => Unlink();

        private void OnDestroy()
        {
            Unlink();
            ForeignMods.ClearYields();
        }
    }
}
