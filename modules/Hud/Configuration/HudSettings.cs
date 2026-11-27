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
        public ConfigEntry<float> Shade { get; }
        public ConfigEntry<BoardDeclutter> Declutter { get; }
        public ConfigEntry<BoardScopeMode> ScopeMode { get; }
        public ConfigEntry<bool> ShowStores { get; }
        public ConfigEntry<bool> PageMissileCam { get; }
        public ConfigEntry<bool> TargetCam { get; }
        public ConfigEntry<bool> ShowLamps { get; }
        public ConfigEntry<bool> ShowGauges { get; }
        public ConfigEntry<bool> ShowTicker { get; }
        public ConfigEntry<bool> ShowSystems { get; }
        public ConfigEntry<bool> Sway { get; }
        public ConfigEntry<bool> Corners { get; }
        public ConfigEntry<bool> CentreAlerts { get; }
        public ConfigEntry<bool> ArPath { get; }
        public ConfigEntry<bool> ArDomes { get; }
        public ConfigEntry<bool> ArMissiles { get; }
        public ConfigEntry<bool> ArBeacons { get; }
        public ConfigEntry<bool> HudFx { get; }
        public ConfigEntry<bool> Brow { get; }
        public ConfigEntry<bool> Arcs { get; }
        public ConfigEntry<bool> Boot { get; }
        public ConfigEntry<bool> Reactive { get; }
        public ConfigEntry<float> VisorSound { get; }
        public ConfigEntry<float> TextEdge { get; }

        public HudSettings(ConfigFile config)
        {
            const string section = "TacBoard";
            const string live = " Applies now; no restart.";
            BoardOptions d = BoardOptions.Default;

            Enabled = config.Bind(section, "Enabled", d.Enabled,
                "Master switch for the TAC BOARD; off hides it and gives back the game's own weapon, countermeasure and power panels (the kill feed and map restyle has its own switch, Corners)." + live);

            Layout = config.Bind(section, "Layout", d.Layout,
                "Wide spreads the board along the top edge of the screen; Stack is the classic narrow column." + live);

            Scale = config.Bind(section, "Scale", d.Scale,
                new ConfigDescription("Size multiplier for the whole board." + live,
                    new AcceptableValueRange<float>(BoardOptions.ScaleMin, BoardOptions.ScaleMax)));
            // 0.2.5 made the board smaller by default; a file still holding the old untouched default 1 takes the new one, once.
            ConfigEntry<int> smallerBoard = config.Bind(section, "SmallerDefaultScale", 0,
                new ConfigDescription("Internal marker, leave as it is: 1 once a Scale left at the old default (1) has moved to the new one.", new AcceptableValueRange<int>(0, 1)));
            if (smallerBoard.Value < 1)
            {
                if (Scale.Value == 1f) Scale.Value = d.Scale;
                smallerBoard.Value = 1;
            }

            Shade = config.Bind(section, "Shade", d.Shade,
                new ConfigDescription("Strength of an optional dark backdrop behind every HUD block of the mod (the board, the left column, the TAC MAP, the wing strip, the centre alerts); 0 (the default) draws none and the HUD reads as glowing light on the visor." + live,
                    new AcceptableValueRange<float>(0f, 1f)));
            // 2026-11-15 turned the backdrop off by default (the HUD's words glow instead); a file still holding the old untouched default 0.45 takes the new one, once.
            ConfigEntry<int> noBackdrop = config.Bind(section, "GlowDefaultShade", 0,
                new ConfigDescription("Internal marker, leave as it is: 1 once a Shade left at the old default (0.45) has moved to the new one (0).", new AcceptableValueRange<int>(0, 1)));
            if (noBackdrop.Value < 1)
            {
                if (System.Math.Abs(Shade.Value - 0.45f) < 0.001f) Shade.Value = d.Shade;
                noBackdrop.Value = 1;
            }
            // 2026-11-17 the frosted-glass default (0.4) was tried and cut again (user: "the shadows suck"): a file still holding that untouched 0.4 goes back to 0, once.
            ConfigEntry<int> frost = config.Bind(section, "ShadowOffShade", 0,
                new ConfigDescription("Internal marker, leave as it is: 1 once a Shade left at the brief frosted default (0.4) has moved back to 0.", new AcceptableValueRange<int>(0, 1)));
            if (frost.Value < 1)
            {
                if (System.Math.Abs(Shade.Value - 0.4f) < 0.001f) Shade.Value = d.Shade;
                frost.Value = 1;
            }

            Declutter = config.Bind(section, "Declutter", d.Declutter,
                "Retired. The HUD always shows all enabled detail; saved Lean and Minimal values read as Full.");

            ScopeMode = config.Bind(section, "ScopeMode", d.ScopeMode,
                "Auto shows the board's panels while they have something to say: RWR (threats; one line while clear), FIRES (your weapons in flight, or a strike) and WING (your wingmen); Off hides them. Manual is retired and reads as Auto." + live);
            if (ScopeMode.Value != BoardScopeMode.Off) ScopeMode.Value = BoardScopeMode.Auto;
            ShowStores = config.Bind(section, "ShowStores", d.ShowStores,
                "Show stores, countermeasures and power. Off gives back the game's native panels." + live);

            PageMissileCam = config.Bind(section, "PageMissileCam", d.PageMissileCam,
                "Show the missile camera card (the newest own missile in the air) in the bottom-right corner. Off also stops its camera rendering." + live);
            TargetCam = config.Bind(section, "TargetCam", true,
                "Show the TARGET CAM card (the targeting pod's picture) in the bottom-right corner. The pod camera is the game's own and keeps feeding the cockpit tac screen." + live);

            ShowLamps = config.Bind(section, "ShowLamps", d.ShowLamps,
                "Show the lamp row: the aircraft's switch states and the threat lamps." + live);
            ShowGauges = config.Bind(section, "ShowGauges", d.ShowGauges,
                "Show the gauge row under the board's page area." + live);
            ShowTicker = config.Bind(section, "ShowTicker", d.ShowTicker,
                "Show the NOTICE line in the left column (under the cue stack). It needs MAP + FEED (Corners) on." + live);
            ShowSystems = config.Bind(section, "ShowSystems", d.ShowSystems,
                "Show the SYSTEMS block (fire, engine and fuel strip) under the lamp line. Its warnings still reach the ticker and lamps when hidden." + live);

            Sway = config.Bind(section, "Sway", d.Sway,
                "The symbology lags slightly against head movement, as if it hung in the air a little way off the glass." + live);

            Corners = config.Bind(section, "Corners", d.Corners,
                "Restyle the kill feed and the map frame in the board's look: the feed becomes the mod's own event log (DL EVT LOG), drawn while you are not typing in the chat; off leaves the game's own feed and map as they are. It stays when the board is switched off." + live);

            CentreAlerts = config.Bind(section, "CentreAlerts", d.CentreAlerts,
                "Combat alerts at the centre of the screen: a WARNING banner at the top centre (MISSILE, PULL UP, FIRE, ENGINE OUT, LOCKED, BINGO FUEL), chevrons round the reticle pointing at every missile inbound, and HIT / DESTROYED / MISSED callouts under it. It stays when the board is switched off." + live);

            const string ar = " AR helmet layer: drawn on the world, only while it matters; it stays when the board is switched off.";
            ArPath = config.Bind(section, "ArPath", d.ArPath,
                "Retired. The AR flight-path line is disabled, including in existing saved configurations.");
            ArDomes = config.Bind(section, "ArDomes", d.ArDomes,
                "Retired. SAM AR envelopes are disabled, including in existing saved configurations.");
            ArMissiles = config.Bind(section, "ArMissiles", d.ArMissiles,
                "Missile lines: your missiles joined to their targets with the seconds to impact, and every missile coming at you boxed with its next two seconds." + ar + live);
            ArBeacons = config.Bind(section, "ArBeacons", d.ArBeacons,
                "Beacons: a pillar of light over the nearest friendly field when fuel is at JOKER or BINGO or the aircraft is damaged, and over a live contract zone." + ar + live);
            HudFx = config.Bind(section, "HudFx", d.HudFx,
                "HUD reactions: brackets spin in on a new target, and the HUD glitches for a moment when a hit damages the aircraft (a still band under REDUCED MOTION)." + live);

            Brow = config.Bind(section, "Brow", d.Brow,
                "The visor's brow rail at the top centre (a curved scale with bearing marks for missiles inbound, radars on you, an ace on the hunt, the objective, the runway, the tanker and the landing zone, and a one-line status caption) and the visor frame: thin rim lines in the screen's corners, tick marks down its sides and a line of etched text along the bottom edge (helmet type, your callsign, aircraft and sortie time). Cockpit view only." + live);

            Arcs = config.Bind(section, "Arcs", d.Arcs,
                "Two segmented arcs hugging the reticle, fuel on the left and throttle on the right, in place of the game's plain ones (the game's own come back the moment this is off, or the view is not the cockpit). Cockpit view only." + live);

            Boot = config.Bind(section, "Boot", d.Boot,
                "The visor powers on when you take the seat: a short self-test of what the aircraft really reports, the visor's zones coming alive one after another, then VISOR ONLINE. The first aircraft of a session boots in full (about 3 seconds), a respawn only re-syncs (1 second). A real warning cuts it short at once. Cockpit view only." + live);

            Reactive = config.Bind(section, "Reactive", d.Reactive,
                "The visor reacts like a helmet display: the symbols shake and dim a little under heavy G, glitch for a moment when the aircraft is hit, stutter out under an EMP and re-sync after it, flash once when a warning comes up, and breathe faintly when all is calm. Off for a still HUD. Never runs with reduced motion on. Cockpit view only." + live);

            VisorSound = config.Bind(section, "VisorSound", d.VisorSound,
                new ConfigDescription("Level of the helmet visor's own sounds: the power-up whine and self-test ticks, the glitch crackle on a hit, the EMP stutter, a sharp blip when a warning comes up, the page wipe, the brow's new-contact tick, the radial menu and the radio YES/NO. They keep quiet under a real alarm or voice. 0 is silent; the interface volume still applies." + live,
                    new AcceptableValueRange<float>(0f, 1f)));

            TextEdge = config.Bind(section, "TextEdge", d.TextEdge,
                new ConfigDescription("Width of the hair-thin dark edge round every letter of the mod's HUD text, so small words stay sharp on bright sky and cloud; 0 draws none." + live,
                    new AcceptableValueRange<float>(0f, BoardOptions.TextEdgeMax)));
            BoscaliSummer.Core.Game.VanillaHudStyle.SetEdge(TextEdge.Value);
        }

        /// <summary>Fill <paramref name="into"/> from the config entries, kept inside their ranges. Reads fields only: it allocates nothing.</summary>
        public void Snapshot(ref BoardOptions into)
        {
            into.Enabled = Enabled.Value;
            into.Layout = Layout.Value;
            into.Scale = Scale.Value;
            into.Shade = Shade.Value;
            into.Declutter = Declutter.Value;
            into.ScopeMode = ScopeMode.Value;
            into.ShowStores = ShowStores.Value;
            into.PageMissileCam = PageMissileCam.Value;
            into.ShowLamps = ShowLamps.Value;
            into.ShowGauges = ShowGauges.Value;
            into.ShowTicker = ShowTicker.Value;
            into.ShowSystems = ShowSystems.Value;
            into.Sway = Sway.Value;
            into.Corners = Corners.Value;
            into.CentreAlerts = CentreAlerts.Value;
            into.ArPath = ArPath.Value;
            into.ArDomes = ArDomes.Value;
            into.ArMissiles = ArMissiles.Value;
            into.ArBeacons = ArBeacons.Value;
            into.HudFx = HudFx.Value;
            into.Brow = Brow.Value;
            into.Arcs = Arcs.Value;
            into.Boot = Boot.Value;
            into.Reactive = Reactive.Value;
            into.VisorSound = VisorSound.Value;
            into.TextEdge = TextEdge.Value;
            into.Sanitize();
        }

        // ITacBoardSettings: the entries under the contract's names. Setters persist (the config file saves on every set).
        bool ITacBoardSettings.Enabled { get => Enabled.Value; set => Enabled.Value = value; }
        BoardLayout ITacBoardSettings.Layout { get => Layout.Value; set => Layout.Value = value; }
        float ITacBoardSettings.Scale { get => Scale.Value; set => Scale.Value = value; }
        float ITacBoardSettings.Shade { get => Shade.Value; set => Shade.Value = value; }
        BoardDeclutter ITacBoardSettings.Declutter { get => BoardDeclutter.Full; set => Declutter.Value = BoardDeclutter.Full; }
        BoardScopeMode ITacBoardSettings.ScopeMode { get => ScopeMode.Value; set => ScopeMode.Value = value; }
        bool ITacBoardSettings.ShowStores { get => ShowStores.Value; set => ShowStores.Value = value; }
        bool ITacBoardSettings.PageMissileCam { get => PageMissileCam.Value; set => PageMissileCam.Value = value; }
        bool ITacBoardSettings.ShowLamps { get => ShowLamps.Value; set => ShowLamps.Value = value; }
        bool ITacBoardSettings.ShowGauges { get => ShowGauges.Value; set => ShowGauges.Value = value; }
        bool ITacBoardSettings.ShowTicker { get => ShowTicker.Value; set => ShowTicker.Value = value; }
        bool ITacBoardSettings.ShowSystems { get => ShowSystems.Value; set => ShowSystems.Value = value; }
        bool ITacBoardSettings.Sway { get => Sway.Value; set => Sway.Value = value; }
        bool ITacBoardSettings.Corners { get => Corners.Value; set => Corners.Value = value; }
        bool ITacBoardSettings.CentreAlerts { get => CentreAlerts.Value; set => CentreAlerts.Value = value; }
        bool ITacBoardSettings.ArPath { get => false; set => ArPath.Value = false; }
        bool ITacBoardSettings.ArDomes { get => false; set => ArDomes.Value = false; }
        bool ITacBoardSettings.ArMissiles { get => ArMissiles.Value; set => ArMissiles.Value = value; }
        bool ITacBoardSettings.ArBeacons { get => ArBeacons.Value; set => ArBeacons.Value = value; }
        bool ITacBoardSettings.HudFx { get => HudFx.Value; set => HudFx.Value = value; }
        bool ITacBoardSettings.Brow { get => Brow.Value; set => Brow.Value = value; }
        bool ITacBoardSettings.Arcs { get => Arcs.Value; set => Arcs.Value = value; }
        bool ITacBoardSettings.Boot { get => Boot.Value; set => Boot.Value = value; }
        bool ITacBoardSettings.Reactive { get => Reactive.Value; set => Reactive.Value = value; }
        float ITacBoardSettings.VisorSound { get => VisorSound.Value; set => VisorSound.Value = value; }
        float ITacBoardSettings.TextEdge { get => TextEdge.Value; set => TextEdge.Value = value; }

        /// <summary>Every entry back to the default it was bound with (the <see cref="BoardOptions.Default"/> values). The file is saved once, not once per entry.</summary>
        void ITacBoardSettings.ResetToDefaults()
        {
            ConfigEntryBase[] all =
            {
                Enabled, Layout, Scale, Shade, Declutter, ScopeMode, ShowStores,
                PageMissileCam, TargetCam,
                ShowLamps, ShowGauges, ShowTicker, ShowSystems, Sway, Corners, CentreAlerts,
                ArPath, ArDomes, ArMissiles, ArBeacons, HudFx,
                Brow, Arcs, Boot, Reactive, VisorSound, TextEdge,
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
