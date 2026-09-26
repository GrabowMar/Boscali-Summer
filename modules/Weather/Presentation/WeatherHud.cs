using System.Text;
using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Presentation
{
    /// <summary>
    /// The weather line on the common HUD element, and nothing else: the sky's own word for
    /// itself with the wind the aircraft is in, then the gust and the one storm worth naming —
    /// its kind, range and bearing. Colours come from the live vanilla theme, so a custom theme
    /// carries over and a warning reads in the same ink as the game's own warnings.
    ///
    /// <para>A screen-space line, so it claims no bezel and patches nothing. It stops refreshing
    /// when the module, the setting or the snapshot says it should, and the board drops the line
    /// by itself. Ten hertz, and nothing allocated after the build.</para>
    /// </summary>
    internal sealed class WeatherHud : MonoBehaviour, ISceneService
    {
        private const string Owner = "weather";
        private const string Channel = "weather";
        private const string LineKey = "conditions";

        /// <summary>Ten hertz: fast enough for a distance readout, cheap enough to ignore.</summary>
        private const float TickSeconds = 0.1f;

        /// <summary>
        /// The line's strings are rebuilt on this bucket rather than every tick: the board keeps
        /// whatever it was last handed, and a one-second stale range is not a stale sky.
        /// </summary>
        private const float TextBucketSeconds = 1f;

        private WeatherSettings settings;
        private WeatherManager manager;
        private ManualLogSource log;

        /// <summary>One buffer for the primed line, reused by every tick.</summary>
        private readonly StringBuilder text = new StringBuilder(160);

        private IHudBoard board;
        private IHudLine line;
        private bool loggedInstall;
        private float nextTick;
        private float nextTextAt;
        private string cachedLine;
        private string cachedDetail;

        public void Configure(WeatherSettings weatherSettings, WeatherManager weatherManager, ManualLogSource logger)
        {
            settings = weatherSettings;
            manager = weatherManager;
            log = logger;
        }

        public void ResetForScene()
        {
            ReleaseLine();
            loggedInstall = false;
            nextTick = 0f;
            nextTextAt = 0f;
            cachedLine = null;
            cachedDetail = null;
        }

        private void OnDestroy() => ReleaseLine();

        private void Update()
        {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + TickSeconds;
            Refresh();
        }

        private void Refresh()
        {
            WeatherSnapshot snapshot = manager != null ? WeatherView.Capture(manager, 1) : WeatherSnapshot.Unavailable;
            if (!CanShow(snapshot) || !TryPlayerPosition(out float playerX, out float playerZ))
            {
                ReleaseLine();
                return;
            }

            if (!ModServices.TryGet(out board)) return;
            board.DeclareChannel(Channel, "WEATHER");
            if (line == null)
            {
                line = board.Acquire(Owner, Channel, LineKey);
                if (line == null) return;
                if (!loggedInstall)
                {
                    loggedInstall = true;
                    log?.LogInfo("Weather line installed on the common HUD element.");
                }
            }

            Apply(snapshot, playerX, playerZ);
        }

        /// <summary>
        /// Stop refreshing. The board's own visibility rule and staleness window take it from
        /// here, so hiding is one call rather than a tree of SetActive paths.
        /// </summary>
        private void ReleaseLine()
        {
            if (line == null) return;
            line.Release();
            line = null;
        }

        private bool CanShow(WeatherSnapshot snapshot)
        {
            if (Application.isBatchMode || settings == null || manager == null) return false;
            if (!settings.Enabled.Value || !settings.Hud.Value) return false;
            return snapshot.Available;
        }

        /// <summary>Same reading the WEA panel passes to its radar page.</summary>
        private static bool TryPlayerPosition(out float x, out float z)
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null)
            {
                x = 0f;
                z = 0f;
                return false;
            }

            Vector3 position = cameras.transform.position;
            x = position.x;
            z = position.z;
            return !float.IsNaN(x) && !float.IsNaN(z);
        }

        // ---- Tick ------------------------------------------------------------------------

        private void Apply(WeatherSnapshot snapshot, float playerX, float playerZ)
        {
            bool forced = manager != null && WeatherView.OverrideActive(manager);
            HudTone tone = forced ? RegimeTone(snapshot.Live.Regime) : WarningTone(snapshot.Warning);

            if (cachedLine == null || Time.unscaledTime >= nextTextAt)
            {
                nextTextAt = Time.unscaledTime + TextBucketSeconds;

                // The one cell worth naming: the tier's own source when there is a warning, else the
                // nearest supercell. Never invented, and never a zero when there is nothing.
                StormCell source = snapshot.WarningSource;
                bool hasCell = snapshot.Warning != StormWarning.None;
                if (!hasCell)
                {
                    hasCell = StormReadout.Nearest(snapshot.Cells, snapshot.CellCount, playerX, playerZ,
                        out source, out _);
                }

                float distance = hasCell ? source.DistanceTo(playerX, playerZ) : 0f;

                // Line one: the sky's own word for itself, then the wind the aircraft is in.
                text.Length = 0;
                text.Append(WeatherReadout.Regime(snapshot.Live.Regime));
                text.Append("  ·  WIND ");
                text.Append(WeatherReadout.Wind(snapshot.LocalWindSpeed, snapshot.LocalWindHeading));

                cachedLine = text.ToString();
                cachedDetail = Detail(snapshot, hasCell, source, distance, playerX, playerZ);
            }

            line.Set(tone, cachedLine, cachedDetail, 0f);
        }

        /// <summary>
        /// Line two: the gust, then either the storm that matters or an honest nothing. The
        /// bearing is printed as well as carried by the tone, so a colour-blind reading and a
        /// glance both work.
        /// </summary>
        private static string Detail(
            WeatherSnapshot snapshot, bool hasCell, in StormCell source, float distance, float playerX, float playerZ)
        {
            string hazard = hasCell
                ? StormReadout.HazardLine(
                    snapshot.Warning == StormWarning.None ? StormWarning.Advisory : snapshot.Warning,
                    StormReadout.Kind(source.Kind), distance,
                    StormReadout.BearingTo(playerX, playerZ, source.X, source.Z))
                : "NO STORM IN RANGE";

            if (!snapshot.Atmosphere.Available) return hazard;
            return "GUST " + WeatherReadout.Speed(snapshot.Atmosphere.GustSpeed) + "  ·  " + hazard;
        }

        /// <summary>Vanilla's own severity ladder: all-clear, caution, alert.</summary>
        private static HudTone WarningTone(StormWarning warning)
        {
            if (warning == StormWarning.Warning) return HudTone.Warning;
            return warning == StormWarning.None ? HudTone.Info : HudTone.Caution;
        }

        private static HudTone RegimeTone(WeatherRegime regime)
        {
            switch (WeatherReadout.Severity(regime))
            {
                case 3: return HudTone.Warning;
                case 2: return HudTone.Caution;
                default: return HudTone.Info;
            }
        }
    }
}
