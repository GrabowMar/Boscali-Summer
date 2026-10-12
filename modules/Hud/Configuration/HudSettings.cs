using BepInEx.Configuration;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Modules.Hud.Configuration
{
    /// <summary>
    /// The TAC BOARD's pilot-facing options (config section "TacBoard"). Client-local presentation only: nothing here changes what the board is told, who is
    /// authoritative, or anything on the wire, and every option applies live. The board reads them through <see cref="Snapshot"/> once per 10 Hz tick; other modules
    /// change them through <see cref="ITacBoardSettings"/>. A new section, not the retired "Hud" one, so no stale value in an old config file is adopted.
    /// </summary>
    internal sealed class HudSettings : ITacBoardSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<BoardLayout> Layout { get; }
        public ConfigEntry<float> Scale { get; }
        public ConfigEntry<float> OffsetX { get; }
        public ConfigEntry<float> OffsetY { get; }
        public ConfigEntry<float> Opacity { get; }
        public ConfigEntry<float> Shade { get; }
        public ConfigEntry<BoardDeclutter> Declutter { get; }
        public ConfigEntry<BoardScopeMode> ScopeMode { get; }
        public ConfigEntry<bool> PageThreat { get; }
        public ConfigEntry<bool> PageShot { get; }
        public ConfigEntry<bool> PageMissileCam { get; }
        public ConfigEntry<bool> PageApproach { get; }
        public ConfigEntry<bool> PageWing { get; }
        public ConfigEntry<bool> PageStrike { get; }
        public ConfigEntry<bool> PageDock { get; }
        public ConfigEntry<bool> PageObjective { get; }
        public ConfigEntry<bool> PageSystems { get; }
        public ConfigEntry<float> ThreatHoldSeconds { get; }
        public ConfigEntry<float> PageMinSeconds { get; }
        public ConfigEntry<bool> ShowLamps { get; }
        public ConfigEntry<bool> ShowSystemsStrip { get; }
        public ConfigEntry<bool> ShowGauges { get; }
        public ConfigEntry<bool> ShowTicker { get; }
        public ConfigEntry<bool> Sway { get; }
        public ConfigEntry<bool> Corners { get; }

        public HudSettings(ConfigFile config)
        {
            const string section = "TacBoard";
            const string live = " Applies now; no restart.";
            BoardOptions d = BoardOptions.Default;

            Enabled = config.Bind(section, "Enabled", d.Enabled,
                "Master switch for the TAC BOARD; off hides it and gives back the game's own weapon, countermeasure and power panels." + live);

            Layout = config.Bind(section, "Layout", d.Layout,
                "Wide spreads the board along the top edge of the screen; Stack is the classic narrow column." + live);

            Scale = config.Bind(section, "Scale", d.Scale,
                new ConfigDescription("Size multiplier for the whole board." + live,
                    new AcceptableValueRange<float>(BoardOptions.ScaleMin, BoardOptions.ScaleMax)));

            OffsetX = config.Bind(section, "OffsetX", d.OffsetX,
                new ConfigDescription("How far the board is moved left from the right screen edge, in units." + live,
                    new AcceptableValueRange<float>(0f, BoardOptions.OffsetXMax)));

            OffsetY = config.Bind(section, "OffsetY", d.OffsetY,
                new ConfigDescription("How far the board is moved down from the top screen edge, in units." + live,
                    new AcceptableValueRange<float>(0f, BoardOptions.OffsetYMax)));

            Opacity = config.Bind(section, "Opacity", d.Opacity,
                new ConfigDescription("Ink opacity of the whole board, from faint (0.25) to solid (1)." + live,
                    new AcceptableValueRange<float>(BoardOptions.OpacityMin, 1f)));

            Shade = config.Bind(section, "Shade", d.Shade,
                new ConfigDescription("Strength of the dark band behind the symbology; 0 draws no band." + live,
                    new AcceptableValueRange<float>(0f, 1f)));

            Declutter = config.Bind(section, "Declutter", d.Declutter,
                "How much the board shows: Full is everything the switches allow, Lean drops the gauge row, dark lamps and captions, Minimal keeps only the lock, spike and missile lamps and the pages that matter in a fight." + live);

            ScopeMode = config.Bind(section, "ScopeMode", d.ScopeMode,
                "Auto shows an information page when it is relevant; Off shows no pages at all." + live);

            PageThreat = config.Bind(section, "PageThreat", d.PageThreat,
                "Show the radar-warning page when something is locking, painting or launching at you." + live);
            PageShot = config.Bind(section, "PageShot", d.PageShot,
                "Show the shot page that follows your own missiles and bombs from launch to result." + live);
            PageMissileCam = config.Bind(section, "PageMissileCam", d.PageMissileCam,
                "Show the missile-camera page while the camera rides one of your own missiles." + live);
            PageApproach = config.Bind(section, "PageApproach", d.PageApproach,
                "Show the approach page: the runway funnel for an ILS or autoland approach." + live);
            PageWing = config.Bind(section, "PageWing", d.PageWing,
                "Show the wing page: your wingmen's formation, fuel, ammunition and orders." + live);
            PageStrike = config.Bind(section, "PageStrike", d.PageStrike,
                "Show the strike page: your own strikes in the air, recon windows and cyber intrusions." + live);
            PageDock = config.Bind(section, "PageDock", d.PageDock,
                "Show the dock page while you hook up to a SKYWELL tanker." + live);
            PageObjective = config.Bind(section, "PageObjective", d.PageObjective,
                "Show the objective page for your selected contract: its zone, deadline and air defences." + live);
            PageSystems = config.Bind(section, "PageSystems", d.PageSystems,
                "Show the systems page; it is also the cruise strip and the home of fire, engine, fuel and structure warnings." + live);

            ThreatHoldSeconds = config.Bind(section, "ThreatHoldSeconds", d.ThreatHoldSeconds,
                new ConfigDescription("How many seconds the radar-warning page keeps a contact after its last warning." + live,
                    new AcceptableValueRange<float>(BoardOptions.HoldMin, BoardOptions.HoldMax)));

            PageMinSeconds = config.Bind(section, "PageMinSeconds", d.PageMinSeconds,
                new ConfigDescription("Minimum seconds a page holds the board's page area once it has appeared; the SYSTEMS page is exempt." + live,
                    new AcceptableValueRange<float>(0f, BoardOptions.DwellMax)));

            ShowLamps = config.Bind(section, "ShowLamps", d.ShowLamps,
                "Show the lamp row: the aircraft's switch states and the threat lamps." + live);
            ShowSystemsStrip = config.Bind(section, "ShowSystemsStrip", d.ShowSystemsStrip,
                "Show the compact systems strip the page area falls back to in cruise; it needs the SYSTEMS page." + live);
            ShowGauges = config.Bind(section, "ShowGauges", d.ShowGauges,
                "Show the gauge row under the board's page area." + live);
            ShowTicker = config.Bind(section, "ShowTicker", d.ShowTicker,
                "Show the notice line at the foot of the board." + live);

            Sway = config.Bind(section, "Sway", d.Sway,
                "The symbology lags slightly against head movement, as if it hung in the air a little way off the glass." + live);

            Corners = config.Bind(section, "Corners", d.Corners,
                "Restyle the kill feed and the map frame in the board's look." + live);
        }

        /// <summary>Fill <paramref name="into"/> from the config entries, kept inside their ranges. Reads fields only: it allocates nothing.</summary>
        public void Snapshot(ref BoardOptions into)
        {
            into.Enabled = Enabled.Value;
            into.Layout = Layout.Value;
            into.Scale = Scale.Value;
            into.OffsetX = OffsetX.Value;
            into.OffsetY = OffsetY.Value;
            into.Opacity = Opacity.Value;
            into.Shade = Shade.Value;
            into.Declutter = Declutter.Value;
            into.ScopeMode = ScopeMode.Value;
            into.PageThreat = PageThreat.Value;
            into.PageShot = PageShot.Value;
            into.PageMissileCam = PageMissileCam.Value;
            into.PageApproach = PageApproach.Value;
            into.PageWing = PageWing.Value;
            into.PageStrike = PageStrike.Value;
            into.PageDock = PageDock.Value;
            into.PageObjective = PageObjective.Value;
            into.PageSystems = PageSystems.Value;
            into.ThreatHoldSeconds = ThreatHoldSeconds.Value;
            into.PageMinSeconds = PageMinSeconds.Value;
            into.ShowLamps = ShowLamps.Value;
            into.ShowSystemsStrip = ShowSystemsStrip.Value;
            into.ShowGauges = ShowGauges.Value;
            into.ShowTicker = ShowTicker.Value;
            into.Sway = Sway.Value;
            into.Corners = Corners.Value;
            into.Sanitize();
        }

        // ITacBoardSettings: the entries under the contract's names. Setters persist (the config file saves on every set).
        bool ITacBoardSettings.Enabled { get => Enabled.Value; set => Enabled.Value = value; }
        BoardLayout ITacBoardSettings.Layout { get => Layout.Value; set => Layout.Value = value; }
        float ITacBoardSettings.Scale { get => Scale.Value; set => Scale.Value = value; }
        float ITacBoardSettings.OffsetX { get => OffsetX.Value; set => OffsetX.Value = value; }
        float ITacBoardSettings.OffsetY { get => OffsetY.Value; set => OffsetY.Value = value; }
        float ITacBoardSettings.Opacity { get => Opacity.Value; set => Opacity.Value = value; }
        float ITacBoardSettings.Shade { get => Shade.Value; set => Shade.Value = value; }
        BoardDeclutter ITacBoardSettings.Declutter { get => Declutter.Value; set => Declutter.Value = value; }
        BoardScopeMode ITacBoardSettings.ScopeMode { get => ScopeMode.Value; set => ScopeMode.Value = value; }
        bool ITacBoardSettings.PageThreat { get => PageThreat.Value; set => PageThreat.Value = value; }
        bool ITacBoardSettings.PageShot { get => PageShot.Value; set => PageShot.Value = value; }
        bool ITacBoardSettings.PageMissileCam { get => PageMissileCam.Value; set => PageMissileCam.Value = value; }
        bool ITacBoardSettings.PageApproach { get => PageApproach.Value; set => PageApproach.Value = value; }
        bool ITacBoardSettings.PageWing { get => PageWing.Value; set => PageWing.Value = value; }
        bool ITacBoardSettings.PageStrike { get => PageStrike.Value; set => PageStrike.Value = value; }
        bool ITacBoardSettings.PageDock { get => PageDock.Value; set => PageDock.Value = value; }
        bool ITacBoardSettings.PageObjective { get => PageObjective.Value; set => PageObjective.Value = value; }
        bool ITacBoardSettings.PageSystems { get => PageSystems.Value; set => PageSystems.Value = value; }
        float ITacBoardSettings.ThreatHoldSeconds { get => ThreatHoldSeconds.Value; set => ThreatHoldSeconds.Value = value; }
        float ITacBoardSettings.PageMinSeconds { get => PageMinSeconds.Value; set => PageMinSeconds.Value = value; }
        bool ITacBoardSettings.ShowLamps { get => ShowLamps.Value; set => ShowLamps.Value = value; }
        bool ITacBoardSettings.ShowSystemsStrip { get => ShowSystemsStrip.Value; set => ShowSystemsStrip.Value = value; }
        bool ITacBoardSettings.ShowGauges { get => ShowGauges.Value; set => ShowGauges.Value = value; }
        bool ITacBoardSettings.ShowTicker { get => ShowTicker.Value; set => ShowTicker.Value = value; }
        bool ITacBoardSettings.Sway { get => Sway.Value; set => Sway.Value = value; }
        bool ITacBoardSettings.Corners { get => Corners.Value; set => Corners.Value = value; }

        /// <summary>Every entry back to the default it was bound with (the <see cref="BoardOptions.Default"/> values). The file is saved once, not once per entry.</summary>
        void ITacBoardSettings.ResetToDefaults()
        {
            ConfigEntryBase[] all =
            {
                Enabled, Layout, Scale, OffsetX, OffsetY, Opacity, Shade, Declutter, ScopeMode,
                PageThreat, PageShot, PageMissileCam, PageApproach, PageWing, PageStrike, PageDock, PageObjective, PageSystems,
                ThreatHoldSeconds, PageMinSeconds, ShowLamps, ShowSystemsStrip, ShowGauges, ShowTicker, Sway, Corners,
            };
            ConfigFile file = Enabled.ConfigFile;
            bool save = file.SaveOnConfigSet;
            file.SaveOnConfigSet = false;
            try { foreach (ConfigEntryBase entry in all) entry.BoxedValue = entry.DefaultValue; }
            finally { file.SaveOnConfigSet = save; }
            if (save) file.Save();
        }
    }
}
