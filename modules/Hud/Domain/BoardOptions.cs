using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// What the TAC BOARD may show once the declutter level is applied on top of the switches (<see cref="BoardOptions.Effective"/>). A level only ever takes things away: a
    /// block whose switch is off stays off at every level. The restriction flags (<see cref="DarkLamps"/> false, <see cref="ThreatLampsOnly"/>, the two SYSTEMS strip ones) say HOW a
    /// block that is on is drawn; they hold whether or not the block's own switch is on.
    /// </summary>
    internal struct BoardEffective
    {
        /// <summary>All registered pages may post notices or call for a dispenser, independently of the visible panels.</summary>
        public ushort Voiced;
        public bool ShowStores;
        public bool ShowLamps;
        /// <summary>Unlit lamps are drawn (as dim outlines). False: only a lamp that is lit shows.</summary>
        public bool DarkLamps;
        /// <summary>Of the lamps only the threat ones: LOCK, SPKE and MSL.</summary>
        public bool ThreatLampsOnly;
        /// <summary>SYSTEMS notices speak only in their critical form (Minimal). The block itself shows by <see cref="ShowSystems"/>, at every declutter level.</summary>
        public bool SystemsCriticalOnly;
        public bool ShowSystems;
        public bool ShowGauges;
        public bool ShowTicker;
        /// <summary>Decorative captions and shelves are drawn (the painters' <c>ScopeCanvas.Furniture</c> flag; brackets, ticks and markers stay at every level).</summary>
        public bool Furniture;

        /// <summary>A page may post a notice of <paramref name="tone"/> to the ticker: SYSTEMS at Minimal only its warnings (a fire, an engine out, BINGO), its cautions stay quiet.</summary>
        public readonly bool Posts(ScopePage page, HudTone tone) => page != ScopePage.Systems || !SystemsCriticalOnly || tone == HudTone.Warning;
    }

    /// <summary>
    /// The pilot's TAC BOARD options as one plain value, pure (no Unity, no BepInEx: it compiles into the unit tests). <c>HudSettings.Snapshot</c> fills one per 10 Hz tick from the
    /// config entries; <see cref="Sanitize"/> keeps whatever a hand-edited file or a settings page wrote inside the ranges; <see cref="Effective"/> applies the declutter rules.
    /// The ranges and defaults here are the config entries' (single source).
    /// </summary>
    internal struct BoardOptions
    {
        public const float ScaleMin = 0.6f, ScaleMax = 1.4f;

        /// <summary>Master switch. Off hides the board and gives back the native weapon, countermeasure and power panels.</summary>
        public bool Enabled;
        public BoardLayout Layout;
        public float Scale, Shade;
        public BoardDeclutter Declutter;
        public BoardScopeMode ScopeMode;
        public bool ShowStores;
        public bool PageMissileCam;
        public bool ShowLamps, ShowGauges, ShowTicker, ShowSystems;
        public bool Sway, Corners;
        /// <summary>The centre combat layer (2026-10-28): the WARNING banner, the THREAT RING round the reticle and the shot callouts. Independent of <see cref="Enabled"/>.</summary>
        public bool CentreAlerts;
        /// <summary>The AR helmet layer (2026-11-14, <c>ArLayer</c>): flight path predictor, SAM envelopes, missile lines, home/objective beacons, and the HUD's reactions (lock spin-in, hit glitch). Independent of <see cref="Enabled"/>.</summary>
        public bool ArPath, ArDomes, ArMissiles, ArBeacons, HudFx;
        /// <summary>The visor's brow rail and frame furniture (2026-11-16). Independent of <see cref="Enabled"/>.</summary>
        public bool Brow;
        /// <summary>The two segmented fuel and throttle arcs round the reticle in place of the game's plain ones (2026-11-16). Cockpit view only. Independent of <see cref="Enabled"/>.</summary>
        public bool Arcs;
        /// <summary>The visor's power-on sequence when the pilot takes the seat (2026-11-16, "HELMET ON"): a full boot the first time, a short re-sync after a respawn. Cockpit view only. Independent of <see cref="Enabled"/>.</summary>
        public bool Boot;
        /// <summary>The visor reacts to what happens to the pilot and the jet (2026-11-16): G load, hits, EMP, warning onset, and a faint idle life. Cockpit view only. Independent of <see cref="Enabled"/>.</summary>
        public bool Reactive;
        /// <summary>Level of the visor's own sounds (the HMD-9 family: power-up, self-test, glitch, EMP, page wipe, brow, radial menu), 0 (off) to 1.</summary>
        public float VisorSound;
        /// <summary>Width of the thin dark edge round every glyph of the mod's HUD text, 0 (off) to <see cref="TextEdgeMax"/> (TMP outline units).</summary>
        public float TextEdge;
        public const float TextEdgeMax = 0.25f;

        /// <summary>What a pilot gets who has touched nothing. (A zeroed struct is NOT this: it has the board off.)</summary>
        public static BoardOptions Default => new BoardOptions
        {
            Enabled = true, Layout = BoardLayout.Wide, Scale = 0.85f, Shade = 0f,
            Declutter = BoardDeclutter.Full, ScopeMode = BoardScopeMode.Auto,
            ShowStores = true,
            PageMissileCam = true,
            ShowLamps = true, ShowGauges = true, ShowTicker = true, ShowSystems = true,
            ArPath = false, ArDomes = false, ArMissiles = true, ArBeacons = true, HudFx = true,
            Sway = true, Corners = true, CentreAlerts = true, Brow = true, Arcs = true, Boot = true, Reactive = true, VisorSound = 0.6f, TextEdge = 0.16f,
        };

        /// <summary>Clamp every range and replace NaN (and an enum that names nothing) with the default.</summary>
        public void Sanitize()
        {
            BoardOptions d = Default;
            ArPath = false;       // Retired: the green prediction line must stay off for saved configs too.
            ArDomes = false;      // Retired: saved configs must not bring the SAM wire cages back.
            Scale = Fit(Scale, ScaleMin, ScaleMax, d.Scale);
            Shade = Fit(Shade, 0f, 1f, d.Shade);
            TextEdge = Fit(TextEdge, 0f, TextEdgeMax, d.TextEdge);
            VisorSound = Fit(VisorSound, 0f, 1f, d.VisorSound);
            if ((uint)Layout > (uint)BoardLayout.Stack) Layout = d.Layout;
            Declutter = BoardDeclutter.Full;
            // The panels show themselves (2026-11-16): a saved Manual, or a value that names nothing, reads as Auto.
            if (ScopeMode != BoardScopeMode.Off) ScopeMode = BoardScopeMode.Auto;
        }

        private static float Fit(float value, float min, float max, float fallback) =>
            float.IsNaN(value) ? fallback : value < min ? min : value > max ? max : value;

        /// <summary>
        /// The major sections with declutter applied to their detail. A scope that is Off hides the panels (the painter reads
        /// <see cref="ScopeMode"/>) while every feed keeps reporting alerts.
        /// </summary>
        public readonly BoardEffective Effective()
        {
            return new BoardEffective
            {
                Voiced = (ushort)((1 << CuePriority.PageCount) - 1),
                ShowStores = ShowStores,
                ShowLamps = ShowLamps,
                DarkLamps = true,
                ThreatLampsOnly = false,
                SystemsCriticalOnly = false,
                ShowGauges = ShowGauges,
                ShowTicker = ShowTicker,
                ShowSystems = ShowSystems,
                Furniture = true,
            };
        }
    }
}
