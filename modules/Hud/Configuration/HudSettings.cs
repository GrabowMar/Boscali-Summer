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
        public ConfigEntry<int> PageCount { get; }
        public ConfigEntry<BoardPage> Page1 { get; }
        public ConfigEntry<BoardPage> Page2 { get; }
        public ConfigEntry<BoardPage> Page3 { get; }
        public ConfigEntry<BoardPage> Page4 { get; }
        public ConfigEntry<bool> ShowStores { get; }
        public ConfigEntry<bool> PageMissileCam { get; }
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

            Declutter = config.Bind(section, "Declutter", d.Declutter,
                "Full shows all enabled detail; Lean drops gauges, dark lamps and decoration; Minimal keeps threat lamps and critical supplementary status. Manual pages and their titles remain available at every level." + live);

            ScopeMode = config.Bind(section, "ScopeMode", d.ScopeMode,
                "Manual keeps the pages chosen for each slot; Off hides the tactical pages. Legacy Auto also means Manual." + live);
            PageCount = config.Bind(section, "PageCount", d.PageCount,
                new ConfigDescription("Full information pages shown, at most two across and two down (Wide puts two side by side, Stack one above the other). Each slot is selected manually; SYSTEMS always shows on its own." + live,
                    new AcceptableValueRange<int>(1, BoardOptions.MaxPages)));
            const string slot = " SYSTEMS is not a choice: it always shows in its own block.";
            Page1 = config.Bind(section, "Page1", d.Page1, "Information page in the first slot; events never replace it." + slot + live);
            Page2 = config.Bind(section, "Page2", d.Page2, "Information page in the second slot (PageCount at least 2)." + slot + live);
            Page3 = config.Bind(section, "Page3", d.Page3, "Information page in the third slot (PageCount at least 3)." + slot + live);
            Page4 = config.Bind(section, "Page4", d.Page4, "Information page in the fourth slot (PageCount 4)." + slot + live);
            ShowStores = config.Bind(section, "ShowStores", d.ShowStores,
                "Show stores, countermeasures and power. Off gives back the game's native panels." + live);

            PageMissileCam = config.Bind(section, "PageMissileCam", d.PageMissileCam,
                "Show the missile camera card (the newest own missile in the air) beside the board." + live);

            ShowLamps = config.Bind(section, "ShowLamps", d.ShowLamps,
                "Show the lamp row: the aircraft's switch states and the threat lamps." + live);
            ShowGauges = config.Bind(section, "ShowGauges", d.ShowGauges,
                "Show the gauge row under the board's page area." + live);
            ShowTicker = config.Bind(section, "ShowTicker", d.ShowTicker,
                "Show the notice line at the foot of the board." + live);
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
                "Flight path predictor: where the aircraft will be over the next 8 seconds, during a hard pull, and in red with TERRAIN and the seconds left when that path meets the ground." + ar + live);
            ArDomes = config.Bind(section, "ArDomes", d.ArDomes,
                "SAM envelopes: the reach of every hostile SAM your side knows about, drawn as a wire envelope once you are near it, the ring at your own height bold; IN SAM ENVELOPE inside." + ar + live);
            ArMissiles = config.Bind(section, "ArMissiles", d.ArMissiles,
                "Missile lines: your missiles joined to their targets with the seconds to impact, and every missile coming at you boxed with its next two seconds." + ar + live);
            ArBeacons = config.Bind(section, "ArBeacons", d.ArBeacons,
                "Beacons: a pillar of light over the nearest friendly field when fuel is at JOKER or BINGO or the aircraft is damaged, and over a live contract zone." + ar + live);
            HudFx = config.Bind(section, "HudFx", d.HudFx,
                "HUD reactions: brackets spin in on a new target, and the HUD glitches for a moment when a hit damages the aircraft (a still band under REDUCED MOTION)." + live);

            // Keep settings surfaces and the renderer in agreement after an invalid hand-edited page configuration.
            BoardOptions saved = d;
            Snapshot(ref saved);
            Page1.Value = saved.Page1;
            Page2.Value = saved.Page2;
            Page3.Value = saved.Page3;
            Page4.Value = saved.Page4;
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
            into.PageCount = PageCount.Value;
            into.Page1 = Page1.Value;
            into.Page2 = Page2.Value;
            into.Page3 = Page3.Value;
            into.Page4 = Page4.Value;
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
            into.Sanitize();
        }

        // ITacBoardSettings: the entries under the contract's names. Setters persist (the config file saves on every set).
        bool ITacBoardSettings.Enabled { get => Enabled.Value; set => Enabled.Value = value; }
        BoardLayout ITacBoardSettings.Layout { get => Layout.Value; set => Layout.Value = value; }
        float ITacBoardSettings.Scale { get => Scale.Value; set => Scale.Value = value; }
        float ITacBoardSettings.Shade { get => Shade.Value; set => Shade.Value = value; }
        BoardDeclutter ITacBoardSettings.Declutter { get => Declutter.Value; set => Declutter.Value = value; }
        BoardScopeMode ITacBoardSettings.ScopeMode { get => ScopeMode.Value; set => ScopeMode.Value = value; }
        int ITacBoardSettings.PageCount { get => PageCount.Value; set => PageCount.Value = value; }
        BoardPage ITacBoardSettings.Page1 { get => Page1.Value; set => Page1.Value = value; }
        BoardPage ITacBoardSettings.Page2 { get => Page2.Value; set => Page2.Value = value; }
        BoardPage ITacBoardSettings.Page3 { get => Page3.Value; set => Page3.Value = value; }
        BoardPage ITacBoardSettings.Page4 { get => Page4.Value; set => Page4.Value = value; }
        bool ITacBoardSettings.ShowStores { get => ShowStores.Value; set => ShowStores.Value = value; }
        bool ITacBoardSettings.PageMissileCam { get => PageMissileCam.Value; set => PageMissileCam.Value = value; }
        bool ITacBoardSettings.ShowLamps { get => ShowLamps.Value; set => ShowLamps.Value = value; }
        bool ITacBoardSettings.ShowGauges { get => ShowGauges.Value; set => ShowGauges.Value = value; }
        bool ITacBoardSettings.ShowTicker { get => ShowTicker.Value; set => ShowTicker.Value = value; }
        bool ITacBoardSettings.ShowSystems { get => ShowSystems.Value; set => ShowSystems.Value = value; }
        bool ITacBoardSettings.Sway { get => Sway.Value; set => Sway.Value = value; }
        bool ITacBoardSettings.Corners { get => Corners.Value; set => Corners.Value = value; }
        bool ITacBoardSettings.CentreAlerts { get => CentreAlerts.Value; set => CentreAlerts.Value = value; }
        bool ITacBoardSettings.ArPath { get => ArPath.Value; set => ArPath.Value = value; }
        bool ITacBoardSettings.ArDomes { get => ArDomes.Value; set => ArDomes.Value = value; }
        bool ITacBoardSettings.ArMissiles { get => ArMissiles.Value; set => ArMissiles.Value = value; }
        bool ITacBoardSettings.ArBeacons { get => ArBeacons.Value; set => ArBeacons.Value = value; }
        bool ITacBoardSettings.HudFx { get => HudFx.Value; set => HudFx.Value = value; }

        /// <summary>Every entry back to the default it was bound with (the <see cref="BoardOptions.Default"/> values). The file is saved once, not once per entry.</summary>
        void ITacBoardSettings.ResetToDefaults()
        {
            ConfigEntryBase[] all =
            {
                Enabled, Layout, Scale, Shade, Declutter, ScopeMode, PageCount, Page1, Page2, Page3, Page4, ShowStores,
                PageMissileCam,
                ShowLamps, ShowGauges, ShowTicker, ShowSystems, Sway, Corners, CentreAlerts,
                ArPath, ArDomes, ArMissiles, ArBeacons, HudFx,
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
